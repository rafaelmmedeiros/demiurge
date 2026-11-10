using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Harness.Pains;

/// <summary>
/// Everything a detector may look at after a tick: what happened (events), what was proposed
/// (decisions), what failed (brain failures), how long it took, and the world state after it.
/// Only what the environment can observe — never a brain's internal reasoning — so detectors
/// work the same for rule-based and LLM brains.
/// Concept: Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor
/// </summary>
public sealed record TickContext(
    TickReport Report,
    IReadOnlyList<AgentDecision> Decisions,
    IReadOnlyList<BrainFailure> Failures,
    TimeSpan Duration,
    TimeSpan TickInterval,
    WorldSnapshot After,
    WorldConfig Config)
{
    public long Tick => Report.Tick;
}

/// <summary>Runs at the end of every tick and reports measured signals. May keep its own history.</summary>
public interface IPainDetector
{
    IEnumerable<PainSignal> Inspect(TickContext ctx);
}

public enum PainCategory
{
    Technical,
    Behavior,
    Knowledge,
}

public enum PainStatus
{
    Open,
    Mitigated,
    Resolved,
}

/// <summary>One occurrence (episode) of a pain, as emitted by a detector.</summary>
public sealed record PainSignal(
    string Code,
    PainCategory Category,
    int Severity,
    long Tick,
    AgentId? Subject,
    string Sample,
    string? Mitigation = null);

/// <summary>Aggregate of every signal with the same code in one world. Ranked by Severity × Count.</summary>
public sealed record Pain(
    string Code,
    PainCategory Category,
    int Severity,
    int Count,
    long FirstTick,
    long LastTick,
    string Sample,
    string? Mitigation,
    PainStatus Status)
{
    public int Rank => Severity * Count;
}

public static class PainCodes
{
    public const string TickException = "TICK_EXCEPTION";
    public const string TickOverrun = "TICK_OVERRUN";
    public const string StarvationParadox = "STARVATION_PARADOX";
    public const string RepetitionLoop = "REPETITION_LOOP";
    public const string ActionRejectionRate = "ACTION_REJECTION_RATE";
    public const string NoSocialContact = "NO_SOCIAL_CONTACT";
    public const string ResourceCollapse = "RESOURCE_COLLAPSE";
    public const string GoalOscillation = "GOAL_OSCILLATION";
}

/// <summary>
/// Edge trigger: reports true only when a condition goes from false to true for a key, and re-arms
/// when it goes back to false. Turns "agent has been silent for 48+ ticks" (true for hundreds of
/// ticks) into one episode instead of one signal per tick.
/// </summary>
internal sealed class EdgeTrigger<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> _active = []; // membership only, never iterated

    public bool Rising(TKey key, bool condition)
    {
        if (!condition)
        {
            _active.Remove(key);
            return false;
        }

        return _active.Add(key);
    }
}
