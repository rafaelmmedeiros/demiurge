namespace Demiurge.Harness.Pains;

/// <summary>
/// TICK_EXCEPTION: a brain threw. Runtime mitigation already applied by TickRunner: the agent idles
/// for this tick and the tick continues. One signal per failure.
/// Concept: Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor
/// </summary>
public sealed class TickExceptionDetector : IPainDetector
{
    public IEnumerable<PainSignal> Inspect(TickContext ctx) =>
        ctx.Failures
            .Where(f => f.Kind is BrainFailureKind.Exception or BrainFailureKind.NullAction)
            .Select(f => new PainSignal(
                PainCodes.TickException, PainCategory.Technical, 4, ctx.Tick, f.AgentId,
                $"{f.AgentId} at tick {ctx.Tick}: {f.Detail}",
                "Agent idled for this tick; failure isolated, tick continued."));
}

/// <summary>
/// TICK_OVERRUN: the tick took longer than the tick interval, or a brain missed the decision
/// deadline. Mitigation already applied: the tick did not wait for late decisions.
/// </summary>
public sealed class TickOverrunDetector : IPainDetector
{
    public IEnumerable<PainSignal> Inspect(TickContext ctx)
    {
        foreach (var f in ctx.Failures.Where(f => f.Kind == BrainFailureKind.Timeout))
            yield return new PainSignal(
                PainCodes.TickOverrun, PainCategory.Technical, 3, ctx.Tick, f.AgentId,
                $"{f.AgentId} at tick {ctx.Tick}: {f.Detail}",
                "Tick did not wait; agent idled for this tick.");

        if (ctx.Duration > ctx.TickInterval)
            yield return new PainSignal(
                PainCodes.TickOverrun, PainCategory.Technical, 3, ctx.Tick, null,
                $"Tick {ctx.Tick} took {ctx.Duration.TotalMilliseconds:F0} ms (interval {ctx.TickInterval.TotalMilliseconds:F0} ms).",
                "Next tick starts immediately; no waiting for late decisions.");
    }
}
