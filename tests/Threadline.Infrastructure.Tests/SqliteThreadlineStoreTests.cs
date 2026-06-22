using Microsoft.Data.Sqlite;
using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Infrastructure.Tests;

public sealed class SqliteThreadlineStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"threadline-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task InitializeAsync_CreatesStoreAndPersistsSessionEventsAndSummary()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var session = ThreadlineSession.Start("SQLite session", DateTimeOffset.UtcNow, "openai");
        await store.SaveSessionAsync(session);
        await store.AppendEventAsync(ContextEvent.Create(session.Id, ContextSource.Manual, "note", "first", DateTimeOffset.UtcNow.AddMinutes(-2)));
        await store.AppendEventAsync(ContextEvent.Create(session.Id, ContextSource.Manual, "note", "second", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await store.SaveSummaryAsync(session.Id, "User captured two notes.");

        var active = await store.GetActiveSessionAsync();
        var events = await store.GetRecentEventsAsync(session.Id, 10);
        var summary = await store.GetLatestSummaryAsync(session.Id);

        Assert.NotNull(active);
        Assert.Equal(session.Id, active.Id);
        Assert.Equal(["first", "second"], events.Select(e => e.Content).ToArray());
        Assert.Equal("User captured two notes.", summary);
    }

    [Fact]
    public async Task ProviderConnections_ArePersistedAndListed()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var connection = ProviderConnection.Create(
            "OpenAI",
            ProviderAuthType.ApiKey,
            DateTimeOffset.UtcNow,
            credentialReference: "credref://openai",
            defaultModel: "gpt-4.1",
            status: ProviderConnectionStatus.Ready);

        await store.SaveProviderConnectionAsync(connection);

        var saved = await store.GetProviderConnectionAsync("OpenAI");
        var providers = await store.ListProviderConnectionsAsync();

        Assert.NotNull(saved);
        Assert.Equal("OpenAI", saved.ProviderName);
        Assert.Equal("credref://openai", saved.CredentialReference);
        Assert.Single(providers);
    }

    [Fact]
    public async Task AuditEvents_ArePersistedInChronologicalOrder()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var sessionId = "ses_test";

        await store.AppendAuditEventAsync(AuditEvent.Create(sessionId, AuditEventType.SessionStarted, DateTimeOffset.UtcNow.AddMinutes(-2), "started"));
        await store.AppendAuditEventAsync(AuditEvent.Create(sessionId, AuditEventType.ContextStored, DateTimeOffset.UtcNow.AddMinutes(-1), "stored"));

        var events = await store.GetRecentAuditEventsAsync(sessionId, 10);

        Assert.Equal(["started", "stored"], events.Select(e => e.Message).ToArray());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private SqliteThreadlineStore CreateStore() => new(new SqliteOptions($"Data Source={_databasePath};Pooling=False", _databasePath));
}
