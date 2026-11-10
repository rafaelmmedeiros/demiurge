using Demiurge.Agents;
using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Harness;

/// <summary>
/// Runs the agent loop for one tick: perceive → decide (all brains in parallel, with a deadline)
/// → act (World.Step validates and applies). A failing or slow brain only idles its own agent
/// for that tick; the tick always closes (CLAUDE.md rule 6).
/// Concept: Agent and the perception → decision → action loop —
///          docs/CONCEPTS.md#agente-e-o-laço-percepção--decisão--ação
/// </summary>
public sealed class TickRunner
{
    private readonly SimulationSession _session;
    private readonly Func<AgentId, IAgentBrain> _brainFactory;
    private readonly Dictionary<AgentId, IAgentBrain> _brains = []; // lookups only, never iterated
    private readonly TimeSpan _decisionTimeout;
    private readonly TimeProvider _clock;

    public TickRunner(
        SimulationSession session,
        Func<AgentId, IAgentBrain> brainFactory,
        TimeSpan decisionTimeout,
        TimeProvider clock)
    {
        _session = session;
        _brainFactory = brainFactory;
        _decisionTimeout = decisionTimeout;
        _clock = clock;
    }

    public SimulationSession Session => _session;

    public async Task<TickOutcome> RunTickAsync(CancellationToken ct = default)
    {
        var world = _session.World;

        // Only agents that can act are asked: alive and standing at a place (in transit = no decision).
        var actors = world.Agents
            .Where(a => a.IsAlive && a.Location is AtPlace)
            .Select(a => (a.Id, Perception: world.Perceive(a.Id)))
            .ToList();

        // Wall-clock deadline lives here, in the harness, never in the engine. A timeout changes
        // which decisions reach the engine, but the engine still records every result as events.
        using var timeout = new CancellationTokenSource(_decisionTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        var results = await Task.WhenAll(actors.Select(a => DecideSafelyAsync(a.Id, a.Perception, linked.Token)));
        ct.ThrowIfCancellationRequested(); // host shutting down: do not apply a half-collected tick

        // Completion order is irrelevant: the engine sorts by id and shuffles with the seed.
        var decisions = results
            .Where(r => r.Action is not null)
            .Select(r => new AgentDecision(r.AgentId, r.Action!))
            .ToList();
        var failures = results.Where(r => r.Failure is not null).Select(r => r.Failure!).ToList();

        return new TickOutcome(_session.Step(decisions), failures);
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
            return DecisionResult.Failed(id, BrainFailureKind.Timeout, $"No decision within {_decisionTimeout.TotalMilliseconds} ms.");
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

/// <summary>What a tick produced in the world, plus what went wrong in the brains (input for pain detectors).</summary>
public sealed record TickOutcome(TickReport Report, IReadOnlyList<BrainFailure> Failures);

public sealed record BrainFailure(AgentId AgentId, BrainFailureKind Kind, string Detail);

public enum BrainFailureKind
{
    Exception,
    Timeout,
    NullAction,
}
