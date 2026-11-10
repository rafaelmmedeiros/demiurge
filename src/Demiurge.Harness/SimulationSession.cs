using Demiurge.Engine;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Harness.Persistence;

namespace Demiurge.Harness;

/// <summary>
/// A world bound to the event store: every tick's events are appended, and a snapshot is saved
/// every N ticks. Resume = latest snapshot + events after it (or full replay if there is none).
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public sealed class SimulationSession : ISimulation
{
    public const int DefaultSnapshotEvery = 100;

    private readonly SqliteEventStore _store;
    private readonly int _snapshotEvery;

    private SimulationSession(SqliteEventStore store, long worldId, World world, int snapshotEvery)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshotEvery, 1);
        _store = store;
        WorldId = worldId;
        World = world;
        _snapshotEvery = snapshotEvery;
    }

    public long WorldId { get; }

    public World World { get; }

    public SqliteEventStore Store => _store;

    /// <summary>Creates a new world (new WorldId) and persists its genesis.</summary>
    public static SimulationSession Start(
        SqliteEventStore store, WorldConfig config, TimeProvider clock, int snapshotEvery = DefaultSnapshotEvery)
    {
        long worldId = store.CreateWorld(config, clock.GetUtcNow());
        var (world, genesis) = World.Create(config);
        store.Append(worldId, genesis.Events);
        return new SimulationSession(store, worldId, world, snapshotEvery);
    }

    /// <summary>Rebuilds a persisted world exactly where it stopped, RNG included.</summary>
    public static SimulationSession Resume(SqliteEventStore store, long worldId, int snapshotEvery = DefaultSnapshotEvery)
    {
        var config = store.LoadConfig(worldId);
        var snapshot = store.LoadLatestSnapshot(worldId);
        var world = snapshot is null
            ? World.FromEvents(config, store.ReadEvents(worldId))
            : World.FromSnapshot(config, snapshot, store.ReadEvents(worldId, afterTick: snapshot.Tick));
        return new SimulationSession(store, worldId, world, snapshotEvery);
    }

    public TickReport Step(IReadOnlyList<AgentDecision> decisions)
    {
        var report = World.Step(decisions);
        _store.Append(WorldId, report.Events);
        if (report.Tick % _snapshotEvery == 0)
            _store.SaveSnapshot(WorldId, World.ToSnapshot());
        return report;
    }
}
