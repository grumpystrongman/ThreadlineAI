using Threadline.Core;

namespace Threadline.Service;

public sealed record StartSessionRequest(string Name, string? Provider = null);

public sealed record AppendContextEventRequest(
    ContextSource Source,
    string ContextType,
    string Content,
    string? ApplicationName = null,
    string? ProcessName = null,
    string? WindowTitle = null,
    string? Uri = null,
    SensitivityLevel Sensitivity = SensitivityLevel.Normal,
    bool UserApproved = false,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ContextEvent ToContextEvent(string sessionId, DateTimeOffset timestamp) =>
        ContextEvent.Create(sessionId, Source, ContextType, Content, timestamp, ApplicationName, ProcessName, WindowTitle, Uri, Sensitivity, UserApproved, Metadata);
}

public sealed record AttachWindowRequest(
    string ApplicationName,
    string ProcessName,
    string WindowTitle,
    int? ProcessId = null,
    string? ExecutablePath = null,
    string? Uri = null,
    bool IsForeground = true,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public WindowSnapshot ToSnapshot(DateTimeOffset timestamp) =>
        WindowSnapshot.Create(timestamp, ApplicationName, ProcessName, WindowTitle, ProcessId, ExecutablePath, Uri, IsForeground, Metadata);
}

public sealed record StoreWindowContextRequest(bool UserApproved = true);

public sealed record ProposeWindowActionRequest(
    WindowActionKind Kind,
    string Description,
    string Payload,
    bool UserApproved = false,
    string? AttachmentId = null,
    WindowActionRisk Risk = WindowActionRisk.Medium);

public sealed record CompleteWindowActionRequest(string? ResultMessage = null, bool Failed = false);

public sealed record ComposePromptRequest(string Question, string? CurrentWindow = null, int? TakeRecentEvents = 20);

public sealed record AskResponse(
    string Answer,
    IReadOnlyList<LlmMessage> Messages,
    string? ProviderName = null,
    string? Model = null,
    long DurationMs = 0);

public sealed record SaveSummaryRequest(string Summary);

public sealed record SaveProviderConnectionRequest(
    string ProviderName,
    ProviderAuthType AuthType,
    string? Id = null,
    string? CredentialReference = null,
    string? BaseUrl = null,
    string? DefaultModel = null,
    ProviderConnectionStatus Status = ProviderConnectionStatus.NeedsConfiguration,
    DateTimeOffset? CreatedAt = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record SaveProviderCredentialRequest(
    string SecretValue,
    ProviderAuthType AuthType = ProviderAuthType.ApiKey,
    string? BaseUrl = null,
    string? DefaultModel = null,
    ProviderConnectionStatus Status = ProviderConnectionStatus.Ready,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record SecretDescriptorResponse(
    string Reference,
    string Name,
    SecretProtectionKind ProtectionKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static SecretDescriptorResponse FromDescriptor(SecretDescriptor descriptor) =>
        new(descriptor.Reference, descriptor.Name, descriptor.ProtectionKind, descriptor.CreatedAt, descriptor.UpdatedAt, descriptor.Metadata);
}

public sealed record RegisterAdapterRequest(
    AdapterKind Kind,
    string DisplayName,
    AdapterPermission Permissions,
    string? Version = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AdapterHeartbeatRequest(
    string? Version = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ProviderTestResult(
    string ProviderName,
    bool Success,
    ThreadlineDoctorCheckStatus Status,
    string Detail,
    long DurationMs = 0,
    string? Model = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PrivacyExclusionRequest(string Pattern, string? Reason = null);

public sealed record NeverSendRequest(
    string? AppName = null,
    string? ProcessName = null,
    string? Domain = null,
    string? Uri = null,
    string? Reason = null);

public sealed record PrivacyStatusResponse(
    bool AuthRequired,
    int RetentionDays,
    bool LocalOnlyMode,
    IReadOnlySet<string> CorsAllowedOrigins,
    int ActivePrivacyRuleCount);

public sealed record ScreenshotVisionPolicyRequest(string AppKey, string Policy);

public sealed record ScreenshotVisionPolicyEntry(string AppKey, string Policy);

public sealed record TranscribeAudioRequest(
    string AudioFilePath,
    string? Provider = null,
    string? Language = null,
    bool Translate = false);
