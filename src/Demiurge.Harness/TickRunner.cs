using Demiurge.Agents;
using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Harness.Pains;

namespace Demiurge.Harness;

/// <summary>Timing of the loop. Decision deadline defaults to 80% of the tick interval.</summary>
public sealed record TickRunnerOptions(TimeSpan TickInterval, TimeSpan DecisionTimeout)
{
    public static TickRunnerOptions FromInterval(TimeSpan interval) => new(interval, interval * 0.8);
}

/// <summary>
/// Runs the agent loop for one tick: perceive → decide (all brains in parallel, with a deadline)
/// → act (World.Step validates and applies) → measure (pain detectors). A failing or slow brain
/// only idles its own agent for that tick; the tick always closes (CLAUDE.md rule 6).
/// Concepts: Agent and the perception → decision → action loop —
///           docs/CONCEPTS.md#agente-e-o-laço-percepção--decisão--ação ;
///           Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor
/// </summary>
public sealed class TickRunner
{
    private readonly ISimulation _simulation;
    private readonly Func<AgentId, IAgentBrain> _brainFactory;
    private readonly Dictionary<AgentId, IAgentBrain> _brains = []; // lookups only, never iterated
    private readonly TickRunnerOptions _options;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyList<IPainDetector> _detectors;

    public TickRunner(
        ISimulation simulation,
        Func<AgentId, IAgentBrain> brainFactory,
        TickRunnerOptions options,
        TimeProvider clock,
        IReadOnlyList<IPainDetector>? detectors = null,
        PainRegistry? pains = null)
    {
        _simulation = simulation;
        _brainFactory = brainFactory;
        _options = options;
        _clock = clock;
        _detectors = detectors ?? [];
        Pains = pains ?? new PainRegistry();
    }

    public ISimulation Simulation => _simulation;

    public PainRegistry Pains { get; }

    public async Task<TickOutcome> RunTickAsync(CancellationToken ct = default)
    {
        long started = _clock.GetTimestamp();
        var world = _simulation.World;

        // Only agents that can act are asked: alive and standing at a place (in transit = no decision).
        var actors = world.Agents
            .Where(a => a.IsAlive && a.Location is AtPlace)
            .Select(a => (a.Id, Perception: world.Perceive(a.Id)))
            .ToList();

        // Wall-clock deadline lives here, in the harness, never in the engine. A timeout changes
        // which decisions reach the engine, but the engine still records every result as events.
        using var timeout = new CancellationTokenSource(_options.DecisionTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        var results = await Task.WhenAll(actors.Select(a => DecideSafelyAsync(a.Id, a.Perception, linked.Token)));
        ct.ThrowIfCancellationRequested(); // host shutting down: do not apply a half-collected tick

        // Completion order is irrelevant: the engine sorts by id and shuffles with the seed.
        var decisions = results
            .Where(r => r.Action is not null)
            .Select(r => new AgentDecision(r.AgentId, r.Action!))
            .ToList();
        var failures = results.Where(r => r.Failure is not null).Select(r => r.Failure!).ToList();

        var report = _simulation.Step(decisions);
        var duration = _clock.GetElapsedTime(started);

        var context = new TickContext(report, decisions, failures, duration, _options.TickInterval,
            world.ToSnapshot(), world.Config);
        var signals = Detect(context);
        Pains.Record(signals);

        return new TickOutcome(report, failures, duration, signals);
    }

    /// <summary>Runs every detector; a broken detector is skipped, it never breaks the tick.</summary>
    private List<PainSignal> Detect(TickContext context)
    {
        var signals = new List<PainSignal>();
        foreach (var detector in _detectors)
        {
            try
            {
                signals.AddRange(detector.Inspect(context));
            }
            catch (Exception ex)
            {
                signals.Add(new PainSignal(
                    PainCodes.TickException, PainCategory.Technical, 4, context.Tick, null,
                    $"Detector {detector.GetType().Name} failed at tick {context.Tick}: {ex.GetType().Name}: {ex.Message}",
                    "Detector skipped for this tick."));
            }
        }

        return signals;
    }

    private async Task<DecisionResult> DecideSafelyAsync(AgentId id, Perception perception, CancellationToken token)
    {
        try
        {
            // WaitAsync stops waiting at the deadline even if the brain ignores the token.
            var action = await BrainFor(id).DecideAsync(perception, token).WaitAsync(token);
            return action is null
                ? DecisionResult.Failed(id, BrainFailureKind.NullAction, "Brain returned null.")
                : new DecisionResult(id, action, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return DecisionResult.Failed(id, BrainFailureKind.Timeout,
                $"No decision within {_options.DecisionTimeout.TotalMilliseconds:F0} ms.");
        }
        catch (Exception ex)
        {
            return DecisionResult.Failed(id, BrainFailureKind.Exception, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private IAgentBrain BrainFor(AgentId id)
    {
        if (!_brains.TryGetValue(id, out var brain))
            _brains[id] = brain = _brainFactory(id);
        return brain;
    }

    private sealed record DecisionResult(AgentId AgentId, AgentAction? Action, BrainFailure? Failure)
    {
        public static DecisionResult Failed(AgentId id, BrainFailureKind kind, string detail) =>
            new(id, null, new BrainFailure(id, kind, detail));
    }
}

/// <summary>What a tick produced: world events, brain failures, wall-clock duration, pain signals.</summary>
public sealed record TickOutcome(
    TickReport Report,
    IReadOnlyList<BrainFailure> Failures,
    TimeSpan Duration,
    IReadOnlyList<PainSignal> Pains);

public sealed record BrainFailure(AgentId AgentId, BrainFailureKind Kind, string Detail);

public enum BrainFailureKind
{
    Exception,
    Timeout,
    NullAction,
}
