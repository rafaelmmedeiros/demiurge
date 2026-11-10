namespace Demiurge.Harness.Pains;

/// <summary>
/// Aggregates pain signals of one world by code: count of episodes, first/last tick, highest
/// severity, latest sample and mitigation. Technical pains with an applied mitigation are
/// Mitigated; behavior pains stay Open until a development session resolves them.
/// Optionally persists every changed aggregate (the store's pains table).
/// Concept: Pain detectors — docs/CONCEPTS.md#harness-observabilidade-e-detectores-de-dor
/// </summary>
public sealed class PainRegistry
{
    private readonly Dictionary<string, Pain> _byCode = new(StringComparer.Ordinal); // lookups; output is sorted
    private readonly Action<IReadOnlyList<Pain>>? _persist;

    public PainRegistry(IEnumerable<Pain>? existing = null, Action<IReadOnlyList<Pain>>? persist = null)
    {
        foreach (var pain in existing ?? [])
            _byCode[pain.Code] = pain;
        _persist = persist;
    }

    /// <summary>Folds the signals of one tick into the aggregates; returns the aggregates that changed.</summary>
    public IReadOnlyList<Pain> Record(IReadOnlyList<PainSignal> signals)
    {
        var changed = new Dictionary<string, Pain>(StringComparer.Ordinal);
        foreach (var s in signals)
        {
            var pain = _byCode.TryGetValue(s.Code, out var p)
                ? p with
                {
                    Severity = Math.Max(p.Severity, s.Severity),
                    Count = p.Count + 1,
                    LastTick = s.Tick,
                    Sample = s.Sample,
                    Mitigation = s.Mitigation ?? p.Mitigation,
                    Status = StatusFor(s, p.Status),
                }
                : new Pain(s.Code, s.Category, s.Severity, 1, s.Tick, s.Tick, s.Sample, s.Mitigation, StatusFor(s, PainStatus.Open));
            _byCode[s.Code] = pain;
            changed[s.Code] = pain;
        }

        var result = changed.Values.OrderBy(p => p.Code, StringComparer.Ordinal).ToList();
        if (result.Count > 0)
            _persist?.Invoke(result);
        return result;
    }

    /// <summary>All pains, highest Severity × Count first; ties by code.</summary>
    public IReadOnlyList<Pain> Ranked() =>
        _byCode.Values
            .OrderByDescending(p => p.Rank)
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .ToList();

    private static PainStatus StatusFor(PainSignal s, PainStatus current) =>
        current == PainStatus.Resolved ? PainStatus.Open        // it came back: reopen
        : s.Mitigation is not null ? PainStatus.Mitigated
        : current == PainStatus.Mitigated ? PainStatus.Mitigated
        : PainStatus.Open;
}
