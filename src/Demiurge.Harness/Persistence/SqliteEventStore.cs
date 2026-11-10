using System.Globalization;
using System.Text.Json;
using Dapper;
using Demiurge.Engine;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Microsoft.Data.Sqlite;

namespace Demiurge.Harness.Persistence;

/// <summary>
/// Append-only event store on SQLite, accessed with Dapper. The events table only ever receives
/// INSERTs: it is the source of truth. Snapshots are a cache to avoid replaying from genesis.
/// Each call opens a pooled connection; each tick is written in one transaction.
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing ; decision: docs/DECISIONS.md ADR-004
/// </summary>
public sealed class SqliteEventStore
{
    private const string Schema = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS worlds (
            world_id    INTEGER PRIMARY KEY,
            seed        TEXT    NOT NULL,   -- ulong does not fit SQLite's signed 64-bit INTEGER
            config      TEXT    NOT NULL,   -- WorldConfig as JSON
            created_utc TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS events (
            world_id INTEGER NOT NULL,
            seq      INTEGER NOT NULL,      -- position in the world's stream, 1..n, no gaps
            tick     INTEGER NOT NULL,
            type     TEXT    NOT NULL,      -- class name, also inside payload as "$type"
            payload  TEXT    NOT NULL,
            PRIMARY KEY (world_id, seq)
        );
        CREATE INDEX IF NOT EXISTS ix_events_world_tick ON events (world_id, tick);

        CREATE TABLE IF NOT EXISTS snapshots (
            world_id INTEGER NOT NULL,
            tick     INTEGER NOT NULL,
            state    TEXT    NOT NULL,      -- WorldSnapshot as JSON, RNG state included
            PRIMARY KEY (world_id, tick)
        );
        """;

    private readonly string _connectionString;

    public SqliteEventStore(string connectionString)
    {
        _connectionString = connectionString;
        using var connection = Open();
        connection.Execute(Schema);
    }

    /// <summary>Convenience: a store on a database file path.</summary>
    public static SqliteEventStore ForFile(string path) =>
        new(new SqliteConnectionStringBuilder { DataSource = path }.ToString());

    public long CreateWorld(WorldConfig config, DateTimeOffset createdUtc)
    {
        using var connection = Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO worlds (seed, config, created_utc) VALUES (@Seed, @Config, @CreatedUtc);
            SELECT last_insert_rowid();
            """,
            new
            {
                Seed = config.Seed.ToString(CultureInfo.InvariantCulture),
                Config = JsonSerializer.Serialize(config, EventJson.Options),
                CreatedUtc = createdUtc.ToString("O", CultureInfo.InvariantCulture),
            });
    }

    public WorldConfig LoadConfig(long worldId)
    {
        using var connection = Open();
        var json = connection.QuerySingle<string>("SELECT config FROM worlds WHERE world_id = @worldId", new { worldId });
        return JsonSerializer.Deserialize<WorldConfig>(json, EventJson.Options)!;
    }

    public long? LatestWorldId()
    {
        using var connection = Open();
        return connection.ExecuteScalar<long?>("SELECT MAX(world_id) FROM worlds");
    }

    /// <summary>Appends events at the end of the world's stream, atomically.</summary>
    public void Append(long worldId, IReadOnlyList<WorldEvent> events)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        long last = connection.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(seq), 0) FROM events WHERE world_id = @worldId", new { worldId }, transaction);

        var rows = events.Select((e, i) => new
        {
            WorldId = worldId,
            Seq = last + 1 + i,
            e.Tick,
            Type = e.GetType().Name,
            Payload = EventJson.Serialize(e),
        });

        connection.Execute(
            "INSERT INTO events (world_id, seq, tick, type, payload) VALUES (@WorldId, @Seq, @Tick, @Type, @Payload)",
            rows, transaction);

        transaction.Commit();
    }

    /// <summary>Events of a world in stream order, optionally only those after a tick.</summary>
    public IReadOnlyList<WorldEvent> ReadEvents(long worldId, long afterTick = -1)
    {
        using var connection = Open();
        return connection
            .Query<string>(
                "SELECT payload FROM events WHERE world_id = @worldId AND tick > @afterTick ORDER BY seq",
                new { worldId, afterTick })
            .Select(EventJson.DeserializeEvent)
            .ToList();
    }

    public void SaveSnapshot(long worldId, WorldSnapshot snapshot)
    {
        using var connection = Open();
        connection.Execute(
            "INSERT OR REPLACE INTO snapshots (world_id, tick, state) VALUES (@worldId, @Tick, @State)",
            new { worldId, snapshot.Tick, State = JsonSerializer.Serialize(snapshot, EventJson.Options) });
    }

    public WorldSnapshot? LoadLatestSnapshot(long worldId)
    {
        using var connection = Open();
        var json = connection.QuerySingleOrDefault<string>(
            "SELECT state FROM snapshots WHERE world_id = @worldId ORDER BY tick DESC LIMIT 1", new { worldId });
        return json is null ? null : JsonSerializer.Deserialize<WorldSnapshot>(json, EventJson.Options);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
