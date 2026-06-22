namespace Threadline.Core;

public enum WindowAttachmentStatus
{
    Attached,
    Detached
}

public sealed record WindowSnapshot(
    string Id,
    DateTimeOffset CapturedAt,
    string ApplicationName,
    string ProcessName,
    string WindowTitle,
    int? ProcessId = null,
    string? ExecutablePath = null,
    string? Uri = null,
    bool IsForeground = true,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static WindowSnapshot Create(
        DateTimeOffset capturedAt,
        string applicationName,
        string processName,
        string windowTitle,
        int? processId = null,
        string? executablePath = null,
        string? uri = null,
        bool isForeground = true,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"win_{Guid.NewGuid():N}", capturedAt, applicationName.Trim(), processName.Trim(), windowTitle.Trim(), processId, executablePath, uri, isForeground, metadata);

    public ContextEvent ToContextEvent(string sessionId, bool userApproved = false) =>
        ContextEvent.Create(
            sessionId,
            ContextSource.ActiveWindow,
            "window-snapshot",
            BuildContextContent(),
            CapturedAt,
            applicationName: ApplicationName,
            processName: ProcessName,
            windowTitle: WindowTitle,
            uri: Uri,
            userApproved: userApproved,
            metadata: BuildContextMetadata());

    private string BuildContextContent()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Application: {ApplicationName}");
        builder.AppendLine($"Process: {ProcessName}");
        builder.AppendLine($"Window: {WindowTitle}");

        var provider = TryGetMetadata("nativeContext.providerName");
        var level = TryGetMetadata("nativeContext.levelDisplay") ?? TryGetMetadata("nativeContext.level");
        var guidance = TryGetMetadata("nativeContext.guidance");
        var nativeContent = TryGetMetadata("nativeContext.content");
        var warnings = TryGetMetadata("nativeContext.warnings");

        if (!string.IsNullOrWhiteSpace(provider)) builder.AppendLine($"Native provider: {provider}");
        if (!string.IsNullOrWhiteSpace(level)) builder.AppendLine($"Context level: {level}");
        if (!string.IsNullOrWhiteSpace(guidance)) builder.AppendLine($"Capture note: {guidance}");
        if (!string.IsNullOrWhiteSpace(warnings)) builder.AppendLine($"Capture warnings: {warnings}");

        if (!string.IsNullOrWhiteSpace(nativeContent))
        {
            builder.AppendLine();
            builder.AppendLine("Captured context:");
            builder.AppendLine(nativeContent);
        }

        return builder.ToString().TrimEnd();
    }

    private string? TryGetMetadata(string key)
    {
        if (Metadata is null) return null;
        return Metadata.TryGetValue(key, out var value) ? value : null;
    }

    private IReadOnlyDictionary<string, string> BuildContextMetadata()
    {
        var result = new Dictionary<string, string>
        {
            ["windowSnapshotId"] = Id,
            ["isForeground"] = IsForeground.ToString()
        };

        if (ProcessId is not null) result["processId"] = ProcessId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(ExecutablePath)) result["executablePath"] = ExecutablePath;
        if (Metadata is not null)
        {
            foreach (var pair in Metadata)
            {
                result[$"window.{pair.Key}"] = pair.Value;
            }
        }

        return result;
    }
}

public sealed record WindowAttachment(
    string Id,
    string SessionId,
    WindowSnapshot Snapshot,
    WindowAttachmentStatus Status,
    DateTimeOffset AttachedAt,
    DateTimeOffset? DetachedAt = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static WindowAttachment Attach(string sessionId, WindowSnapshot snapshot, DateTimeOffset attachedAt, IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"att_{Guid.NewGuid():N}", sessionId, snapshot, WindowAttachmentStatus.Attached, attachedAt, null, metadata);

    public WindowAttachment Detach(DateTimeOffset detachedAt) => this with { Status = WindowAttachmentStatus.Detached, DetachedAt = detachedAt };
}

public enum WindowActionKind
{
    CopyToClipboard,
    PasteText,
    InsertText,
    ClickElement,
    FocusWindow,
    RunCommand
}

public enum WindowActionRisk
{
    Low,
    Medium,
    High
}

public enum WindowActionStatus
{
    Proposed,
    Approved,
    Completed,
    Failed,
    Cancelled
}

public sealed record WindowActionRequest(
    string Id,
    string SessionId,
    string? AttachmentId,
    WindowActionKind Kind,
    string Description,
    string Payload,
    WindowActionRisk Risk,
    WindowActionStatus Status,
    bool RequiresApproval,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? ResultMessage = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static WindowActionRequest Propose(
        string sessionId,
        WindowActionKind kind,
        string description,
        string payload,
        DateTimeOffset createdAt,
        string? attachmentId = null,
        WindowActionRisk risk = WindowActionRisk.Medium,
        bool requiresApproval = true,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"act_{Guid.NewGuid():N}", sessionId, attachmentId, kind, description.Trim(), payload, risk, WindowActionStatus.Proposed, requiresApproval, createdAt, null, null, null, metadata);

    public WindowActionRequest Approve(DateTimeOffset approvedAt) =>
        this with { Status = WindowActionStatus.Approved, ApprovedAt = approvedAt };

    public WindowActionRequest Complete(DateTimeOffset completedAt, string? resultMessage = null) =>
        this with { Status = WindowActionStatus.Completed, CompletedAt = completedAt, ResultMessage = resultMessage };

    public WindowActionRequest Fail(DateTimeOffset completedAt, string resultMessage) =>
        this with { Status = WindowActionStatus.Failed, CompletedAt = completedAt, ResultMessage = resultMessage };

    public WindowActionRequest Cancel(DateTimeOffset completedAt, string? resultMessage = null) =>
        this with { Status = WindowActionStatus.Cancelled, CompletedAt = completedAt, ResultMessage = resultMessage };
}

public interface IWindowAttachmentRepository
{
    Task<WindowAttachment> SaveAttachmentAsync(WindowAttachment attachment, CancellationToken cancellationToken = default);
    Task<WindowAttachment?> GetAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default);
    Task<WindowAttachment?> GetActiveAttachmentAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WindowAttachment>> ListAttachmentsAsync(string sessionId, int take, CancellationToken cancellationToken = default);
    Task<WindowActionRequest> SaveActionAsync(WindowActionRequest action, CancellationToken cancellationToken = default);
    Task<WindowActionRequest?> GetActionAsync(string actionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WindowActionRequest>> ListActionsAsync(string sessionId, int take, CancellationToken cancellationToken = default);
}
