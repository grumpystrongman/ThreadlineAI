using Microsoft.Data.Sqlite;
using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Infrastructure.Tests;

public sealed class SqliteReleaseConfidenceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"threadline-build23-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task InitializeAsync_IsIdempotentMigrationAndDatabaseRemainsWritable()
    {
        var store = CreateStore();

        await store.InitializeAsync();
        await store.InitializeAsync();
        await store.AppendAuditEventAsync(AuditEvent.Create(null, AuditEventType.AdapterHeartbeat, DateTimeOffset.UtcNow, "Build 23 SQLite writable probe."));

        var events = await store.GetRecentAuditEventsAsync(null, 10);

        Assert.Contains(events, item => item.Message == "Build 23 SQLite writable probe.");
    }

    [Fact]
    public async Task InitializeAsync_CreatesExpectedSchemaTablesAndIndexes()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type IN ('table', 'index')
            ORDER BY name;
            """;

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Contains("Sessions", names);
        Assert.Contains("ContextEvents", names);
        Assert.Contains("SessionSummaries", names);
        Assert.Contains("ProviderConnections", names);
        Assert.Contains("AuditEvents", names);
        Assert.Contains("IX_ContextEvents_Session_Timestamp", names);
        Assert.Contains("IX_AuditEvents_Session_Timestamp", names);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private SqliteThreadlineStore CreateStore() => new(new SqliteOptions($"Data Source={_databasePath};Pooling=False", _databasePath));
}
