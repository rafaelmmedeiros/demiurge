using System.Diagnostics;
using Demiurge.Agents;
using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Tests;

namespace Demiurge.Harness.Tests;

/// <summary>
/// Perceive → decide (parallel, with deadline, failures isolated) → act.
/// Concept: Agent and the perception → decision → action loop —
///          docs/CONCEPTS.md#agente-e-o-laço-percepção--decisão--ação
/// </summary>
public class TickRunnerTests
{
    private static readonly WorldConfig Config = new() { Seed = 42 };
    private static readonly AgentId A2 = new(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- test brains ----------

    private sealed class ScriptedBrain : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct) =>
            Task.FromResult(ScriptedPolicy.Decide(p) ?? new Rest());
    }

    private sealed class ThrowingBrain : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct) =>
            throw new InvalidOperationException("brain exploded");
    }

    /// <summary>Ignores the token and never answers: the worst case for the tick.</summary>
    private sealed class HangingBrain : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct) =>
            new TaskCompletionSource<AgentAction>().Task;
    }

    private sealed class NullBrain : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct) => Task.FromResult<AgentAction>(null!);
    }

    private static TickRunner Runner(TempDatabase db, Func<AgentId, IAgentBrain> brains, int timeoutMs = 1000) =>
        new(SimulationSession.Start(db.Store, Config, TimeProvider.System), brains,
            TimeSpan.FromMilliseconds(timeoutMs), TimeProvider.System);

    // ---------- tests ----------

    [Fact]
    public async Task Runner_produces_the_same_world_as_calling_the_engine_directly()
    {
        using var db = new TempDatabase();
        var runner = Runner(db, _ => new ScriptedBrain());

        var (direct, _) = World.Create(Config);
        for (int i = 0; i < 100; i++)
        {
            var viaRunner = (await runner.RunTickAsync(Ct)).Report.Events.Select(e => e.ToString());
            var viaEngine = direct.Step(ScriptedPolicy.DecideAll(direct)).Events.Select(e => e.ToString());
            Assert.Equal(viaEngine, viaRunner);
        }
    }

    [Fact]
    public async Task Throwing_brain_idles_only_its_agent_and_the_tick_closes()
    {
        using var db = new TempDatabase();
        var runner = Runner(db, id => id == A2 ? new ThrowingBrain() : new ScriptedBrain());

        var outcome = await runner.RunTickAsync(Ct);

        var failure = Assert.Single(outcome.Failures);
        Assert.Equal((A2, BrainFailureKind.Exception), (failure.AgentId, failure.Kind));
        Assert.Contains("brain exploded", failure.Detail);
        // At tick 0 the scripted policy makes everyone talk; the other 4 did, agent-2 did nothing.
        var speakers = outcome.Report.Events.OfType<Talked>().Select(t => t.Speaker).ToList();
        Assert.Equal(4, speakers.Count);
        Assert.DoesNotContain(A2, speakers);
        Assert.IsType<TickEnded>(outcome.Report.Events[^1]);
    }

    [Fact]
    public async Task Hanging_brain_is_cut_at_the_deadline()
    {
        using var db = new TempDatabase();
        var runner = Runner(db, id => id == A2 ? new HangingBrain() : new ScriptedBrain(), timeoutMs: 100);

        var clock = Stopwatch.StartNew();
        var outcome = await runner.RunTickAsync(Ct);

        Assert.True(clock.ElapsedMilliseconds < 2000, $"tick took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(BrainFailureKind.Timeout, Assert.Single(outcome.Failures).Kind);
        Assert.Equal(1, runner.Session.World.Tick);
    }

    [Fact]
    public async Task Null_action_is_a_failure_not_a_crash()
    {
        using var db = new TempDatabase();
        var runner = Runner(db, id => id == A2 ? new NullBrain() : new ScriptedBrain());

        var outcome = await runner.RunTickAsync(Ct);

        Assert.Equal(BrainFailureKind.NullAction, Assert.Single(outcome.Failures).Kind);
    }

    [Fact]
    public async Task Agents_in_transit_are_not_asked_to_decide()
    {
        using var db = new TempDatabase();
        var asked = new List<long>();
        var runner = Runner(db, id => new RecordingBrain(id == A2 ? asked : null));

        await runner.RunTickAsync(Ct); // tick 1: everyone starts home → workplace (2 ticks); in transit during tick 2
        await runner.RunTickAsync(Ct);
        await runner.RunTickAsync(Ct);

        Assert.Equal([0L, 2L], asked); // perceived at tick 0 and tick 2; skipped while on the road
    }

    [Fact]
    public async Task Brains_decide_in_parallel()
    {
        // Each brain waits until all 5 have started. Sequential calls would deadlock and time out.
        using var db = new TempDatabase();
        var gate = new Gate(5);
        var runner = Runner(db, _ => new GateBrain(gate), timeoutMs: 2000);

        var outcome = await runner.RunTickAsync(Ct);

        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task Each_agent_gets_one_brain_instance()
    {
        using var db = new TempDatabase();
        int created = 0;
        var runner = Runner(db, _ => { Interlocked.Increment(ref created); return new ScriptedBrain(); });

        for (int i = 0; i < 10; i++) await runner.RunTickAsync(Ct);

        Assert.Equal(5, created);
    }

    private sealed class RecordingBrain(List<long>? log) : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct)
        {
            lock (this) log?.Add(p.Tick);
            return Task.FromResult<AgentAction>(p.Location is AtPlace { Place: var here } && here == PlacesGraph.Workplace
                ? new Work()
                : new MoveTo(PlacesGraph.Workplace));
        }
    }

    private sealed class Gate(int count)
    {
        private int _arrived;
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ArriveAndWait(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == count) _open.SetResult();
            return _open.Task.WaitAsync(ct);
        }
    }

    private sealed class GateBrain(Gate gate) : IAgentBrain
    {
        public async Task<AgentAction> DecideAsync(Perception p, CancellationToken ct)
        {
            await gate.ArriveAndWait(ct);
            return new Rest();
        }
    }
}
