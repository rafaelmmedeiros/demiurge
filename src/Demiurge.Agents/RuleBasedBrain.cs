using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Agents;

/// <summary>
/// Utility AI brain, deliberately weak. Every tick it scores each goal from the perception
/// (integers 0–100), picks the highest, then turns the goal into an action: do it if already at
/// the goal's place, otherwise MoveTo there.
///
/// Weak on purpose (CLAUDE.md §6.3): purely reactive, no memory, ignores LastRejection and travel
/// time, never talks, never steals. The behavior pains it produces are what justify the LLM brain.
/// Concept: Rule-based agent and utility AI — docs/CONCEPTS.md#agente-baseado-em-regras-e-utility-ai
/// </summary>
public sealed class RuleBasedBrain : IAgentBrain
{
    /// <summary>Penalty on "get food" versus eating what you carry: getting it costs a trip.</summary>
    public const int GetFoodTravelPenalty = 10;

    /// <summary>Money level at which the urge to work reaches zero.</summary>
    public const int ComfortableMoney = 70;

    public Task<AgentAction> DecideAsync(Perception perception, CancellationToken ct) =>
        Task.FromResult(Decide(perception));

    public static AgentAction Decide(Perception p)
    {
        var goal = Score(p).MaxBy(s => s.Score)!.Goal; // MaxBy keeps the first on ties: table order breaks ties
        var here = p.Location is AtPlace at ? at.Place : (PlaceId?)null;

        return goal switch
        {
            Goal.Eat => new Eat(),
            Goal.GetFood => GoOrDo(here, PlacesGraph.Market, new Buy()),
            Goal.Work => GoOrDo(here, PlacesGraph.Workplace, new Work()),
            Goal.Rest => GoOrDo(here, PlacesGraph.Home, new Rest()),
            _ => throw new InvalidOperationException($"Unknown goal {goal}."),
        };
    }

    /// <summary>
    /// Score of every available goal, in fixed order (Eat, GetFood, Work, Rest). Public so a decision
    /// can always be explained: "it went to the market because GetFood scored 62 vs Rest 60".
    /// </summary>
    public static IReadOnlyList<GoalScore> Score(Perception p)
    {
        var scores = new List<GoalScore>(4);

        if (p.Food >= 1)
            scores.Add(new GoalScore(Goal.Eat, p.Hunger));
        if (p.Food == 0 && p.Money >= p.MarketFoodPrice)
            scores.Add(new GoalScore(Goal.GetFood, Math.Max(0, p.Hunger - GetFoodTravelPenalty)));
        if (p.Energy >= WorldRules.WorkEnergyCost)
            scores.Add(new GoalScore(Goal.Work, Math.Max(0, ComfortableMoney - p.Money)));
        scores.Add(new GoalScore(Goal.Rest, WorldRules.MaxNeed - p.Energy));

        return scores;
    }

    private static AgentAction GoOrDo(PlaceId? here, PlaceId target, AgentAction action) =>
        here == target ? action : new MoveTo(target);
}

public enum Goal
{
    Eat,
    GetFood,
    Work,
    Rest,
}

public sealed record GoalScore(Goal Goal, int Score);
