using System.Security.Cryptography;
using System.Text;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Engine.Tests;

/// <summary>
/// Same seed + same decisions = same world (CLAUDE.md rule 2).
/// Concept: Determinism and seed — docs/CONCEPTS.md#determinismo-e-seed
/// </summary>
public class DeterminismTests
{
    private const int Ticks = 500;

    [Fact]
    public void Same_seed_produces_identical_event_stream()
    {
        var first = RunAndHash(new WorldConfig { Seed = 42 }, Ticks);
        var second = RunAndHash(new WorldConfig { Seed = 42 }, Ticks);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_produce_different_worlds()
    {
        var a = RunAndHash(new WorldConfig { Seed = 42 }, Ticks);
        var b = RunAndHash(new WorldConfig { Seed = 43 }, Ticks);

        Assert.NotEqual(a, b);
    }

    /// <summary>SHA-256 over one line per event, in emission order.</summary>
    internal static string RunAndHash(WorldConfig config, int ticks)
    {
        var (world, genesis) = World.Create(config);
        var lines = new StringBuilder();
        Append(lines, genesis.Events);

        // Decisions come from a deterministic scripted policy, so the whole loop
        // (perceive → decide → validate/apply in seeded order) is covered by the hash.
        for (int i = 0; i < ticks; i++)
            Append(lines, world.Step(ScriptedPolicy.DecideAll(world)).Events);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString())));
    }

    private static void Append(StringBuilder sb, IEnumerable<WorldEvent> events)
    {
        foreach (var e in events)
            sb.Append(e).Append('\n');
    }
}
