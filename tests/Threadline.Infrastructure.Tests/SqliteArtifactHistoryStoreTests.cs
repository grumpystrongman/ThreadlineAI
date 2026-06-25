using Microsoft.Data.Sqlite;
using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Infrastructure.Tests;

public sealed class SqliteArtifactHistoryStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"threadline-ah-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAndGetArtifactHistory_RoundTrips()
    {
        var (workStore, historyStore) = await CreateInitializedStores();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await workStore.SaveWorkThreadAsync(thread);

        var artifact = WorkArtifact.Create(thread.Id, "code", "Helper", "v1 content", now);
        await workStore.SaveArtifactAsync(artifact);

        await historyStore.SaveArtifactVersionAsync(artifact, "generated", "act_1");

        var history = await historyStore.GetArtifactHistoryAsync(artifact.Id);

        Assert.Single(history);
        Assert.Equal(1, history[0].Version);
        Assert.Equal("generated", history[0].Operation);
        Assert.Equal("act_1", history[0].ActionId);
        Assert.Equal("v1 content", history[0].Content);
    }

    [Fact]
    public async Task SaveMultipleVersions_IncrementsVersionNumber()
    {
        var (workStore, historyStore) = await CreateInitializedStores();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await workStore.SaveWorkThreadAsync(thread);

        var artifact = WorkArtifact.Create(thread.Id, "code", "Helper", "v1", now);
        await workStore.SaveArtifactAsync(artifact);

        await historyStore.SaveArtifactVersionAsync(artifact, "generated");

        var updatedArtifact = artifact with { Content = "v2", UpdatedAt = now.AddMinutes(1) };
        await historyStore.SaveArtifactVersionAsync(updatedArtifact, "regenerated");

        var history = await historyStore.GetArtifactHistoryAsync(artifact.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal(1, history[0].Version);
        Assert.Equal(2, history[1].Version);
        Assert.Equal("v1", history[0].Content);
        Assert.Equal("v2", history[1].Content);
    }

    [Fact]
    public async Task GetArtifactHistory_ReturnsEmptyForUnknownArtifact()
    {
        var (_, historyStore) = await CreateInitializedStores();

        var history = await historyStore.GetArtifactHistoryAsync("art_nonexistent");
        Assert.Empty(history);
    }

    [Fact]
    public async Task GetArtifactHistory_ReturnsEmptyForBlankId()
    {
        var (_, historyStore) = await CreateInitializedStores();

        var history = await historyStore.GetArtifactHistoryAsync("   ");
        Assert.Empty(history);
    }

    [Fact]
    public async Task SaveArtifactVersion_NormalizesBlankOperationToDefault()
    {
        var (workStore, historyStore) = await CreateInitializedStores();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await workStore.SaveWorkThreadAsync(thread);

        var artifact = WorkArtifact.Create(thread.Id, "note", "Note", "text", now);
        await workStore.SaveArtifactAsync(artifact);

        await historyStore.SaveArtifactVersionAsync(artifact, "  ");

        var history = await historyStore.GetArtifactHistoryAsync(artifact.Id);
        Assert.Single(history);
        Assert.Equal("generated", history[0].Operation);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var (_, historyStore) = await CreateInitializedStores();
        await historyStore.InitializeAsync();

        var history = await historyStore.GetArtifactHistoryAsync("art_any");
        Assert.Empty(history);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private async Task<(SqliteWorkThreadStore workStore, SqliteArtifactHistoryStore historyStore)> CreateInitializedStores()
    {
        var options = new SqliteOptions($"Data Source={_databasePath};Pooling=False", _databasePath);
        var workStore = new SqliteWorkThreadStore(options);
        var historyStore = new SqliteArtifactHistoryStore(options);
        await workStore.InitializeAsync();
        await historyStore.InitializeAsync();
        return (workStore, historyStore);
    }
}
