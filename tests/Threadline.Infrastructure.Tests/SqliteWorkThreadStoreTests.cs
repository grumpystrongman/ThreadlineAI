using Microsoft.Data.Sqlite;
using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Infrastructure.Tests;

public sealed class SqliteWorkThreadStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"threadline-wt-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAndGetWorkThread_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("Build feature", now, description: "A feature");
        await store.SaveWorkThreadAsync(thread);

        var retrieved = await store.GetWorkThreadAsync(thread.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(thread.Id, retrieved.Id);
        Assert.Equal("Build feature", retrieved.Title);
        Assert.Equal("A feature", retrieved.Description);
        Assert.Equal(WorkThreadStatus.Open, retrieved.Status);
    }

    [Fact]
    public async Task GetWorkThread_ReturnsNullForMissingId()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var result = await store.GetWorkThreadAsync("thr_nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveWorkThread_ReturnsLatestOpenThread()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var older = WorkThread.Create("Older", now.AddMinutes(-10));
        var newer = WorkThread.Create("Newer", now);

        await store.SaveWorkThreadAsync(older);
        await store.SaveWorkThreadAsync(newer);

        var active = await store.GetActiveWorkThreadAsync();

        Assert.NotNull(active);
        Assert.Equal("Newer", active.Title);
    }

    [Fact]
    public async Task GetActiveWorkThread_IgnoresClosedThreads()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var closed = WorkThread.Create("Closed", now).Close(now.AddMinutes(1));
        await store.SaveWorkThreadAsync(closed);

        var active = await store.GetActiveWorkThreadAsync();
        Assert.Null(active);
    }

    [Fact]
    public async Task ListWorkThreads_ReturnsInDescendingOrder()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        await store.SaveWorkThreadAsync(WorkThread.Create("First", now.AddMinutes(-2)));
        await store.SaveWorkThreadAsync(WorkThread.Create("Second", now.AddMinutes(-1)));
        await store.SaveWorkThreadAsync(WorkThread.Create("Third", now));

        var list = await store.ListWorkThreadsAsync(take: 10);

        Assert.Equal(3, list.Count);
        Assert.Equal("Third", list[0].Title);
        Assert.Equal("First", list[2].Title);
    }

    [Fact]
    public async Task SaveWorkThread_UpsertOverwritesExisting()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("Original", now);
        await store.SaveWorkThreadAsync(thread);

        var renamed = thread.Rename("Updated", now.AddMinutes(1));
        await store.SaveWorkThreadAsync(renamed);

        var retrieved = await store.GetWorkThreadAsync(thread.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved.Title);
    }

    [Fact]
    public async Task AppendAndGetWorkContextEvents_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await store.SaveWorkThreadAsync(thread);

        var evt1 = WorkContextEvent.Create(thread.Id, "browser", "page", WorkCaptureMode.Followed, now.AddMinutes(-2), appName: "Chrome");
        var evt2 = WorkContextEvent.Create(thread.Id, "editor", "code", WorkCaptureMode.Locked, now.AddMinutes(-1), appName: "VSCode");

        await store.AppendWorkContextEventAsync(evt1);
        await store.AppendWorkContextEventAsync(evt2);

        var events = await store.GetRecentWorkContextEventsAsync(thread.Id, take: 10);

        Assert.Equal(2, events.Count);
        Assert.Equal("page", events[0].SourceName);
        Assert.Equal("code", events[1].SourceName);
    }

    [Fact]
    public async Task AppendAndGetConversationMessages_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await store.SaveWorkThreadAsync(thread);

        var msg1 = ConversationMessage.Create(thread.Id, "user", "Hello", now.AddMinutes(-2));
        var msg2 = ConversationMessage.Create(thread.Id, "assistant", "Hi there", now.AddMinutes(-1));

        await store.AppendConversationMessageAsync(msg1);
        await store.AppendConversationMessageAsync(msg2);

        var messages = await store.GetConversationMessagesAsync(thread.Id, take: 10);

        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello", messages[0].Content);
        Assert.Equal("Hi there", messages[1].Content);
    }

    [Fact]
    public async Task SaveAndGetContextReceipt_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await store.SaveWorkThreadAsync(thread);

        var receipt = ContextReceiptRecord.Create(thread.Id, "[\"source1\"]", now, notUsedSourcesJson: "[\"source2\"]", limitations: "token limit");
        await store.SaveContextReceiptAsync(receipt);

        var retrieved = await store.GetContextReceiptAsync(receipt.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(receipt.Id, retrieved.Id);
        Assert.Equal("[\"source1\"]", retrieved.UsedSourcesJson);
        Assert.Equal("[\"source2\"]", retrieved.NotUsedSourcesJson);
        Assert.Equal("token limit", retrieved.Limitations);
    }

    [Fact]
    public async Task GetContextReceipt_ReturnsNullForMissingId()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var result = await store.GetContextReceiptAsync("crp_nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndGetArtifacts_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var thread = WorkThread.Create("T", now);
        await store.SaveWorkThreadAsync(thread);

        var artifact = WorkArtifact.Create(thread.Id, "code", "Helper", "def helper(): pass", now);
        await store.SaveArtifactAsync(artifact);

        var artifacts = await store.GetArtifactsAsync(thread.Id, take: 10);

        Assert.Single(artifacts);
        Assert.Equal("Helper", artifacts[0].Title);
        Assert.Equal("def helper(): pass", artifacts[0].Content);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.InitializeAsync();

        var list = await store.ListWorkThreadsAsync();
        Assert.Empty(list);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private SqliteWorkThreadStore CreateStore() => new(new SqliteOptions($"Data Source={_databasePath};Pooling=False", _databasePath));
}
