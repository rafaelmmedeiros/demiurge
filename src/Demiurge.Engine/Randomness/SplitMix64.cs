namespace Demiurge.Engine.Randomness;

/// <summary>
/// SplitMix64 PRNG (Steele, Lea, Flood, 2014): 8 bytes of state, passes BigCrush.
/// Implemented here instead of using System.Random because (1) .NET does not guarantee
/// System.Random's algorithm across versions and (2) it does not expose its state for snapshots.
/// Concept: Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed
/// </summary>
public sealed class SplitMix64 : IRandom
{
    private ulong _state;

    public SplitMix64(ulong seedOrState) => _state = seedOrState;

    public ulong State => _state;

    public ulong NextULong()
    {
        ulong z = _state += 0x9E3779B97F4A7C15;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }
}
