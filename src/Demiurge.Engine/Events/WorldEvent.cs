using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;

namespace Demiurge.Engine.Events;

/// <summary>
/// A fact that happened in the world at a given tick. Every state change produces one
/// (CLAUDE.md rule 3). Events carry the resulting values so a reader never has to recompute them.
/// Persistence and rebuilding state from events come with the event sourcing concept.
/// </summary>
public abstract record WorldEvent(long Tick);

// Genesis
public sealed record AgentSpawned(long Tick, AgentId AgentId, int Hunger, int Energy, int Food, int Money)
    : WorldEvent(Tick);

public sealed record MarketOpened(long Tick, int FoodStock, int FoodPrice) : WorldEvent(Tick);

// Time
public sealed record TickStarted(long Tick) : WorldEvent(Tick);

public sealed record NeedsUpdated(long Tick, AgentId AgentId, int Hunger, int Energy) : WorldEvent(Tick);

public sealed record AgentDied(long Tick, AgentId AgentId, DeathCause Cause) : WorldEvent(Tick);

public sealed record MarketRestocked(long Tick, int FoodStock) : WorldEvent(Tick);

// Actions accepted by the engine
public sealed record Rested(long Tick, AgentId AgentId, int Energy) : WorldEvent(Tick);

public sealed record Ate(long Tick, AgentId AgentId, int Hunger, int FoodLeft) : WorldEvent(Tick);

public sealed record Worked(long Tick, AgentId AgentId, int Earned, int Money, int Energy) : WorldEvent(Tick);

public sealed record FoodBought(long Tick, AgentId AgentId, int Price, int Money, int Food, int MarketFoodStock)
    : WorldEvent(Tick);

// Actions refused by the engine: no state changed, the reason goes into the next perception.
public sealed record ActionRejected(long Tick, AgentId AgentId, AgentAction Action, RejectionReason Reason)
    : WorldEvent(Tick);

public enum DeathCause
{
    Starvation,
}

/// <summary>Everything a tick produced. Will grow with decisions and timings for the pain detectors.</summary>
public sealed record TickReport(long Tick, IReadOnlyList<WorldEvent> Events);
