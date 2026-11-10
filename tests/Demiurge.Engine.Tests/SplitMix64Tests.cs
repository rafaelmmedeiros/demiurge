using Demiurge.Engine.Randomness;

namespace Demiurge.Engine.Tests;

public class SplitMix64Tests
{
    [Fact]
    public void Seed_zero_matches_reference_vector()
    {
        // Reference outputs of SplitMix64 for seed 0. If this fails, every world ever created changes.
        var rng = new SplitMix64(0);

        Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextULong());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.NextULong());
        Assert.Equal(0x06C45D188009454FUL, rng.NextULong());
    }

    [Fact]
    public void Restoring_from_state_continues_the_same_sequence()
    {
        var original = new SplitMix64(42);
        for (int i = 0; i < 100; i++) original.NextULong();

        var restored = new SplitMix64(original.State);

        for (int i = 0; i < 100; i++)
            Assert.Equal(original.NextULong(), restored.NextULong());
    }

    [Fact]
    public void NextInt_stays_in_range_and_covers_every_value()
    {
        var rng = new SplitMix64(42);
        var seen = new int[10];

        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(5, 15);
            Assert.InRange(value, 5, 14);
            seen[value - 5]++;
        }

        // 10 000 draws over 10 values: expected 1 000 each; a bound of ±20% catches gross bias.
        Assert.All(seen, count => Assert.InRange(count, 800, 1200));
    }

    [Fact]
    public void Shuffle_is_reproducible_for_the_same_seed()
    {
        var a = Enumerable.Range(1, 20).ToList();
        var b = Enumerable.Range(1, 20).ToList();

        new SplitMix64(7).Shuffle(a);
        new SplitMix64(7).Shuffle(b);

        Assert.Equal(a, b);
        Assert.NotEqual(Enumerable.Range(1, 20), a);
    }
}
