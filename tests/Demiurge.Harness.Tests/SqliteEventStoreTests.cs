using Demiurge.Engine;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Tests;

namespace Demiurge.Harness.Tests;

/// <summary>
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public class SqliteEventStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Appended_events_are_read_back_in_order()
    {
        using var db = new TempDatabase();
        var (world, genesis) = World.Create(new WorldConfig());
        long id = db.Store.CreateWorld(world.Config, Now);

        db.Store.Append(id, genesis.Events);
        var tick1 = world.Step(ScriptedPolicy.DecideAll(world)).Events;
        db.Store.Append(id, tick1);

        Assert.Equal(genesis.Events.Concat(tick1), db.Store.ReadEvents(id));
        Assert.Equal(tick1, db.Store.ReadEvents(id, afterTick: 0));
    }

    [Fact]
    public void Worlds_have_separate_streams_and_configs()
    {
        using var db = new TempDatabase();
        long a = db.Store.CreateWorld(new WorldConfig { Seed = 1 }, Now);
        long b = db.Store.CreateWorld(new WorldConfig { Seed = 2 }, Now);

        db.Store.Append(a, [new TickStarted(1)]);

        Assert.Single(db.Store.ReadEvents(a));
        Assert.Empty(db.Store.ReadEvents(b));
        Assert.Equal(2UL, db.Store.LoadConfig(b).Seed);
        Assert.Equal(b, db.Store.LatestWorldId());
    }

    [Fact]
    public void Latest_snapshot_is_returned()
    {
        using var db = new TempDatabase();
        var (world, _) = World.Create(new WorldConfig());
        long id = db.Store.CreateWorld(world.Config, Now);

        world.Step();
        db.Store.SaveSnapshot(id, world.ToSnapshot());
        world.Step();
        var latest = world.ToSnapshot();
        db.Store.SaveSnapshot(id, latest);

        Assert.Equivalent(latest, db.Store.LoadLatestSnapshot(id), strict: true);
    }
}
