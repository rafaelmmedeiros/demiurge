using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine;

/// <summary>
/// Immutable copy of what one agent can see at the end of a tick. Brains receive this, never the
/// mutable <see cref="Agent"/>: without a reference to state, a brain cannot change it.
/// <see cref="LastRejection"/> closes the loop: a refused action in tick t is visible in t+1.
/// Concept: Authoritative engine — docs/CONCEPTS.md#engine-autoritativo
/// </summary>
public sealed record Perception(
    long Tick,
    AgentId Self,
    int Hunger,
    int Energy,
    int Food,
    int Money,
    int MarketFoodStock,
    int MarketFoodPrice,
    RejectionReason? LastRejection);
