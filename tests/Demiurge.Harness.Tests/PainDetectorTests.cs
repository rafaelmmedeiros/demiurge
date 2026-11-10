using Demiurge.Agents;
using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Harness.Pains;

namespace Demiurge.Harness.Tests;

/// <summary>
/// Each detector against a scenario that must trigger it and, where useful, one that must not.
/// Concept: Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor
/// </summary>
public class PainDetectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FuncBrain(Func<Perception, AgentAction> decide) : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct) => Task.FromResult(decide(p));
    }

    private sealed class ThrowingBrain : IAgentBrain
    {
        public Task<AgentAction> DecideAsync(Perception p, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    private static TickRunner Runner(WorldConfig config, Func<Perception, AgentAction> decide) =>
        Runner(config, _ => new FuncBrain(decide));

    private static TickRunner Runner(WorldConfig config, Func<AgentId, IAgentBrain> brains) =>
        new(InMemorySimulation.Start(config), brains, TickRunnerOptions.FromInterval(TimeSpan.FromSeconds(5)),
            TimeProvider.System, PainDetectors.CreateDefault());

    private static async Task<List<PainSignal>> RunAsync(TickRunner runner, int ticks)
    {
        var signals = new List<PainSignal>();
        for (int i = 0; i < ticks; i++)
            signals.AddRange((await runner.RunTickAsync(Ct)).Pains);
        return signals;
    }

    private static List<PainSignal> Of(List<PainSignal> signals, string code) => signals.Where(s => s.Code == code).ToList();

    // ---------- STARVATION_PARADOX ----------

    [Fact]
    public async Task Starving_with_money_and_stock_is_a_paradox()
    {
        var runner = Runner(new WorldConfig { AgentCount = 1, InitialHunger = (96, 97), InitialMoney = (50, 51) }, _ => new Rest());

        var signal = Assert.Single(Of(await RunAsync(runner, 20), PainCodes.StarvationParadox));

        Assert.Equal(5, signal.Severity);
        Assert.Contains("money 50", signal.Sample);
    }

    [Fact]
    public async Task Starving_broke_is_not_a_paradox()
    {
        var runner = Runner(new WorldConfig { AgentCount = 1, InitialHunger = (96, 97), InitialMoney = (0, 1) }, _ => new Rest());

        Assert.Empty(Of(await RunAsync(runner, 20), PainCodes.StarvationParadox));
    }

    // ---------- RESOURCE_COLLAPSE ----------

    [Fact]
    public async Task Half_the_population_dead_is_a_collapse_reported_once()
    {
        var runner = Runner(new WorldConfig { AgentCount = 2, InitialHunger = (96, 97) }, _ => new Rest());

        var signals = Of(await RunAsync(runner, 40), PainCodes.ResourceCollapse);

        Assert.Equal(5, Assert.Single(signals).Severity); // edge-triggered: once, not every tick after
    }

    [Fact]
    public void Gini_is_0_for_equal_and_high_for_concentrated()
    {
        Assert.Equal(0, ResourceCollapseDetector.Gini([10, 10, 10, 10]));
        Assert.Equal(0.75, ResourceCollapseDetector.Gini([0, 0, 0, 100]), 3);
    }

    // ---------- NO_SOCIAL_CONTACT ----------

    private static readonly WorldConfig Fed = new() { AgentCount = 2, InitialHunger = (0, 1), InitialFood = (10, 11) };

    [Fact]
    public async Task Silent_agents_are_reported_once_each_not_every_tick()
    {
        // Enough food to stay alive for 100 ticks: silence, not death, must be what is measured.
        var runner = Runner(Fed, p => p.Hunger >= 60 ? new Eat() : new Rest());

        var signals = Of(await RunAsync(runner, 100), PainCodes.NoSocialContact);
        Assert.All(runner.Simulation.World.Agents, a => Assert.True(a.IsAlive));

        Assert.Equal(2, signals.Count);
        Assert.All(signals, s => Assert.Equal(48, s.Tick));
    }

    [Fact]
    public async Task Talking_agents_have_no_social_pain()
    {
        var runner = Runner(Fed,
            p => p.PeoplePresent.Count > 0 && p.Tick % 20 == 0 ? new Talk(p.PeoplePresent[0], "oi")
                : p.Hunger >= 60 ? new Eat() : new Rest());

        Assert.Empty(Of(await RunAsync(runner, 100), PainCodes.NoSocialContact));
        Assert.All(runner.Simulation.World.Agents, a => Assert.True(a.IsAlive));
    }

    // ---------- GOAL_OSCILLATION ----------

    [Fact]
    public async Task Arriving_and_leaving_without_acting_is_oscillation()
    {
        // Market <-> Square cost 1: arrive, then leave the next tick, forever.
        var runner = Runner(new WorldConfig { AgentCount = 1, InitialHunger = (0, 1), StartPlace = PlacesGraph.Square },
            p => new MoveTo(p.Location is AtPlace { Place: var here } && here == PlacesGraph.Market ? PlacesGraph.Square : PlacesGraph.Market));

        var signal = Assert.Single(Of(await RunAsync(runner, 20), PainCodes.GoalOscillation));

        Assert.Contains("5 of 5 arrivals", signal.Sample);
    }

    [Fact]
    public async Task Going_somewhere_and_working_is_not_oscillation()
    {
        var runner = Runner(new WorldConfig { AgentCount = 1, InitialHunger = (0, 1) },
            p => p.Location is AtPlace { Place: var here } && here == PlacesGraph.Workplace
                ? (p.Energy >= WorldRules.WorkEnergyCost ? new Work() : new Rest())
                : new MoveTo(PlacesGraph.Workplace));

        Assert.Empty(Of(await RunAsync(runner, 30), PainCodes.GoalOscillation));
    }

    // ---------- REPETITION_LOOP and ACTION_REJECTION_RATE ----------

    [Fact]
    public async Task Buying_at_home_forever_is_a_loop_and_a_rejection_problem()
    {
        var runner = Runner(new WorldConfig { AgentCount = 1, InitialHunger = (0, 1), InitialEnergy = (100, 101) }, _ => new Buy());

        var signals = await RunAsync(runner, 15);

        Assert.Equal(8, Assert.Single(Of(signals, PainCodes.RepetitionLoop)).Tick);
        var rejection = Assert.Single(Of(signals, PainCodes.ActionRejectionRate));
        Assert.Equal(10, rejection.Tick);
        Assert.Contains("NotAtRequiredPlace", rejection.Sample);
    }

    [Fact]
    public async Task Repeating_an_action_that_improves_a_resource_is_not_a_loop()
    {
        // Working every tick raises money every tick.
        var runner = Runner(new WorldConfig { AgentCount = 1, InitialHunger = (0, 1), InitialEnergy = (100, 101), StartPlace = PlacesGraph.Workplace },
            _ => new Work());

        Assert.Empty(Of(await RunAsync(runner, 9), PainCodes.RepetitionLoop));
    }

    // ---------- Technical ----------

    [Fact]
    public async Task Throwing_brain_is_a_mitigated_tick_exception()
    {
        var runner = Runner(new WorldConfig { AgentCount = 2 }, id => id.Value == 2 ? new ThrowingBrain() : new FuncBrain(_ => new Rest()));

        await RunAsync(runner, 3);

        var pain = Assert.Single(runner.Pains.Ranked(), p => p.Code == PainCodes.TickException);
        Assert.Equal((3, PainStatus.Mitigated, 1L, 3L), (pain.Count, pain.Status, pain.FirstTick, pain.LastTick));
    }

    [Fact]
    public void Slow_tick_is_an_overrun()
    {
        var (world, _) = World.Create(new WorldConfig());
        var report = world.Step();
        var ctx = new TickContext(report, [], [], TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(1), world.ToSnapshot(), world.Config);

        var signal = Assert.Single(new TickOverrunDetector().Inspect(ctx));

        Assert.Equal(PainCodes.TickOverrun, signal.Code);
        Assert.Contains("1500 ms", signal.Sample);
    }

    // ---------- Registry, ranking, determinism ----------

    [Fact]
    public void Registry_aggregates_by_code_and_ranks_by_severity_times_count()
    {
        var registry = new PainRegistry();
        registry.Record([
            new PainSignal("A", PainCategory.Behavior, 2, 10, null, "a1"),
            new PainSignal("A", PainCategory.Behavior, 2, 20, null, "a2"),
            new PainSignal("A", PainCategory.Behavior, 2, 30, null, "a3"),
            new PainSignal("B", PainCategory.Behavior, 5, 15, null, "b1"),
        ]);

        var ranked = registry.Ranked();

        Assert.Equal(["A", "B"], ranked.Select(p => p.Code)); // 2×3 = 6 > 5×1 = 5
        Assert.Equal((3, 10L, 30L, "a3", PainStatus.Open), (ranked[0].Count, ranked[0].FirstTick, ranked[0].LastTick, ranked[0].Sample, ranked[0].Status));
    }

    [Fact]
    public void Pains_are_persisted_and_loaded()
    {
        using var db = new TempDatabase();
        long id = db.Store.CreateWorld(new WorldConfig(), DateTimeOffset.UnixEpoch);
        var registry = new PainRegistry(persist: changed => db.Store.SavePains(id, changed));

        registry.Record([new PainSignal(PainCodes.NoSocialContact, PainCategory.Behavior, 2, 48, new AgentId(1), "s1")]);
        registry.Record([new PainSignal(PainCodes.NoSocialContact, PainCategory.Behavior, 2, 60, new AgentId(2), "s2")]);

        var loaded = Assert.Single(db.Store.LoadPains(id));
        Assert.Equal(registry.Ranked()[0], loaded);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public async Task Same_seed_produces_the_same_pains()
    {
        async Task<string> Measure()
        {
            var runner = Runner(new WorldConfig { Seed = 7 }, _ => new RuleBasedBrain());
            var signals = await RunAsync(runner, 300);
            return string.Join("\n", signals.Select(s => $"{s.Tick}|{s.Code}|{s.Subject}|{s.Sample}"));
        }

        Assert.Equal(await Measure(), await Measure());
    }
}
