using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Engine.Actions;

/// <summary>
/// Validation and application of proposed actions. Validate is a pure check (no mutation);
/// Apply runs only after Validate returned null, so every invariant lives in this file:
/// money never goes negative, food is never created from nothing, needs stay in 0–100.
/// Concept: Authoritative engine — docs/CONCEPTS.md#engine-autoritativo
/// </summary>
internal static class ActionRules
{
    /// <returns>null if the action is legal for this agent now; otherwise the reason it is not.</returns>
    public static RejectionReason? Validate(Agent agent, AgentAction action, Market market) => action switch
    {
        Rest => null,
        Eat when agent.Food < 1 => RejectionReason.NoFood,
        Eat => null,
        Work when agent.Energy < WorldRules.WorkEnergyCost => RejectionReason.TooTired,
        Work => null,
        Buy when agent.Money < market.FoodPrice => RejectionReason.InsufficientMoney,
        Buy when market.FoodStock < 1 => RejectionReason.OutOfStock,
        Buy => null,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action type."),
    };

    public static WorldEvent Apply(long tick, Agent agent, AgentAction action, Market market)
    {
        switch (action)
        {
            case Rest:
                agent.Energy = Math.Min(WorldRules.MaxNeed, agent.Energy + WorldRules.RestEnergyGain);
                return new Rested(tick, agent.Id, agent.Energy);

            case Eat:
                agent.Food -= 1;
                agent.Hunger = Math.Max(0, agent.Hunger - WorldRules.MealHungerRelief);
                return new Ate(tick, agent.Id, agent.Hunger, agent.Food);

            case Work:
                agent.Energy -= WorldRules.WorkEnergyCost;
                agent.Money += WorldRules.WagePerWork;
                return new Worked(tick, agent.Id, WorldRules.WagePerWork, agent.Money, agent.Energy);

            case Buy:
                agent.Money -= market.FoodPrice;
                agent.Food += 1;
                market.FoodStock -= 1;
                market.Money += market.FoodPrice;
                return new FoodBought(tick, agent.Id, market.FoodPrice, agent.Money, agent.Food, market.FoodStock);

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action type.");
        }
    }
}
