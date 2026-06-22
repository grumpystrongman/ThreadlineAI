using Threadline.Core;

namespace Threadline.Service;

public sealed record StartWorkThreadRequest(string Title, string? Description = null);
public sealed record RenameWorkThreadRequest(string Title, string? Description = null);

public sealed record SaveWorkContextEventRequest(
    string SourceType,
    string SourceName,
    string? AppName,
    string? WindowTitle,
    string? Url,
    string? ContentSummary,
    WorkCaptureMode CaptureMode = WorkCaptureMode.Followed);

public sealed record SaveConversationMessageRequest(string Role, string Content, string? ContextReceiptId = null);
public sealed record SaveContextReceiptRequest(string UsedSourcesJson, string? NotUsedSourcesJson = null, string? Limitations = null);
public sealed record SaveArtifactRequest(string ArtifactType, string Title, string Content, string? ContextReceiptId = null);
public sealed record RegenerateArtifactRequest(string? Transcript = null, string? ContextSummary = null);
public sealed record ArtifactExportResponse(string FileName, string ContentType, string Content, WorkArtifact Artifact);
