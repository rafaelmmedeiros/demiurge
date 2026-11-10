using Demiurge.Engine.Domain;

namespace Demiurge.Engine.Events;

/// <summary>
/// A fact that happened in the world at a given tick. Every state change produces one
/// (CLAUDE.md rule 3). Persistence and rebuilding state from events come with the
/// event sourcing concept; for now events are the observable output of a tick.
/// </summary>
public abstract record WorldEvent(long Tick);

public sealed record AgentSpawned(long Tick, AgentId AgentId, int Hunger, int Energy, int Food, int Money)
    : WorldEvent(Tick);

public sealed record TickStarted(long Tick) : WorldEvent(Tick);

public sealed record NeedsUpdated(long Tick, AgentId AgentId, int Hunger, int Energy) : WorldEvent(Tick);

public sealed record AgentDied(long Tick, AgentId AgentId, DeathCause Cause) : WorldEvent(Tick);

public enum DeathCause
{
    Starvation,
}

/// <summary>Everything a tick produced. Will grow with decisions and timings for the pain detectors.</summary>
public sealed record TickReport(long Tick, IReadOnlyList<WorldEvent> Events);
