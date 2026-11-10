using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine;

/// <summary>
/// Complete world state at the end of a tick, RNG included. A cache over the event log:
/// restoring = load the latest snapshot + apply the events after it, instead of replaying from genesis.
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public sealed record WorldSnapshot(
    long Tick,
    ulong RngState,
    IReadOnlyList<AgentSnapshot> Agents,
    MarketSnapshot Market);

public sealed record AgentSnapshot(
    AgentId Id,
    int Hunger,
    int Energy,
    int Food,
    int Money,
    bool IsAlive,
    int TicksStarving,
    Location Location,
    RejectionReason? LastRejection,
    IReadOnlyList<HeardMessage> MessagesHeard,
    IReadOnlyList<WitnessedTheft> TheftsWitnessed);

public sealed record MarketSnapshot(int FoodStock, int FoodPrice, int Money);
