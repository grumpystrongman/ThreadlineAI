using Threadline.Core;
using Threadline.Infrastructure.Windowing;

namespace Threadline.Infrastructure.Tests;

public sealed class WindowAttachmentServiceTests
{
    [Fact]
    public async Task AttachAsync_ReplacesActiveAttachmentForSession()
    {
        var repository = new InMemoryWindowAttachmentRepository();
        var sessionRepository = new InMemorySessionRepository();
        var sessions = CreateSessionService(sessionRepository);
        var session = await sessions.StartAsync("Window test");
        var service = CreateWindowService(repository, sessionRepository, sessions);

        var first = await service.AttachAsync(session.Id, WindowSnapshot.Create(DateTimeOffset.UtcNow, "PowerShell", "pwsh", "First"));
        var second = await service.AttachAsync(session.Id, WindowSnapshot.Create(DateTimeOffset.UtcNow, "Edge", "msedge", "Second"));
        var active = await service.GetActiveAsync(session.Id);
        var attachments = await service.ListAttachmentsAsync(session.Id, 10);

        Assert.Equal(second.Id, active!.Id);
        Assert.Contains(attachments, attachment => attachment.Id == first.Id && attachment.Status == WindowAttachmentStatus.Detached);
        Assert.Contains(attachments, attachment => attachment.Id == second.Id && attachment.Status == WindowAttachmentStatus.Attached);
    }

    [Fact]
    public async Task StoreActiveWindowContextAsync_PersistsWindowSnapshotAsContext()
    {
        var repository = new InMemoryWindowAttachmentRepository();
        var sessionRepository = new InMemorySessionRepository();
        var sessions = CreateSessionService(sessionRepository);
        var session = await sessions.StartAsync("Window context test");
        var service = CreateWindowService(repository, sessionRepository, sessions);

        await service.AttachAsync(session.Id, WindowSnapshot.Create(DateTimeOffset.UtcNow, "PowerShell", "pwsh", "Threadline smoke"));
        var stored = await service.StoreActiveWindowContextAsync(session.Id, userApproved: true);
        var events = await sessionRepository.GetRecentEventsAsync(session.Id, 10);

        Assert.Equal(stored.Id, events.Single().Id);
        Assert.Contains("Threadline smoke", events.Single().Content);
    }

    [Fact]
    public async Task ProposeActionAsync_CanApproveAndCompleteAction()
    {
        var repository = new InMemoryWindowAttachmentRepository();
        var sessionRepository = new InMemorySessionRepository();
        var sessions = CreateSessionService(sessionRepository);
        var session = await sessions.StartAsync("Action test");
        var service = CreateWindowService(repository, sessionRepository, sessions);

        await service.AttachAsync(session.Id, WindowSnapshot.Create(DateTimeOffset.UtcNow, "Notepad", "notepad", "Draft"));
        var action = await service.ProposeActionAsync(session.Id, WindowActionKind.InsertText, "Insert draft", "Hello", userApproved: true);
        var completed = await service.CompleteActionAsync(action.Id, "Inserted.");

        Assert.Equal(WindowActionStatus.Approved, action.Status);
        Assert.Equal(WindowActionStatus.Completed, completed.Status);
        Assert.Equal("Inserted.", completed.ResultMessage);
    }

    private static SessionService CreateSessionService(InMemorySessionRepository repository) => new(
        repository,
        new SystemClock(),
        new SecretRedactor(),
        new CapturePolicy([]));

    private static WindowAttachmentService CreateWindowService(InMemoryWindowAttachmentRepository repository, InMemorySessionRepository sessionRepository, SessionService sessions) =>
        new(repository, sessionRepository, sessions, new SystemClock());
}
