using Demiurge.Harness.Persistence;
using Microsoft.Data.Sqlite;

namespace Demiurge.Harness.Tests;

/// <summary>A throwaway SQLite file per test, deleted on dispose.</summary>
internal sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"demiurge-test-{Guid.NewGuid():N}.db");
        Store = SqliteEventStore.ForFile(Path);
    }

    public string Path { get; }

    public SqliteEventStore Store { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // pooled connections keep the file locked on Windows
        foreach (var file in new[] { Path, Path + "-wal", Path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
