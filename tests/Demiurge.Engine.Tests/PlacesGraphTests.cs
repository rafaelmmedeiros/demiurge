using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Space as a graph of places with travel time; location rules; talk; theft and witnesses.
/// Concept: Places graph — docs/CONCEPTS.md#grafo-de-lugares
/// </summary>
public class PlacesGraphTests
{
    private static readonly AgentId A1 = new(1);
    private static readonly AgentId A2 = new(2);
    private static readonly AgentId A3 = new(3);

    private static World NewWorld(WorldConfig config) => World.Create(config).World;

    private static TickReport Do(World world, AgentId id, AgentAction action) =>
        world.Step([new AgentDecision(id, action)]);

    private static RejectionReason Rejection(TickReport report) =>
        Assert.Single(report.Events.OfType<ActionRejected>()).Reason;

    // ---------- Graph ----------

    [Fact]
    public void Default_graph_has_expected_travel_times()
    {
        var g = PlacesGraph.Default;

        Assert.Equal(2, g.TravelTicks(PlacesGraph.Home, PlacesGraph.Workplace));
        Assert.Equal(2, g.TravelTicks(PlacesGraph.Workplace, PlacesGraph.Home)); // undirected
        Assert.Equal(1, g.TravelTicks(PlacesGraph.Home, PlacesGraph.Market));
        Assert.Equal(4, g.Places.Count);
    }

    [Fact]
    public void Incomplete_graph_is_refused()
    {
        var a = new PlaceId("a");
        var b = new PlaceId("b");
        var c = new PlaceId("c");

        Assert.Throws<ArgumentException>(() => new PlacesGraph(
            [new Place(a, PlaceKind.Home, 0, 0), new Place(b, PlaceKind.Market, 0, 0), new Place(c, PlaceKind.Square, 0, 0)],
            [new Edge(a, b, 1), new Edge(b, c, 1)])); // a–c missing
    }

    // ---------- Travel ----------

    [Fact]
    public void One_tick_trip_arrives_at_the_end_of_the_same_tick()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1 });

        var report = Do(world, A1, new MoveTo(PlacesGraph.Market));

        Assert.Single(report.Events.OfType<TravelStarted>());
        Assert.Single(report.Events.OfType<Arrived>());
        Assert.Equal(new AtPlace(PlacesGraph.Market), world.Agents[0].Location);
    }

    [Fact]
    public void Two_tick_trip_spends_one_full_tick_on_the_road()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1 });

        var first = Do(world, A1, new MoveTo(PlacesGraph.Workplace));
        Assert.Equal(new InTransit(PlacesGraph.Home, PlacesGraph.Workplace, 1), world.Agents[0].Location);
        Assert.Single(first.Events.OfType<TravelProgressed>());

        var second = world.Step();
        Assert.Single(second.Events.OfType<Arrived>());
        Assert.Equal(new AtPlace(PlacesGraph.Workplace), world.Agents[0].Location);
    }

    [Fact]
    public void Agent_in_transit_cannot_decide_but_still_gets_hungry()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialHunger = (10, 11) });
        Do(world, A1, new MoveTo(PlacesGraph.Workplace));

        var report = Do(world, A1, new Rest());

        Assert.Equal(RejectionReason.InTransit, Rejection(report));
        Assert.Equal(10 + 2 * WorldRules.HungerPerTick, world.Agents[0].Hunger);
    }

    [Fact]
    public void Moving_to_current_or_unknown_place_is_rejected()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1 });

        Assert.Equal(RejectionReason.AlreadyThere, Rejection(Do(world, A1, new MoveTo(PlacesGraph.Home))));
        Assert.Equal(RejectionReason.UnknownPlace, Rejection(Do(world, A1, new MoveTo(new PlaceId("moon")))));
    }

    // ---------- Location requirements ----------

    [Fact]
    public void Work_and_buy_require_their_place()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 1, InitialMoney = (50, 51) }); // at home

        Assert.Equal(RejectionReason.NotAtRequiredPlace, Rejection(Do(world, A1, new Work())));
        Assert.Equal(RejectionReason.NotAtRequiredPlace, Rejection(Do(world, A1, new Buy())));
    }

    [Fact]
    public void Rest_recovers_more_at_home()
    {
        var home = NewWorld(new WorldConfig { AgentCount = 1, InitialEnergy = (50, 51) });
        var square = NewWorld(new WorldConfig { AgentCount = 1, InitialEnergy = (50, 51), StartPlace = PlacesGraph.Square });

        Do(home, A1, new Rest());
        Do(square, A1, new Rest());

        Assert.Equal(50 + WorldRules.RestEnergyGainAtHome - WorldRules.EnergyPerTick, home.Agents[0].Energy);
        Assert.Equal(50 + WorldRules.RestEnergyGainElsewhere - WorldRules.EnergyPerTick, square.Agents[0].Energy);
    }

    // ---------- Perception of space ----------

    [Fact]
    public void Perception_lists_only_people_at_the_same_place()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 3 });
        Do(world, A3, new MoveTo(PlacesGraph.Market));

        var p1 = world.Perceive(A1);

        Assert.Equal([A2], p1.PeoplePresent);
        Assert.Equal(new AtPlace(PlacesGraph.Home), p1.Location);
    }

    [Fact]
    public void Agent_in_transit_sees_nobody()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 2 });
        Do(world, A1, new MoveTo(PlacesGraph.Workplace));

        Assert.Empty(world.Perceive(A1).PeoplePresent);
        Assert.Empty(world.Perceive(A2).PeoplePresent);
    }

    // ---------- Talk ----------

    [Fact]
    public void Talk_reaches_the_listener_next_tick()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 2 });

        var report = Do(world, A1, new Talk(A2, "Tem comida sobrando?"));

        Assert.Single(report.Events.OfType<Talked>());
        Assert.Equal([new HeardMessage(A1, "Tem comida sobrando?")], world.Perceive(A2).MessagesHeard);
        Assert.Empty(world.Perceive(A1).MessagesHeard);
    }

    [Fact]
    public void Talk_validation()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 3 });
        Do(world, A3, new MoveTo(PlacesGraph.Market));

        Assert.Equal(RejectionReason.InvalidTarget, Rejection(Do(world, A1, new Talk(A1, "oi"))));
        Assert.Equal(RejectionReason.EmptyMessage, Rejection(Do(world, A1, new Talk(A2, "  "))));
        Assert.Equal(RejectionReason.MessageTooLong, Rejection(Do(world, A1, new Talk(A2, new string('x', 201)))));
        Assert.Equal(RejectionReason.TargetNotPresent, Rejection(Do(world, A1, new Talk(A3, "oi"))));
    }

    // ---------- Steal and witnesses ----------

    [Fact]
    public void Steal_moves_one_food_from_victim_to_thief()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 2, InitialFood = (2, 3) });

        var report = Do(world, A1, new Steal(A2));

        Assert.Single(report.Events.OfType<Stole>());
        Assert.Equal(3, world.Agents[0].Food);
        Assert.Equal(1, world.Agents[1].Food);
    }

    [Fact]
    public void With_certain_witnessing_everyone_present_sees_the_thief()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 3, InitialFood = (2, 3), TheftWitnessChancePercent = 100 });

        var report = Do(world, A1, new Steal(A2));

        var witnesses = report.Events.OfType<TheftWitnessed>().Select(w => w.Witness).ToList();
        Assert.Equal([A2, A3], witnesses); // victim included, thief excluded, id order
        Assert.Equal([new WitnessedTheft(A1, A2)], world.Perceive(A3).TheftsWitnessed);
    }

    [Fact]
    public void Unseen_theft_is_in_the_event_log_but_in_nobody_s_perception()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 3, InitialFood = (2, 3), TheftWitnessChancePercent = 0 });

        var report = Do(world, A1, new Steal(A2));

        Assert.Single(report.Events.OfType<Stole>());            // god's view: the fact happened
        Assert.Empty(report.Events.OfType<TheftWitnessed>());
        Assert.Empty(world.Perceive(A2).TheftsWitnessed);        // victim's view: food is gone, thief unknown
        Assert.Equal(1, world.Perceive(A2).Food);
    }

    [Fact]
    public void Steal_validation()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 3, InitialFood = (0, 1) });
        Do(world, A3, new MoveTo(PlacesGraph.Market));

        Assert.Equal(RejectionReason.InvalidTarget, Rejection(Do(world, A1, new Steal(A1))));
        Assert.Equal(RejectionReason.NothingToSteal, Rejection(Do(world, A1, new Steal(A2))));
        Assert.Equal(RejectionReason.TargetNotPresent, Rejection(Do(world, A1, new Steal(A3))));
    }

    [Fact]
    public void Agent_in_transit_cannot_be_robbed()
    {
        var world = NewWorld(new WorldConfig { AgentCount = 2, InitialFood = (2, 3) });
        Do(world, A2, new MoveTo(PlacesGraph.Workplace));

        Assert.Equal(RejectionReason.TargetNotPresent, Rejection(Do(world, A1, new Steal(A2))));
    }
}
