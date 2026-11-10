using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Randomness;

namespace Demiurge.Engine.Actions;

/// <summary>
/// Validation and resolution of proposed actions. Both are pure with respect to world state:
/// Validate checks the rules; Resolve computes the events the action produces (with resulting
/// values). The world changes only when World applies those events.
/// Every invariant lives in this file: money never goes negative, food is never created from
/// nothing, needs stay in 0–100, actions happen only where they are allowed.
/// Callers guarantee the agent is alive and at a place (not in transit).
/// Concepts: Authoritative engine — docs/CONCEPTS.md#engine-autoritativo ;
///           Places graph — docs/CONCEPTS.md#grafo-de-lugares ;
///           Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
internal static class ActionRules
{
    /// <returns>null if the action is legal for this agent now; otherwise the reason it is not.</returns>
    public static RejectionReason? Validate(Agent agent, AgentAction action, World world)
    {
        var here = ((AtPlace)agent.Location).Place;
        var kind = world.Config.Graph.Find(here)!.Kind;

        return action switch
        {
            Rest => null,

            Eat when agent.Food < 1 => RejectionReason.NoFood,
            Eat => null,

            Work when kind != PlaceKind.Workplace => RejectionReason.NotAtRequiredPlace,
            Work when agent.Energy < WorldRules.WorkEnergyCost => RejectionReason.TooTired,
            Work => null,

            Buy when kind != PlaceKind.Market => RejectionReason.NotAtRequiredPlace,
            Buy when agent.Money < world.Market.FoodPrice => RejectionReason.InsufficientMoney,
            Buy when world.Market.FoodStock < 1 => RejectionReason.OutOfStock,
            Buy => null,

            MoveTo m when world.Config.Graph.Find(m.Destination) is null => RejectionReason.UnknownPlace,
            MoveTo m when m.Destination == here => RejectionReason.AlreadyThere,
            MoveTo => null,

            Talk t when t.Target == agent.Id => RejectionReason.InvalidTarget,
            Talk t when string.IsNullOrWhiteSpace(t.Text) => RejectionReason.EmptyMessage,
            Talk t when t.Text.Length > WorldRules.MaxMessageLength => RejectionReason.MessageTooLong,
            Talk t when !world.IsPresentWith(agent, t.Target) => RejectionReason.TargetNotPresent,
            Talk => null,

            Steal s when s.Target == agent.Id => RejectionReason.InvalidTarget,
            Steal s when !world.IsPresentWith(agent, s.Target) => RejectionReason.TargetNotPresent,
            Steal s when world.FindAgent(s.Target)!.Food < 1 => RejectionReason.NothingToSteal,
            Steal => null,

            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action type."),
        };
    }

    /// <summary>Events produced by a valid action. Does not mutate state (only the seeded RNG advances).</summary>
    public static IReadOnlyList<WorldEvent> Resolve(long tick, Agent agent, AgentAction action, World world)
    {
        var here = ((AtPlace)agent.Location).Place;
        var market = world.Market;

        switch (action)
        {
            case Rest:
                int gain = world.Config.Graph.Find(here)!.Kind == PlaceKind.Home
                    ? WorldRules.RestEnergyGainAtHome
                    : WorldRules.RestEnergyGainElsewhere;
                return [new Rested(tick, agent.Id, Math.Min(WorldRules.MaxNeed, agent.Energy + gain))];

            case Eat:
                return [new Ate(tick, agent.Id, Math.Max(0, agent.Hunger - WorldRules.MealHungerRelief), agent.Food - 1)];

            case Work:
                return [new Worked(tick, agent.Id, WorldRules.WagePerWork,
                    agent.Money + WorldRules.WagePerWork, agent.Energy - WorldRules.WorkEnergyCost)];

            case Buy:
                return [new FoodBought(tick, agent.Id, market.FoodPrice,
                    agent.Money - market.FoodPrice, agent.Food + 1, market.FoodStock - 1, market.Money + market.FoodPrice)];

            case MoveTo m:
                return [new TravelStarted(tick, agent.Id, here, m.Destination, world.Config.Graph.TravelTicks(here, m.Destination))];

            case Talk t:
                return [new Talked(tick, agent.Id, t.Target, t.Text)];

            case Steal s:
                return ResolveSteal(tick, agent, world.FindAgent(s.Target)!, here, world);

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action type.");
        }
    }

    private static List<WorldEvent> ResolveSteal(long tick, Agent thief, Agent victim, PlaceId here, World world)
    {
        var events = new List<WorldEvent>
        {
            new Stole(tick, thief.Id, victim.Id, StolenItem.Food, thief.Food + 1, victim.Food - 1),
        };

        // Each agent present (victim included) rolls independently, in id order so the RNG
        // sequence is deterministic. The world records the theft either way; only witnesses learn who did it.
        foreach (var witness in world.PresentAt(here))
        {
            if (witness.Id == thief.Id)
                continue;
            if (world.Rng.Chance(world.Config.TheftWitnessChancePercent))
                events.Add(new TheftWitnessed(tick, witness.Id, thief.Id, victim.Id));
        }

        return events;
    }
}
