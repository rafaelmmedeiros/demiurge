using Demiurge.Engine;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Engine.Tests;

namespace Demiurge.Harness.Tests;

/// <summary>
/// A world survives a restart: stop at any tick, resume from the store, and the future is
/// identical to a run that never stopped.
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public class SimulationSessionTests
{
    private static readonly WorldConfig Config = new() { Seed = 42 };

    private static List<string> Uninterrupted(int ticks)
    {
        var (world, genesis) = World.Create(Config);
        var events = new List<WorldEvent>(genesis.Events);
        for (int i = 0; i < ticks; i++)
            events.AddRange(world.Step(ScriptedPolicy.DecideAll(world)).Events);
        return events.Select(e => e.ToString()).ToList();
    }

    private static void Advance(SimulationSession session, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            session.Step(ScriptedPolicy.DecideAll(session.World));
    }

    [Theory]
    [InlineData(100)]  // resume = snapshot at tick 200 + events 201..250
    [InlineData(1000)] // no snapshot yet: resume = full replay from genesis
    public void Resumed_world_continues_exactly_like_one_that_never_stopped(int snapshotEvery)
    {
        using var db = new TempDatabase();

        var first = SimulationSession.Start(db.Store, Config, TimeProvider.System, snapshotEvery);
        Advance(first, 250);

        var resumed = SimulationSession.Resume(db.Store, first.WorldId, snapshotEvery); // "process restarted"
        Assert.Equal(250, resumed.World.Tick);
        Advance(resumed, 250);

        var fromStore = db.Store.ReadEvents(first.WorldId).Select(e => e.ToString()).ToList();
        Assert.Equal(Uninterrupted(500), fromStore);
    }

    [Fact]
    public void Snapshots_are_written_every_n_ticks()
    {
        using var db = new TempDatabase();
        var session = SimulationSession.Start(db.Store, Config, TimeProvider.System, snapshotEvery: 100);

        Advance(session, 250);

        Assert.Equal(200, db.Store.LoadLatestSnapshot(session.WorldId)!.Tick);
    }
}
