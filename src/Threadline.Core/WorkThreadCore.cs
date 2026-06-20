namespace Threadline.Core;

public enum WorkThreadStatus
{
    Open,
    Closed
}

public enum WorkCaptureMode
{
    Followed,
    Locked,
    Manual,
    Inferred
}

public sealed record WorkThread(
    string Id,
    string Title,
    string? Description,
    WorkThreadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt = null,
    DateTimeOffset? LastResumedAt = null)
{
    public static WorkThread Create(string title, DateTimeOffset now, string? description = null) =>
        new($"thr_{Guid.NewGuid():N}", NormalizeTitle(title), NormalizeDescription(description), WorkThreadStatus.Open, now, now, null, now);

    public WorkThread Rename(string title, DateTimeOffset now, string? description = null) =>
        this with { Title = NormalizeTitle(title), Description = NormalizeDescription(description), UpdatedAt = now };

    public WorkThread Resume(DateTimeOffset now) =>
        this with { Status = WorkThreadStatus.Open, LastResumedAt = now, UpdatedAt = now, ClosedAt = null };

    public WorkThread Close(DateTimeOffset now) =>
        this with { Status = WorkThreadStatus.Closed, ClosedAt = now, UpdatedAt = now };

    private static string NormalizeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? $"Work Thread {DateTimeOffset.Now:g}" : title.Trim();

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

public sealed record WorkContextEvent(
    string Id,
    string WorkThreadId,
    string SourceType,
    string SourceName,
    string? AppName,
    string? WindowTitle,
    string? Url,
    string? ContentSummary,
    WorkCaptureMode CaptureMode,
    DateTimeOffset CreatedAt)
{
    public static WorkContextEvent Create(
        string workThreadId,
        string sourceType,
        string sourceName,
        WorkCaptureMode captureMode,
        DateTimeOffset now,
        string? appName = null,
        string? windowTitle = null,
        string? url = null,
        string? contentSummary = null) =>
        new(
            $"wce_{Guid.NewGuid():N}",
            workThreadId,
            Normalize(sourceType, "Unknown"),
            Normalize(sourceName, "Unknown context"),
            NormalizeOptional(appName),
            NormalizeOptional(windowTitle),
            NormalizeOptional(url),
            NormalizeOptional(contentSummary),
            captureMode,
            now);

    private static string Normalize(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ConversationMessage(
    string Id,
    string WorkThreadId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    string? ContextReceiptId = null)
{
    public static ConversationMessage Create(string workThreadId, string role, string content, DateTimeOffset now, string? contextReceiptId = null) =>
        new($"msg_{Guid.NewGuid():N}", workThreadId, NormalizeRole(role), content.Trim(), now, contextReceiptId);

    private static string NormalizeRole(string role) =>
        string.IsNullOrWhiteSpace(role) ? "note" : role.Trim().ToLowerInvariant();
}

public sealed record ContextReceiptRecord(
    string Id,
    string WorkThreadId,
    string UsedSourcesJson,
    string? NotUsedSourcesJson,
    string? Limitations,
    DateTimeOffset CreatedAt)
{
    public static ContextReceiptRecord Create(
        string workThreadId,
        string usedSourcesJson,
        DateTimeOffset now,
        string? notUsedSourcesJson = null,
        string? limitations = null) =>
        new($"crp_{Guid.NewGuid():N}", workThreadId, usedSourcesJson.Trim(), NormalizeOptional(notUsedSourcesJson), NormalizeOptional(limitations), now);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record WorkArtifact(
    string Id,
    string WorkThreadId,
    string ArtifactType,
    string Title,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ContextReceiptId = null)
{
    public static WorkArtifact Create(string workThreadId, string artifactType, string title, string content, DateTimeOffset now, string? contextReceiptId = null) =>
        new($"art_{Guid.NewGuid():N}", workThreadId, artifactType.Trim(), title.Trim(), content.Trim(), now, now, contextReceiptId);
}

public interface IWorkThreadRepository
{
    Task<WorkThread?> GetWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default);
    Task<WorkThread?> GetActiveWorkThreadAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkThread>> ListWorkThreadsAsync(int take = 25, CancellationToken cancellationToken = default);
    Task SaveWorkThreadAsync(WorkThread workThread, CancellationToken cancellationToken = default);

    Task AppendWorkContextEventAsync(WorkContextEvent contextEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkContextEvent>> GetRecentWorkContextEventsAsync(string workThreadId, int take = 20, CancellationToken cancellationToken = default);

    Task AppendConversationMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(string workThreadId, int take = 100, CancellationToken cancellationToken = default);

    Task SaveContextReceiptAsync(ContextReceiptRecord contextReceipt, CancellationToken cancellationToken = default);
    Task<ContextReceiptRecord?> GetContextReceiptAsync(string contextReceiptId, CancellationToken cancellationToken = default);

    Task SaveArtifactAsync(WorkArtifact artifact, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkArtifact>> GetArtifactsAsync(string workThreadId, int take = 25, CancellationToken cancellationToken = default);
}
