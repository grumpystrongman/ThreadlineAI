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

public sealed record ComposePromptRequest(string Question, string? CurrentWindow = null, int? TakeRecentEvents = 20);

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

public sealed record RegisterAdapterRequest(
    AdapterKind Kind,
    string DisplayName,
    AdapterPermission Permissions,
    string? Version = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
