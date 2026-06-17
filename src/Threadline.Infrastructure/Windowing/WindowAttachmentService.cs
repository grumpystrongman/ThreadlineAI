using Threadline.Core;

namespace Threadline.Infrastructure.Windowing;

public sealed class WindowAttachmentService
{
    private readonly IWindowAttachmentRepository _repository;
    private readonly ISessionRepository _sessions;
    private readonly SessionService _sessionService;
    private readonly IClock _clock;
    private readonly IAuditRepository? _audit;

    public WindowAttachmentService(
        IWindowAttachmentRepository repository,
        ISessionRepository sessions,
        SessionService sessionService,
        IClock clock,
        IAuditRepository? audit = null)
    {
        _repository = repository;
        _sessions = sessions;
        _sessionService = sessionService;
        _clock = clock;
        _audit = audit;
    }

    public async Task<WindowAttachment> AttachAsync(string sessionId, WindowSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await EnsureSessionExistsAsync(sessionId, cancellationToken);
        var attachment = WindowAttachment.Attach(sessionId, snapshot, _clock.UtcNow);
        var saved = await _repository.SaveAttachmentAsync(attachment, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(sessionId, AuditEventType.WindowAttached, _clock.UtcNow, $"Attached window: {snapshot.ApplicationName} - {snapshot.WindowTitle}", WindowMetadata(saved)), cancellationToken);
        return saved;
    }

    public async Task<WindowAttachment?> DetachAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var active = await _repository.GetActiveAttachmentAsync(sessionId, cancellationToken);
        if (active is null)
        {
            return null;
        }

        var detached = active.Detach(_clock.UtcNow);
        await _repository.SaveAttachmentAsync(detached, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(sessionId, AuditEventType.WindowDetached, _clock.UtcNow, $"Detached window: {detached.Snapshot.ApplicationName} - {detached.Snapshot.WindowTitle}", WindowMetadata(detached)), cancellationToken);
        return detached;
    }

    public Task<WindowAttachment?> GetActiveAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _repository.GetActiveAttachmentAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<WindowAttachment>> ListAttachmentsAsync(string sessionId, int take, CancellationToken cancellationToken = default) =>
        _repository.ListAttachmentsAsync(sessionId, take, cancellationToken);

    public async Task<ContextPreview> PreviewActiveWindowContextAsync(string sessionId, bool userApproved, CancellationToken cancellationToken = default)
    {
        var active = await _repository.GetActiveAttachmentAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("No active window attachment exists for this session.");
        var contextEvent = active.Snapshot.ToContextEvent(sessionId, userApproved);
        var preview = await _sessionService.PreviewContextAsync(contextEvent, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(sessionId, AuditEventType.WindowContextPreviewed, _clock.UtcNow, "Active window context previewed.", WindowMetadata(active)), cancellationToken);
        return preview;
    }

    public async Task<ContextEvent> StoreActiveWindowContextAsync(string sessionId, bool userApproved, CancellationToken cancellationToken = default)
    {
        var active = await _repository.GetActiveAttachmentAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("No active window attachment exists for this session.");
        var contextEvent = active.Snapshot.ToContextEvent(sessionId, userApproved);
        return await _sessionService.AppendContextAsync(contextEvent, cancellationToken);
    }

    public async Task<WindowActionRequest> ProposeActionAsync(string sessionId, WindowActionKind kind, string description, string payload, bool userApproved, string? attachmentId = null, WindowActionRisk risk = WindowActionRisk.Medium, CancellationToken cancellationToken = default)
    {
        await EnsureSessionExistsAsync(sessionId, cancellationToken);
        var active = string.IsNullOrWhiteSpace(attachmentId)
            ? await _repository.GetActiveAttachmentAsync(sessionId, cancellationToken)
            : await _repository.GetAttachmentAsync(attachmentId, cancellationToken);
        var riskRequiresApproval = risk is WindowActionRisk.Medium or WindowActionRisk.High;
        var action = WindowActionRequest.Propose(sessionId, kind, description, payload, _clock.UtcNow, active?.Id, risk, riskRequiresApproval);
        if (userApproved)
        {
            action = action.Approve(_clock.UtcNow);
        }

        var saved = await _repository.SaveActionAsync(action, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(sessionId, AuditEventType.WindowActionProposed, _clock.UtcNow, $"Window action proposed: {saved.Kind}", ActionMetadata(saved)), cancellationToken);
        if (saved.Status == WindowActionStatus.Approved)
        {
            await AppendAuditAsync(AuditEvent.Create(sessionId, AuditEventType.WindowActionApproved, _clock.UtcNow, $"Window action approved: {saved.Kind}", ActionMetadata(saved)), cancellationToken);
        }

        return saved;
    }

    public async Task<WindowActionRequest> ApproveActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        var action = await RequireActionAsync(actionId, cancellationToken);
        var approved = action.Approve(_clock.UtcNow);
        await _repository.SaveActionAsync(approved, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(approved.SessionId, AuditEventType.WindowActionApproved, _clock.UtcNow, $"Window action approved: {approved.Kind}", ActionMetadata(approved)), cancellationToken);
        return approved;
    }

    public async Task<WindowActionRequest> CompleteActionAsync(string actionId, string? resultMessage, CancellationToken cancellationToken = default)
    {
        var action = await RequireActionAsync(actionId, cancellationToken);
        var completed = action.Complete(_clock.UtcNow, resultMessage);
        await _repository.SaveActionAsync(completed, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(completed.SessionId, AuditEventType.WindowActionCompleted, _clock.UtcNow, $"Window action completed: {completed.Kind}", ActionMetadata(completed)), cancellationToken);
        return completed;
    }

    public async Task<WindowActionRequest> FailActionAsync(string actionId, string resultMessage, CancellationToken cancellationToken = default)
    {
        var action = await RequireActionAsync(actionId, cancellationToken);
        var failed = action.Fail(_clock.UtcNow, resultMessage);
        await _repository.SaveActionAsync(failed, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(failed.SessionId, AuditEventType.WindowActionFailed, _clock.UtcNow, $"Window action failed: {failed.Kind}", ActionMetadata(failed)), cancellationToken);
        return failed;
    }

    public Task<IReadOnlyList<WindowActionRequest>> ListActionsAsync(string sessionId, int take, CancellationToken cancellationToken = default) =>
        _repository.ListActionsAsync(sessionId, take, cancellationToken);

    private async Task EnsureSessionExistsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await _sessions.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Session does not exist: {sessionId}");
        }
    }

    private async Task<WindowActionRequest> RequireActionAsync(string actionId, CancellationToken cancellationToken)
    {
        var action = await _repository.GetActionAsync(actionId, cancellationToken);
        return action ?? throw new InvalidOperationException($"Window action does not exist: {actionId}");
    }

    private static IReadOnlyDictionary<string, string> WindowMetadata(WindowAttachment attachment) => new Dictionary<string, string>
    {
        ["attachmentId"] = attachment.Id,
        ["windowSnapshotId"] = attachment.Snapshot.Id,
        ["applicationName"] = attachment.Snapshot.ApplicationName,
        ["processName"] = attachment.Snapshot.ProcessName,
        ["windowTitle"] = attachment.Snapshot.WindowTitle,
        ["status"] = attachment.Status.ToString()
    };

    private static IReadOnlyDictionary<string, string> ActionMetadata(WindowActionRequest action) => new Dictionary<string, string>
    {
        ["actionId"] = action.Id,
        ["kind"] = action.Kind.ToString(),
        ["risk"] = action.Risk.ToString(),
        ["status"] = action.Status.ToString(),
        ["requiresApproval"] = action.RequiresApproval.ToString()
    };

    private Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        _audit is null ? Task.CompletedTask : _audit.AppendAuditEventAsync(auditEvent, cancellationToken);
}
