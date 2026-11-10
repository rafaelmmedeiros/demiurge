using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine;

/// <summary>
/// Immutable copy of what one agent can see at the end of a tick. Brains receive this, never the
/// mutable <see cref="Agent"/>: without a reference to state, a brain cannot change it.
/// It holds only what the agent could know (its place, who is there, what it heard and saw),
/// not the full world: the gap between this and the event log is asymmetric information.
/// Concepts: Authoritative engine — docs/CONCEPTS.md#engine-autoritativo ;
///           Places graph — docs/CONCEPTS.md#grafo-de-lugares
/// </summary>
public sealed record Perception(
    long Tick,
    AgentId Self,
    int Hunger,
    int Energy,
    int Food,
    int Money,
    Location Location,
    IReadOnlyList<AgentId> PeoplePresent,
    IReadOnlyList<HeardMessage> MessagesHeard,
    IReadOnlyList<WitnessedTheft> TheftsWitnessed,
    int MarketFoodStock,
    int MarketFoodPrice,
    PlacesGraph Map,
    RejectionReason? LastRejection);
