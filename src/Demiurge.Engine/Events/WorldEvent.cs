using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine.Events;

/// <summary>
/// A fact that happened in the world at a given tick. The ONLY way state changes: the engine
/// emits an event and applies it (World.Apply). Events carry the resulting values, so applying
/// one is an assignment, never a re-run of a rule — the past stays fixed even if rules change.
/// The event log is god's view: it records every fact, including the ones no agent saw.
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public abstract record WorldEvent(long Tick);

// Genesis
public sealed record AgentSpawned(long Tick, AgentId AgentId, int Hunger, int Energy, int Food, int Money, PlaceId Place)
    : WorldEvent(Tick);

public sealed record MarketOpened(long Tick, int FoodStock, int FoodPrice) : WorldEvent(Tick);

// Time
public sealed record TickStarted(long Tick) : WorldEvent(Tick);

/// <summary>Closes a tick and records the RNG state, so a rebuilt world continues the same random sequence.</summary>
public sealed record TickEnded(long Tick, ulong RngState) : WorldEvent(Tick);

public sealed record NeedsUpdated(long Tick, AgentId AgentId, int Hunger, int Energy, int TicksStarving) : WorldEvent(Tick);

public sealed record AgentDied(long Tick, AgentId AgentId, DeathCause Cause) : WorldEvent(Tick);

public sealed record MarketRestocked(long Tick, int FoodStock) : WorldEvent(Tick);

public sealed record TravelProgressed(long Tick, AgentId AgentId, PlaceId From, PlaceId To, int TicksRemaining)
    : WorldEvent(Tick);

public sealed record Arrived(long Tick, AgentId AgentId, PlaceId Place) : WorldEvent(Tick);

// Actions accepted by the engine
public sealed record Rested(long Tick, AgentId AgentId, int Energy) : WorldEvent(Tick);

public sealed record Ate(long Tick, AgentId AgentId, int Hunger, int FoodLeft) : WorldEvent(Tick);

public sealed record Worked(long Tick, AgentId AgentId, int Earned, int Money, int Energy) : WorldEvent(Tick);

public sealed record FoodBought(
    long Tick, AgentId AgentId, int Price, int Money, int Food, int MarketFoodStock, int MarketMoney)
    : WorldEvent(Tick);

public sealed record TravelStarted(long Tick, AgentId AgentId, PlaceId From, PlaceId To, int Ticks) : WorldEvent(Tick);

public sealed record Talked(long Tick, AgentId Speaker, AgentId Listener, string Text) : WorldEvent(Tick);

public sealed record Stole(long Tick, AgentId Thief, AgentId Victim, StolenItem Item, int ThiefFood, int VictimFood)
    : WorldEvent(Tick);

public sealed record TheftWitnessed(long Tick, AgentId Witness, AgentId Thief, AgentId Victim) : WorldEvent(Tick);

// Actions refused by the engine: no state changed, the reason goes into the next perception.
public sealed record ActionRejected(long Tick, AgentId AgentId, AgentAction Action, RejectionReason Reason)
    : WorldEvent(Tick);

public enum DeathCause
{
    Starvation,
}

public enum StolenItem
{
    Food,
}

/// <summary>Everything a tick produced. Will grow with decisions and timings for the pain detectors.</summary>
public sealed record TickReport(long Tick, IReadOnlyList<WorldEvent> Events);
