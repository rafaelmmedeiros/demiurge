namespace Demiurge.Host;

/// <summary>
/// Configuration section "DEMIURGE". Every value can be overridden by an environment variable
/// with the same name and a double underscore: DEMIURGE__Seed, DEMIURGE__TickIntervalMs, ...
/// Defaults work with no configuration at all (CLAUDE.md rule 5).
/// </summary>
public sealed class DemiurgeOptions
{
    public const string Section = "DEMIURGE";

    public ulong Seed { get; set; } = 42;

    public int AgentCount { get; set; } = 5;

    /// <summary>Real time between ticks. 1 tick = 1 simulated hour.</summary>
    public int TickIntervalMs { get; set; } = 1000;

    /// <summary>"Rules" today; "Llm" arrives in stage 2.</summary>
    public string Brain { get; set; } = "Rules";

    public string DatabasePath { get; set; } = "data/demiurge.db";

    /// <summary>Start with the clock paused (tests, debugging).</summary>
    public bool StartPaused { get; set; }
}
