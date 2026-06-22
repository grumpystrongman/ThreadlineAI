namespace Threadline.Core;

public enum ThreadlineReadinessState
{
    Ready,
    NeedsSetup,
    Degraded
}

public enum ThreadlineCapabilityStatus
{
    Ready,
    NeedsSetup,
    Degraded,
    Unavailable
}

public enum ThreadlineDoctorCheckStatus
{
    Pass,
    Warning,
    Fail,
    Unknown
}

public enum ThreadlineActionKind
{
    Summary,
    Handoff,
    Decisions,
    Risks,
    NextActions,
    ProviderTest,
    ResumeWork,
    ClearContext,
    Custom
}

public sealed record ThreadlineCapability(
    string Id,
    string Category,
    string DisplayName,
    ThreadlineCapabilityStatus Status,
    string Description,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ThreadlineCapability WithStatus(ThreadlineCapabilityStatus status, string? description = null, IReadOnlyDictionary<string, string>? metadata = null) =>
        this with
        {
            Status = status,
            Description = string.IsNullOrWhiteSpace(description) ? Description : description.Trim(),
            Metadata = metadata ?? Metadata
        };
}

public sealed record ProviderCapability(
    string ProviderName,
    ThreadlineCapabilityStatus Status,
    string Description,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ThreadlineCapability ToCapability() =>
        new($"provider.{Normalize(ProviderName)}", "ProviderCapability", ProviderName.Trim(), Status, Description.Trim(), Metadata);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant().Replace(' ', '-');
}

public sealed record ContextCapability(
    string ContextSource,
    ThreadlineCapabilityStatus Status,
    string Description,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ThreadlineCapability ToCapability() =>
        new($"context.{Normalize(ContextSource)}", "ContextCapability", ContextSource.Trim(), Status, Description.Trim(), Metadata);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant().Replace(' ', '-');
}

public sealed record MemoryCapability(
    string Name,
    ThreadlineCapabilityStatus Status,
    string Description,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ThreadlineCapability ToCapability() =>
        new($"memory.{Normalize(Name)}", "MemoryCapability", Name.Trim(), Status, Description.Trim(), Metadata);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant().Replace(' ', '-');
}

public sealed record ArtifactCapability(
    string Name,
    ThreadlineCapabilityStatus Status,
    string Description,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ThreadlineCapability ToCapability() =>
        new($"artifact.{Normalize(Name)}", "ArtifactCapability", Name.Trim(), Status, Description.Trim(), Metadata);

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant().Replace(' ', '-');
}

public sealed record BrowserExtensionCapability(
    ThreadlineCapabilityStatus Status,
    string Description,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public ThreadlineCapability ToCapability() =>
        new("browser-extension.bridge", "BrowserExtensionCapability", "Browser Extension Bridge", Status, Description.Trim(), Metadata);
}

public sealed record ThreadlineActionDefinition(
    string Id,
    ThreadlineActionKind Kind,
    string DisplayName,
    string Description,
    string? RequiredCapabilityId = null,
    bool RequiresActiveSession = false,
    bool RequiresActiveWorkThread = false);

public sealed record ThreadlineDoctorCheck(
    string Id,
    string DisplayName,
    ThreadlineDoctorCheckStatus Status,
    string Detail,
    string? Remediation = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ThreadlineDoctorCheck Pass(string id, string displayName, string detail, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, displayName, ThreadlineDoctorCheckStatus.Pass, detail, null, metadata);

    public static ThreadlineDoctorCheck Warning(string id, string displayName, string detail, string? remediation = null, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, displayName, ThreadlineDoctorCheckStatus.Warning, detail, remediation, metadata);

    public static ThreadlineDoctorCheck Fail(string id, string displayName, string detail, string? remediation = null, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, displayName, ThreadlineDoctorCheckStatus.Fail, detail, remediation, metadata);

    public static ThreadlineDoctorCheck Unknown(string id, string displayName, string detail, string? remediation = null, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, displayName, ThreadlineDoctorCheckStatus.Unknown, detail, remediation, metadata);
}

public sealed record ThreadlineDoctorReport(
    ThreadlineReadinessState Readiness,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ThreadlineDoctorCheck> Checks,
    IReadOnlyList<ThreadlineCapability> Capabilities,
    IReadOnlyList<ThreadlineActionDefinition> Actions)
{
    public bool IsReady => Readiness == ThreadlineReadinessState.Ready;
}

public sealed class CapabilityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ThreadlineCapability> _capabilities = new(StringComparer.OrdinalIgnoreCase);

    public CapabilityRegistry()
    {
        Register(new ProviderCapability("Configured Provider", ThreadlineCapabilityStatus.NeedsSetup, "No provider has been proven ready yet.").ToCapability());
        Register(new ContextCapability("Active Window", ThreadlineCapabilityStatus.Ready, "Threadline can use active-window metadata as a minimal context source.").ToCapability());
        Register(new MemoryCapability("Work Thread", ThreadlineCapabilityStatus.Ready, "Threadline can persist work-thread memory when the service storage is available.").ToCapability());
        Register(new ArtifactCapability("Work Artifact", ThreadlineCapabilityStatus.Ready, "Threadline can save summary, handoff, decision, risk, and next-action artifacts.").ToCapability());
        Register(new BrowserExtensionCapability(ThreadlineCapabilityStatus.NeedsSetup, "Install and register the Chrome or Edge extension for page-level browser context.").ToCapability());
    }

    public void Register(ThreadlineCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (string.IsNullOrWhiteSpace(capability.Id))
        {
            throw new ArgumentException("Capability id is required.", nameof(capability));
        }

        lock (_gate)
        {
            _capabilities[capability.Id] = capability;
        }
    }

    public ThreadlineCapability? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            return _capabilities.TryGetValue(id, out var capability) ? capability : null;
        }
    }

    public IReadOnlyList<ThreadlineCapability> List()
    {
        lock (_gate)
        {
            return _capabilities.Values.OrderBy(c => c.Category).ThenBy(c => c.DisplayName).ToArray();
        }
    }
}

public sealed class ThreadlineActionCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ThreadlineActionDefinition> _actions = new(StringComparer.OrdinalIgnoreCase);

    public ThreadlineActionCatalog()
    {
        Register(new ThreadlineActionDefinition("artifact.summary", ThreadlineActionKind.Summary, "Summary", "Create a concise summary artifact from the current conversation.", "artifact.work-artifact", RequiresActiveWorkThread: true));
        Register(new ThreadlineActionDefinition("artifact.handoff", ThreadlineActionKind.Handoff, "Handoff", "Create a handoff artifact that captures current state and continuation notes.", "artifact.work-artifact", RequiresActiveWorkThread: true));
        Register(new ThreadlineActionDefinition("artifact.decisions", ThreadlineActionKind.Decisions, "Decisions", "Create a decision-log artifact from the current conversation.", "artifact.work-artifact", RequiresActiveWorkThread: true));
        Register(new ThreadlineActionDefinition("artifact.risks", ThreadlineActionKind.Risks, "Risks", "Create a risks and watchouts artifact from the current conversation.", "artifact.work-artifact", RequiresActiveWorkThread: true));
        Register(new ThreadlineActionDefinition("artifact.next-actions", ThreadlineActionKind.NextActions, "Next actions", "Create a next-actions artifact from the current conversation.", "artifact.work-artifact", RequiresActiveWorkThread: true));
        Register(new ThreadlineActionDefinition("provider.test", ThreadlineActionKind.ProviderTest, "Provider test", "Validate the active provider configuration and execution path.", "provider.configured"));
        Register(new ThreadlineActionDefinition("work.resume", ThreadlineActionKind.ResumeWork, "Resume work", "Resume the active Work Thread or bootstrap one if needed.", "memory.work-thread"));
        Register(new ThreadlineActionDefinition("context.clear", ThreadlineActionKind.ClearContext, "Clear context", "Clear the sidecar's local shared context without deleting durable history."));
    }

    public void Register(ThreadlineActionDefinition action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Id))
        {
            throw new ArgumentException("Action id is required.", nameof(action));
        }

        lock (_gate)
        {
            _actions[action.Id] = action;
        }
    }

    public ThreadlineActionDefinition? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            return _actions.TryGetValue(id, out var action) ? action : null;
        }
    }

    public IReadOnlyList<ThreadlineActionDefinition> List()
    {
        lock (_gate)
        {
            return _actions.Values.OrderBy(a => a.Kind).ThenBy(a => a.DisplayName).ToArray();
        }
    }
}
