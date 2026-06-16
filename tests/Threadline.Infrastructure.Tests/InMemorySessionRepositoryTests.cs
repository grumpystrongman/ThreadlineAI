using Threadline.Core;
using Threadline.Infrastructure;

namespace Threadline.Infrastructure.Tests;

public sealed class InMemorySessionRepositoryTests
{
    [Fact]
    public async Task SaveSessionAsync_ThenGetActiveSessionAsync_ReturnsLatestActiveSession()
    {
        var repository = new InMemorySessionRepository();
        var older = ThreadlineSession.Start("Older", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = ThreadlineSession.Start("Newer", DateTimeOffset.UtcNow);

        await repository.SaveSessionAsync(older);
        await repository.SaveSessionAsync(newer);

        var active = await repository.GetActiveSessionAsync();

        Assert.NotNull(active);
        Assert.Equal(newer.Id, active.Id);
    }

    [Fact]
    public async Task AppendEventAsync_ThenGetRecentEventsAsync_ReturnsChronologicalEvents()
    {
        var repository = new InMemorySessionRepository();
        var session = ThreadlineSession.Start("Debug", DateTimeOffset.UtcNow);
        await repository.SaveSessionAsync(session);

        await repository.AppendEventAsync(ContextEvent.Create(session.Id, ContextSource.Manual, "note", "first", DateTimeOffset.UtcNow.AddMinutes(-2)));
        await repository.AppendEventAsync(ContextEvent.Create(session.Id, ContextSource.Manual, "note", "second", DateTimeOffset.UtcNow.AddMinutes(-1)));

        var events = await repository.GetRecentEventsAsync(session.Id, 10);

        Assert.Equal(["first", "second"], events.Select(e => e.Content).ToArray());
    }
}
