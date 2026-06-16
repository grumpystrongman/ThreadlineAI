using Threadline.Core;
using Threadline.Infrastructure;

namespace Threadline.Infrastructure.Tests;

public sealed class SessionServiceTests
{
    [Fact]
    public async Task StartAsync_PersistsActiveSession()
    {
        var repository = new InMemorySessionRepository();
        var service = CreateService(repository);

        var session = await service.StartAsync("Fix build", "openai");
        var saved = await repository.GetSessionAsync(session.Id);

        Assert.NotNull(saved);
        Assert.Equal("Fix build", saved.Name);
        Assert.Equal("openai", saved.ActiveProvider);
    }

    [Fact]
    public async Task AppendContextAsync_RedactsSecretsBeforeStorage()
    {
        var repository = new InMemorySessionRepository();
        var service = CreateService(repository);
        var session = await service.StartAsync("Secret test");
        var contextEvent = ContextEvent.Create(session.Id, ContextSource.Manual, "note", "api_key=abc123456789", DateTimeOffset.UtcNow);

        var saved = await service.AppendContextAsync(contextEvent);
        var events = await repository.GetRecentEventsAsync(session.Id, 10);

        Assert.Equal(saved.Id, events.Single().Id);
        Assert.Equal("api_key=[REDACTED]", events.Single().Content);
    }

    [Fact]
    public async Task AppendContextAsync_ThrowsWhenPolicyBlocks()
    {
        var repository = new InMemorySessionRepository();
        var policy = new CapturePolicy([
            CaptureRule.Create(CaptureRuleType.ProcessName, "blocked.exe", CaptureRuleAction.Block, DateTimeOffset.UtcNow)
        ]);
        var service = new SessionService(repository, new SystemClock(), new SecretRedactor(), policy);
        var session = await service.StartAsync("Blocked test");
        var contextEvent = ContextEvent.Create(session.Id, ContextSource.ActiveWindow, "window", "blocked", DateTimeOffset.UtcNow, processName: "blocked.exe");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AppendContextAsync(contextEvent));
    }

    private static SessionService CreateService(InMemorySessionRepository repository) => new(
        repository,
        new SystemClock(),
        new SecretRedactor(),
        new CapturePolicy([]));
}
