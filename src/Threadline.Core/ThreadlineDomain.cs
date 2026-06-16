namespace Threadline.Core;

public enum CaptureMode
{
    Paused,
    AskOnly,
    CurrentWindow,
    CurrentSession,
    FullApprovedContext
}

public enum ProviderAuthType
{
    ApiKey,
    OAuth,
    LocalEndpoint,
    ManagedIdentity,
    None
}

public enum ProviderConnectionStatus
{
    Disabled,
    NeedsConfiguration,
    Ready,
    Failed
}

public sealed record ProviderConnection(
    string Id,
    string ProviderName,
    ProviderAuthType AuthType,
    string? CredentialReference,
    string? BaseUrl,
    string? DefaultModel,
    ProviderConnectionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ProviderConnection Create(
        string providerName,
        ProviderAuthType authType,
        DateTimeOffset now,
        string? credentialReference = null,
        string? baseUrl = null,
        string? defaultModel = null,
        ProviderConnectionStatus status = ProviderConnectionStatus.NeedsConfiguration,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"prv_{Guid.NewGuid():N}", providerName.Trim(), authType, credentialReference, baseUrl, defaultModel, status, now, null, metadata);

    public ProviderConnection MarkReady(DateTimeOffset now) => this with { Status = ProviderConnectionStatus.Ready, UpdatedAt = now };
    public ProviderConnection Disable(DateTimeOffset now) => this with { Status = ProviderConnectionStatus.Disabled, UpdatedAt = now };
}

public enum ArtifactType
{
    Url,
    File,
    Command,
    Screenshot,
    Selection,
    Note
}

public sealed record SessionArtifact(
    string Id,
    string SessionId,
    ArtifactType ArtifactType,
    string Title,
    string Reference,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static SessionArtifact Create(string sessionId, ArtifactType artifactType, string title, string reference, DateTimeOffset now, IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"art_{Guid.NewGuid():N}", sessionId, artifactType, title.Trim(), reference.Trim(), now, metadata);
}

public enum AdapterKind
{
    Unknown,
    WindowsShell,
    BrowserExtension,
    PowerShell,
    Terminal,
    UiAutomation,
    DevelopmentTool
}

[Flags]
public enum AdapterPermission
{
    None = 0,
    ReadSessions = 1,
    WriteContext = 2,
    ComposePrompts = 4,
    ManageProviders = 8,
    ReadAudit = 16,
    RegisterAdapters = 32,
    All = ReadSessions | WriteContext | ComposePrompts | ManageProviders | ReadAudit | RegisterAdapters
}

public sealed record AdapterRegistration(
    string Id,
    AdapterKind Kind,
    string DisplayName,
    AdapterPermission Permissions,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt = null,
    string? Version = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static AdapterRegistration Create(
        AdapterKind kind,
        string displayName,
        AdapterPermission permissions,
        DateTimeOffset now,
        string? version = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"adp_{Guid.NewGuid():N}", kind, displayName.Trim(), permissions, now, now, version, metadata);

    public AdapterRegistration Seen(DateTimeOffset now) => this with { LastSeenAt = now };
}

public enum AuditEventType
{
    SessionStarted,
    SessionPaused,
    SessionResumed,
    SessionEnded,
    ContextPreviewed,
    ContextStored,
    ContextBlocked,
    ProviderConfigured,
    ProviderCallStarted,
    ProviderCallCompleted,
    ProviderCallFailed,
    AdapterRegistered,
    AdapterHeartbeat
}

public sealed record AuditEvent(
    string Id,
    string? SessionId,
    AuditEventType EventType,
    DateTimeOffset Timestamp,
    string Message,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static AuditEvent Create(string? sessionId, AuditEventType eventType, DateTimeOffset timestamp, string message, IReadOnlyDictionary<string, string>? metadata = null) =>
        new($"aud_{Guid.NewGuid():N}", sessionId, eventType, timestamp, message, metadata);
}

public sealed record ContextPreview(
    ContextEvent OriginalEvent,
    CaptureDecision Decision,
    string RedactedContent,
    bool WillBeStored,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> Warnings)
{
    public ContextEvent ToApprovedEvent() => OriginalEvent with
    {
        Content = RedactedContent,
        UserApproved = Decision.IsAllowed && !RequiresExplicitApproval
    };
}

public sealed class ContextPreviewBuilder
{
    private readonly CapturePolicy _capturePolicy;
    private readonly SecretRedactor _redactor;

    public ContextPreviewBuilder(CapturePolicy capturePolicy, SecretRedactor redactor)
    {
        _capturePolicy = capturePolicy;
        _redactor = redactor;
    }

    public ContextPreview Build(ContextEvent contextEvent)
    {
        var decision = _capturePolicy.Evaluate(contextEvent);
        var redactedContent = _redactor.Redact(contextEvent.Content);
        var warnings = new List<string>();

        if (!decision.IsAllowed)
        {
            warnings.Add(decision.Reason);
        }

        if (!string.Equals(contextEvent.Content, redactedContent, StringComparison.Ordinal))
        {
            warnings.Add("Potential secrets or sensitive identifiers were redacted.");
        }

        if (decision.RequiresExplicitApproval)
        {
            warnings.Add("This context requires explicit user approval before storage or provider use.");
        }

        return new ContextPreview(
            contextEvent,
            decision,
            redactedContent,
            decision.IsAllowed && !decision.RequiresExplicitApproval,
            decision.RequiresExplicitApproval,
            warnings);
    }
}

public interface IProviderConnectionRepository
{
    Task SaveProviderConnectionAsync(ProviderConnection connection, CancellationToken cancellationToken = default);
    Task<ProviderConnection?> GetProviderConnectionAsync(string providerName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderConnection>> ListProviderConnectionsAsync(CancellationToken cancellationToken = default);
}

public interface IAdapterRegistry
{
    Task<AdapterRegistration> RegisterAsync(AdapterRegistration registration, CancellationToken cancellationToken = default);
    Task<AdapterRegistration?> GetAsync(string adapterId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdapterRegistration>> ListAsync(CancellationToken cancellationToken = default);
    Task<AdapterRegistration?> MarkSeenAsync(string adapterId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IAuditRepository
{
    Task AppendAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvent>> GetRecentAuditEventsAsync(string? sessionId, int take, CancellationToken cancellationToken = default);
}
