namespace Demiurge.Engine.Randomness;

/// <summary>
/// Seeded pseudo-random source: the only source of randomness allowed in the engine.
/// Its whole state is exposed so a snapshot can resume the exact same sequence.
/// Concept: Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed
/// </summary>
public interface IRandom
{
    /// <summary>Next raw 64-bit value; advances the state.</summary>
    ulong NextULong();

    /// <summary>Complete internal state. <c>new SplitMix64(state)</c> continues the same sequence.</summary>
    ulong State { get; }
}

public static class RandomExtensions
{
    /// <summary>
    /// Uniform integer in [minInclusive, maxExclusive), without modulo bias:
    /// raw values below 2^64 mod n are rejected, so every result has exactly the same probability.
    /// </summary>
    public static int NextInt(this IRandom rng, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong threshold = (0UL - range) % range; // == 2^64 mod range
        while (true)
        {
            ulong r = rng.NextULong();
            if (r >= threshold)
                return (int)((long)minInclusive + (long)(r % range));
        }
    }

    /// <summary>True with probability percent/100. Integer percent avoids floating point in the engine.</summary>
    public static bool Chance(this IRandom rng, int percent) => rng.NextInt(0, 100) < percent;

    /// <summary>In-place Fisher–Yates shuffle: every permutation equally likely, driven only by the seed.</summary>
    public static void Shuffle<T>(this IRandom rng, IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
