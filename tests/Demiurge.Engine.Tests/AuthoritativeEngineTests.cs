using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Propose → validate → apply or reject.
/// Concept: Authoritative engine — docs/CONCEPTS.md#engine-autoritativo
/// </summary>
public class AuthoritativeEngineTests
{
    private static readonly AgentId A1 = new(1);
    private static readonly AgentId A2 = new(2);

    private static World NewWorld(WorldConfig config) => World.Create(config).World;

    private static TickReport Do(World world, AgentId id, AgentAction action) =>
        world.Step([new AgentDecision(id, action)]);

    private static ActionRejected SingleRejection(TickReport report) =>
        Assert.Single(report.Events.OfType<ActionRejected>());

    // ---------- Eat ----------

    [Fact]
    public void Eat_without_food_is_rejected_and_changes_nothing_but_time()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialFood = (0, 1), InitialHunger = (50, 51) });

        var report = Do(world, A1, new Eat());

        Assert.Equal(RejectionReason.NoFood, SingleRejection(report).Reason);
        Assert.Equal(0, world.Agents[0].Food);
        Assert.Equal(50 + WorldRules.HungerPerTick, world.Agents[0].Hunger); // only the hour passed
    }

    [Fact]
    public void Eat_consumes_one_food_and_relieves_hunger()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialFood = (2, 3), InitialHunger = (50, 51) });

        var report = Do(world, A1, new Eat());

        Assert.Single(report.Events.OfType<Ate>());
        Assert.Equal(1, world.Agents[0].Food);
        Assert.Equal(50 - WorldRules.MealHungerRelief + WorldRules.HungerPerTick, world.Agents[0].Hunger);
    }

    // ---------- Buy ----------

    [Fact]
    public void Buy_without_money_is_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialMoney = (4, 5), FoodPrice = 5, StartPlace = PlacesGraph.Market });

        var report = Do(world, A1, new Buy());

        Assert.Equal(RejectionReason.InsufficientMoney, SingleRejection(report).Reason);
        Assert.Equal(4, world.Agents[0].Money);
    }

    [Fact]
    public void Buy_from_empty_market_is_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialMarketStock = 0, MarketRestockPerTick = 0, StartPlace = PlacesGraph.Market });

        var report = Do(world, A1, new Buy());

        Assert.Equal(RejectionReason.OutOfStock, SingleRejection(report).Reason);
    }

    [Fact]
    public void Buy_moves_money_to_market_and_food_to_agent()
    {
        var world = NewWorld(new WorldConfig
        {
            AgentCount = 1, InitialMoney = (30, 31), InitialFood = (0, 1),
            FoodPrice = 5, InitialMarketStock = 10, MarketRestockPerTick = 0, StartPlace = PlacesGraph.Market,
        });

        Do(world, A1, new Buy());

        Assert.Equal(25, world.Agents[0].Money);
        Assert.Equal(1, world.Agents[0].Food);
        Assert.Equal(9, world.Market.FoodStock);
        Assert.Equal(5, world.Market.Money);
    }

    [Fact]
    public void Two_agents_buying_the_last_food_one_gets_it_the_other_is_rejected()
    {
        var world = NewWorld(new WorldConfig
        {
            AgentCount = 2, InitialMoney = (50, 51), InitialMarketStock = 1, MarketRestockPerTick = 0,
            StartPlace = PlacesGraph.Market,
        });

        var report = world.Step([new AgentDecision(A1, new Buy()), new AgentDecision(A2, new Buy())]);

        Assert.Single(report.Events.OfType<FoodBought>());
        Assert.Equal(RejectionReason.OutOfStock, SingleRejection(report).Reason);
        Assert.Equal(0, world.Market.FoodStock);
    }

    // ---------- Work / Rest / Exhausted ----------

    [Fact]
    public void Work_pays_wage_and_costs_energy()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialMoney = (20, 21), InitialEnergy = (50, 51), StartPlace = PlacesGraph.Workplace });

        Do(world, A1, new Work());

        Assert.Equal(20 + WorldRules.WagePerWork, world.Agents[0].Money);
        Assert.Equal(50 - WorldRules.WorkEnergyCost - WorldRules.EnergyPerTick, world.Agents[0].Energy);
    }

    [Fact]
    public void Work_when_too_tired_is_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialEnergy = (5, 6), StartPlace = PlacesGraph.Workplace });

        var report = Do(world, A1, new Work());

        Assert.Equal(RejectionReason.TooTired, SingleRejection(report).Reason);
    }

    [Fact]
    public void Exhausted_agent_is_forced_to_rest()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialEnergy = (0, 1) });

        var report = Do(world, A1, new Work());

        Assert.Equal(RejectionReason.Exhausted, SingleRejection(report).Reason);
        Assert.Single(report.Events.OfType<Rested>());
        Assert.Equal(WorldRules.RestEnergyGainAtHome - WorldRules.EnergyPerTick, world.Agents[0].Energy);
    }

    // ---------- Malformed proposals never break the world ----------

    [Fact]
    public void Decision_for_unknown_agent_is_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1 });

        var report = Do(world, new AgentId(99), new Work());

        Assert.Equal(RejectionReason.UnknownAgent, SingleRejection(report).Reason);
    }

    [Fact]
    public void Decision_for_dead_agent_is_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1 });
        while (world.Agents[0].IsAlive) world.Step();

        var report = Do(world, A1, new Rest());

        Assert.Equal(RejectionReason.AgentNotAlive, SingleRejection(report).Reason);
    }

    [Fact]
    public void Duplicate_decisions_from_one_agent_are_all_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialMoney = (20, 21) });

        var report = world.Step([new AgentDecision(A1, new Work()), new AgentDecision(A1, new Work())]);

        Assert.Equal(2, report.Events.OfType<ActionRejected>().Count(r => r.Reason == RejectionReason.DuplicateDecision));
        Assert.Equal(20, world.Agents[0].Money);
    }

    // ---------- Feedback loop ----------

    [Fact]
    public void Rejection_reason_appears_in_the_next_perception_only()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialFood = (0, 1) });

        Do(world, A1, new Eat());
        Assert.Equal(RejectionReason.NoFood, world.Perceive(A1).LastRejection);

        Do(world, A1, new Rest());
        Assert.Null(world.Perceive(A1).LastRejection);
    }

    [Fact]
    public void Perception_is_a_copy_not_a_live_view()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1 });
        var before = world.Perceive(A1);

        world.Step();

        Assert.NotEqual(before.Hunger, world.Perceive(A1).Hunger);
        Assert.Equal(0, before.Tick);
    }

    // ---------- Ordering ----------

    [Fact]
    public void Order_of_submitted_decisions_does_not_change_the_outcome()
    {
        var config = new WorldConfig { AgentCount = 5, InitialMarketStock = 2, MarketRestockPerTick = 0, InitialMoney = (50, 51), StartPlace = PlacesGraph.Market };
        var buyAll = Enumerable.Range(1, 5).Select(i => new AgentDecision(new AgentId(i), new Buy())).ToList();

        var forward = NewWorld(config).Step(buyAll).Events.Select(e => e.ToString()).ToList();
        var reversed = NewWorld(config).Step(Enumerable.Reverse(buyAll).ToList()).Events.Select(e => e.ToString()).ToList();

        Assert.Equal(forward, reversed);
    }
}
