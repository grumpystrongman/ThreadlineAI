using Threadline.Core;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Service;

public sealed class ThreadlineDoctorService
{
    private readonly ThreadlineServiceOptions _options;
    private readonly SqliteThreadlineStore _sqliteStore;
    private readonly ProviderConnectionService _providers;
    private readonly ISessionRepository _sessions;
    private readonly IWorkThreadRepository _workThreads;
    private readonly IAdapterRegistry _adapters;
    private readonly IAuditRepository _audit;
    private readonly CapabilityRegistry _capabilities;
    private readonly ThreadlineActionCatalog _actions;
    private readonly IClock _clock;

    public ThreadlineDoctorService(
        ThreadlineServiceOptions options,
        SqliteThreadlineStore sqliteStore,
        ProviderConnectionService providers,
        ISessionRepository sessions,
        IWorkThreadRepository workThreads,
        IAdapterRegistry adapters,
        IAuditRepository audit,
        CapabilityRegistry capabilities,
        ThreadlineActionCatalog actions,
        IClock clock)
    {
        _options = options;
        _sqliteStore = sqliteStore;
        _providers = providers;
        _sessions = sessions;
        _workThreads = workThreads;
        _adapters = adapters;
        _audit = audit;
        _capabilities = capabilities;
        _actions = actions;
        _clock = clock;
    }

    public async Task<ThreadlineDoctorReport> BuildReportAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<ThreadlineDoctorCheck>
        {
            ThreadlineDoctorCheck.Pass(
                "service.running",
                "Service running",
                "Threadline.Service accepted the Doctor request.",
                new Dictionary<string, string>
                {
                    ["authRequired"] = _options.RequireApiToken.ToString(),
                    ["maxContextCharacters"] = _options.MaxContextCharacters.ToString()
                })
        };

        checks.Add(await CheckSqliteWritableAsync(cancellationToken));

        var providers = await _providers.ListAsync(cancellationToken);
        var readyProviders = providers.Where(p => p.Status == ProviderConnectionStatus.Ready).ToArray();
        var activeSession = await _sessions.GetActiveSessionAsync(cancellationToken);
        var activeWorkThread = await _workThreads.GetActiveWorkThreadAsync(cancellationToken);
        var adapters = await _adapters.ListAsync(cancellationToken);
        var browserAdapters = adapters.Where(a => a.Kind == AdapterKind.BrowserExtension).ToArray();
        var auditEvents = await _audit.GetRecentAuditEventsAsync(null, 50, cancellationToken);
        var lastProviderError = auditEvents.LastOrDefault(e => e.EventType == AuditEventType.ProviderCallFailed);
        var recentEvents = activeSession is null
            ? Array.Empty<ContextEvent>()
            : (await _sessions.GetRecentEventsAsync(activeSession.Id, 5, cancellationToken)).ToArray();
        var currentEvent = recentEvents.LastOrDefault();

        checks.Add(CheckProviderConfigured(providers, readyProviders));
        checks.Add(CheckProviderTest(lastProviderError));
        checks.Add(CheckActiveSession(activeSession));
        checks.Add(CheckActiveWorkThread(activeWorkThread));
        checks.Add(CheckBrowserExtension(browserAdapters));
        checks.Add(CheckCurrentContextSource(currentEvent));
        checks.Add(CheckLastProviderError(lastProviderError));
        checks.Add(CheckSidecarGeometryState());

        var capabilities = BuildCapabilities(providers, readyProviders, activeSession, activeWorkThread, browserAdapters, currentEvent).ToArray();
        var readiness = DetermineReadiness(checks);
        return new ThreadlineDoctorReport(readiness, _clock.UtcNow, checks, capabilities, _actions.List());
    }

    private async Task<ThreadlineDoctorCheck> CheckSqliteWritableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sqliteStore.ProbeWritableAsync(cancellationToken);
            return ThreadlineDoctorCheck.Pass("sqlite.writable", "SQLite writable", "SQLite opened and accepted a temporary write probe.");
        }
        catch (Exception ex)
        {
            return ThreadlineDoctorCheck.Fail(
                "sqlite.writable",
                "SQLite writable",
                $"SQLite write probe failed: {ex.Message}",
                "Check the Threadline database path, local app data permissions, disk space, and whether another process has locked the database.");
        }
    }

    private static ThreadlineDoctorCheck CheckProviderConfigured(IReadOnlyList<ProviderConnection> providers, IReadOnlyList<ProviderConnection> readyProviders)
    {
        if (readyProviders.Count > 0)
        {
            return ThreadlineDoctorCheck.Pass(
                "provider.configured",
                "Provider configured",
                $"Ready provider(s): {string.Join(", ", readyProviders.Select(p => p.ProviderName))}.");
        }

        if (providers.Count > 0)
        {
            return ThreadlineDoctorCheck.Warning(
                "provider.configured",
                "Provider configured",
                $"Provider record(s) exist but none are Ready: {string.Join(", ", providers.Select(p => $"{p.ProviderName}={p.Status}"))}.",
                "Open Settings, confirm base URL/model/credential, save the provider, then run Provider test.");
        }

        return ThreadlineDoctorCheck.Fail(
            "provider.configured",
            "Provider configured",
            "No provider has been configured.",
            "Open Settings, choose a provider, save the provider, then start or resume a session with that provider.");
    }

    private static ThreadlineDoctorCheck CheckProviderTest(AuditEvent? lastProviderError)
    {
        if (lastProviderError is null)
        {
            return ThreadlineDoctorCheck.Unknown(
                "provider.test",
                "Provider test",
                "No provider failure has been recorded recently. Run Provider test for an explicit pass/fail result.",
                "Use POST /providers/{providerName}/test or the Windows Tools panel Provider test action.");
        }

        return ThreadlineDoctorCheck.Warning(
            "provider.test",
            "Provider test",
            $"Last provider failure: {lastProviderError.Message}",
            "Run Provider test after correcting provider settings.",
            lastProviderError.Metadata);
    }

    private static ThreadlineDoctorCheck CheckActiveSession(ThreadlineSession? activeSession) =>
        activeSession is null
            ? ThreadlineDoctorCheck.Warning("session.active", "Active session", "No active Threadline session exists.", "Start a session or click Use Session in the sidecar.")
            : ThreadlineDoctorCheck.Pass("session.active", "Active session", $"Active session: {activeSession.Name} ({activeSession.Id}).", new Dictionary<string, string>
            {
                ["sessionId"] = activeSession.Id,
                ["provider"] = activeSession.ActiveProvider ?? "None"
            });

    private static ThreadlineDoctorCheck CheckActiveWorkThread(WorkThread? activeWorkThread) =>
        activeWorkThread is null
            ? ThreadlineDoctorCheck.Warning("work-thread.active", "Active Work Thread", "No active Work Thread exists.", "Click Resume or New Thread so memory-backed actions have a durable work target.")
            : ThreadlineDoctorCheck.Pass("work-thread.active", "Active Work Thread", $"Active Work Thread: {activeWorkThread.Title} ({activeWorkThread.Id}).", new Dictionary<string, string>
            {
                ["workThreadId"] = activeWorkThread.Id,
                ["status"] = activeWorkThread.Status.ToString()
            });

    private static ThreadlineDoctorCheck CheckBrowserExtension(IReadOnlyList<AdapterRegistration> browserAdapters)
    {
        if (browserAdapters.Count == 0)
        {
            return ThreadlineDoctorCheck.Warning(
                "browser-extension.reachable",
                "Browser extension reachable",
                "No browser-extension adapter is registered with the local service.",
                "Install or reload the Chrome/Edge extension, then use its Threadline capture action so page-level context can reach the service.");
        }

        var latest = browserAdapters.OrderByDescending(a => a.LastSeenAt ?? a.RegisteredAt).First();
        return ThreadlineDoctorCheck.Pass(
            "browser-extension.reachable",
            "Browser extension reachable",
            $"Browser extension registered: {latest.DisplayName}.",
            new Dictionary<string, string>
            {
                ["adapterId"] = latest.Id,
                ["lastSeenAt"] = (latest.LastSeenAt ?? latest.RegisteredAt).ToString("O")
            });
    }

    private static ThreadlineDoctorCheck CheckCurrentContextSource(ContextEvent? currentEvent)
    {
        if (currentEvent is null)
        {
            return ThreadlineDoctorCheck.Warning(
                "context.current-source",
                "Current context source",
                "No stored context event exists for the active session yet.",
                "Use Follow/Lock, Store Window, Native UI preview, or the browser extension to create current context before asking.");
        }

        var metadata = new Dictionary<string, string>
        {
            ["eventId"] = currentEvent.Id,
            ["source"] = currentEvent.Source.ToString(),
            ["contextType"] = currentEvent.ContextType
        };

        if (currentEvent.Source == ContextSource.Browser)
        {
            return ThreadlineDoctorCheck.Pass(
                "context.current-source",
                "Current context source",
                $"Current context is browser-provided page context: {currentEvent.ContextType}.",
                metadata: metadata);
        }

        if (currentEvent.Source == ContextSource.ActiveWindow && LooksLikeBrowser(currentEvent))
        {
            return ThreadlineDoctorCheck.Warning(
                "context.current-source",
                "Current context source",
                "Current context appears to be browser title/window metadata only, not extension page text.",
                "Use the browser extension to capture page title, URL, selection, and page text when the answer needs deeper browser context.",
                metadata);
        }

        return ThreadlineDoctorCheck.Pass(
            "context.current-source",
            "Current context source",
            $"Current context source: {currentEvent.Source}/{currentEvent.ContextType}.",
            metadata: metadata);
    }

    private static ThreadlineDoctorCheck CheckLastProviderError(AuditEvent? lastProviderError) =>
        lastProviderError is null
            ? ThreadlineDoctorCheck.Pass("provider.last-error", "Last provider error", "No recent provider-call failure was found in the audit log.")
            : ThreadlineDoctorCheck.Warning("provider.last-error", "Last provider error", lastProviderError.Message, "Open Settings, run Provider test, and correct the provider issue before relying on Ask.", lastProviderError.Metadata);

    private static ThreadlineDoctorCheck CheckSidecarGeometryState() =>
        ThreadlineDoctorCheck.Unknown(
            "sidecar.geometry-state",
            "Sidecar geometry state",
            "Geometry is owned by the Windows sidecar process and is not persisted in the local service yet.",
            "The Windows sidecar should save/restore geometry locally and keep geometry failures non-fatal.");

    private IEnumerable<ThreadlineCapability> BuildCapabilities(
        IReadOnlyList<ProviderConnection> providers,
        IReadOnlyList<ProviderConnection> readyProviders,
        ThreadlineSession? activeSession,
        WorkThread? activeWorkThread,
        IReadOnlyList<AdapterRegistration> browserAdapters,
        ContextEvent? currentEvent)
    {
        var baseCapabilities = _capabilities.List()
            .Where(c => !string.Equals(c.Id, "provider.configured", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.Id, "browser-extension.bridge", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.Id, "memory.work-thread", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.Id, "context.active-window", StringComparison.OrdinalIgnoreCase));

        foreach (var capability in baseCapabilities)
        {
            yield return capability;
        }

        yield return new ThreadlineCapability(
            "provider.configured",
            "ProviderCapability",
            "Configured Provider",
            readyProviders.Count > 0 ? ThreadlineCapabilityStatus.Ready : providers.Count > 0 ? ThreadlineCapabilityStatus.Degraded : ThreadlineCapabilityStatus.NeedsSetup,
            readyProviders.Count > 0 ? "At least one provider is Ready." : "Provider setup is incomplete.",
            new Dictionary<string, string> { ["providerCount"] = providers.Count.ToString(), ["readyProviderCount"] = readyProviders.Count.ToString() });

        foreach (var provider in providers)
        {
            yield return new ProviderCapability(
                provider.ProviderName,
                provider.Status == ProviderConnectionStatus.Ready ? ThreadlineCapabilityStatus.Ready : ThreadlineCapabilityStatus.NeedsSetup,
                $"Provider status: {provider.Status}.",
                new Dictionary<string, string>
                {
                    ["status"] = provider.Status.ToString(),
                    ["authType"] = provider.AuthType.ToString(),
                    ["model"] = provider.DefaultModel ?? "None"
                }).ToCapability();
        }

        yield return new ContextCapability(
            "Active Window",
            currentEvent is null ? ThreadlineCapabilityStatus.Degraded : ThreadlineCapabilityStatus.Ready,
            currentEvent is null ? "No current session context has been stored yet." : $"Current source: {currentEvent.Source}/{currentEvent.ContextType}.",
            new Dictionary<string, string> { ["activeSession"] = (activeSession is not null).ToString() }).ToCapability();

        yield return new MemoryCapability(
            "Work Thread",
            activeWorkThread is null ? ThreadlineCapabilityStatus.Degraded : ThreadlineCapabilityStatus.Ready,
            activeWorkThread is null ? "Work Thread memory is available but no active Work Thread is selected." : $"Active Work Thread: {activeWorkThread.Title}.",
            new Dictionary<string, string> { ["activeWorkThread"] = (activeWorkThread is not null).ToString() }).ToCapability();

        yield return new BrowserExtensionCapability(
            browserAdapters.Count > 0 ? ThreadlineCapabilityStatus.Ready : ThreadlineCapabilityStatus.NeedsSetup,
            browserAdapters.Count > 0 ? "Browser extension adapter is registered." : "Browser extension is not registered with the local service yet.",
            new Dictionary<string, string> { ["adapterCount"] = browserAdapters.Count.ToString() }).ToCapability();
    }

    private static ThreadlineReadinessState DetermineReadiness(IReadOnlyList<ThreadlineDoctorCheck> checks)
    {
        if (checks.Any(c => c.Status == ThreadlineDoctorCheckStatus.Fail && c.Id is "sqlite.writable" or "service.running"))
        {
            return ThreadlineReadinessState.Degraded;
        }

        if (checks.Any(c => c.Id == "provider.configured" && c.Status == ThreadlineDoctorCheckStatus.Fail))
        {
            return ThreadlineReadinessState.NeedsSetup;
        }

        if (checks.Any(c => c.Status is ThreadlineDoctorCheckStatus.Fail or ThreadlineDoctorCheckStatus.Warning))
        {
            return ThreadlineReadinessState.Degraded;
        }

        return ThreadlineReadinessState.Ready;
    }

    private static bool LooksLikeBrowser(ContextEvent currentEvent)
    {
        static bool Contains(string? value, string pattern) => !string.IsNullOrWhiteSpace(value) && value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        return Contains(currentEvent.ProcessName, "chrome")
            || Contains(currentEvent.ProcessName, "msedge")
            || Contains(currentEvent.ProcessName, "firefox")
            || Contains(currentEvent.ApplicationName, "chrome")
            || Contains(currentEvent.ApplicationName, "edge")
            || Contains(currentEvent.ApplicationName, "firefox");
    }
}
