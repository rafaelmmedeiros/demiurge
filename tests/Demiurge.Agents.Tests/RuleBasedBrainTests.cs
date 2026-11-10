using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Agents.Tests;

/// <summary>
/// Concept: Rule-based agent and utility AI — docs/CONCEPTS.md#agente-baseado-em-regras-e-utility-ai
/// </summary>
public class RuleBasedBrainTests
{
    private static Perception P(int hunger, int energy, int food, int money, PlaceId place) =>
        new(Tick: 0, Self: new AgentId(1), Hunger: hunger, Energy: energy, Food: food, Money: money,
            Location: new AtPlace(place), PeoplePresent: [], MessagesHeard: [], TheftsWitnessed: [],
            MarketFoodStock: 20, MarketFoodPrice: 5, Map: PlacesGraph.Default, LastRejection: null);

    [Fact]
    public void Worked_example_from_the_explanation()
    {
        var p = P(hunger: 72, energy: 40, food: 0, money: 12, PlacesGraph.Workplace);

        Assert.Equal(
            [new GoalScore(Goal.GetFood, 62), new GoalScore(Goal.Work, 58), new GoalScore(Goal.Rest, 60)],
            RuleBasedBrain.Score(p));
        Assert.Equal(new MoveTo(PlacesGraph.Market), RuleBasedBrain.Decide(p));
    }

    [Fact]
    public void Hungry_agent_with_food_eats_wherever_it_is()
    {
        Assert.Equal(new Eat(), RuleBasedBrain.Decide(P(80, 60, 1, 10, PlacesGraph.Square)));
    }

    [Fact]
    public void Agent_at_the_market_wanting_food_buys()
    {
        Assert.Equal(new Buy(), RuleBasedBrain.Decide(P(80, 60, 0, 10, PlacesGraph.Market)));
    }

    [Fact]
    public void Tired_agent_rests_at_home_or_goes_home()
    {
        Assert.Equal(new Rest(), RuleBasedBrain.Decide(P(10, 15, 0, 100, PlacesGraph.Home)));
        Assert.Equal(new MoveTo(PlacesGraph.Home), RuleBasedBrain.Decide(P(10, 15, 0, 100, PlacesGraph.Workplace)));
    }

    [Fact]
    public void Poor_rested_agent_works()
    {
        Assert.Equal(new Work(), RuleBasedBrain.Decide(P(10, 90, 0, 0, PlacesGraph.Workplace)));
    }

    [Fact]
    public void Unavailable_goals_are_not_scored()
    {
        // No food and no money: neither Eat nor GetFood; energy below work cost: no Work.
        var scores = RuleBasedBrain.Score(P(90, 5, 0, 2, PlacesGraph.Home));

        Assert.Equal([new GoalScore(Goal.Rest, 95)], scores);
    }

    [Fact]
    public void Ties_are_broken_by_fixed_goal_order()
    {
        // Eat = 50, Rest = 50: Eat comes first in the table.
        Assert.Equal(new Eat(), RuleBasedBrain.Decide(P(50, 50, 1, 70, PlacesGraph.Home)));
    }

    [Fact]
    public void Never_talks_or_steals_in_a_long_run()
    {
        var (world, _) = World.Create(new WorldConfig { Seed = 42 });
        var events = new List<WorldEvent>();

        for (int i = 0; i < 500; i++)
        {
            var decisions = world.Agents
                .Where(a => a.IsAlive && a.Location is AtPlace)
                .Select(a => new AgentDecision(a.Id, RuleBasedBrain.Decide(world.Perceive(a.Id))))
                .ToList();
            events.AddRange(world.Step(decisions).Events);
        }

        Assert.DoesNotContain(events, e => e is Talked or Stole);
    }
}
