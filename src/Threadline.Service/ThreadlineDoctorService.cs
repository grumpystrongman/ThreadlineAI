using Threadline.Core;

namespace Threadline.Service;

public sealed class ThreadlineDoctorService
{
    private const string ExpectedBrowserExtensionVersion = "17.0.0";
    private static readonly TimeSpan BrowserHeartbeatFreshnessWindow = TimeSpan.FromMinutes(10);

    private readonly ThreadlineServiceOptions _options;
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
        var lastProviderTest = auditEvents.LastOrDefault(IsProviderTestAudit);
        var lastProviderError = auditEvents.LastOrDefault(e => e.EventType == AuditEventType.ProviderCallFailed);
        var recentEvents = activeSession is null
            ? Array.Empty<ContextEvent>()
            : (await _sessions.GetRecentEventsAsync(activeSession.Id, 5, cancellationToken)).ToArray();
        var currentEvent = recentEvents.LastOrDefault();

        checks.Add(CheckProviderConfigured(providers, readyProviders));
        checks.Add(CheckProviderTest(lastProviderTest));
        checks.Add(CheckActiveSession(activeSession));
        checks.Add(CheckActiveWorkThread(activeWorkThread));
        checks.Add(CheckBrowserExtension(browserAdapters, _clock.UtcNow));
        checks.Add(CheckBrowserExtensionCompatibility(browserAdapters));
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
            await _audit.AppendAuditEventAsync(
                AuditEvent.Create(
                    null,
                    AuditEventType.AdapterHeartbeat,
                    _clock.UtcNow,
                    "Threadline Doctor SQLite write probe.",
                    new Dictionary<string, string> { ["source"] = "ThreadlineDoctor" }),
                cancellationToken);
            return ThreadlineDoctorCheck.Pass("sqlite.writable", "SQLite writable", "SQLite accepted the Doctor audit write probe.");
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

    private static ThreadlineDoctorCheck CheckProviderTest(AuditEvent? lastProviderTest)
    {
        if (lastProviderTest is null)
        {
            return ThreadlineDoctorCheck.Unknown(
                "provider.test",
                "Provider test",
                "Provider settings have not been tested in this service run.",
                "Use POST /providers/{providerName}/test or the Windows Tools panel Provider test action.");
        }

        var provider = GetMetadataValue(lastProviderTest, "provider") ?? "provider";
        var detail = GetMetadataValue(lastProviderTest, "detail") ?? lastProviderTest.Message;
        var status = GetMetadataValue(lastProviderTest, "status") ?? lastProviderTest.EventType.ToString();
        var metadata = lastProviderTest.Metadata;

        if (lastProviderTest.EventType == AuditEventType.ProviderCallCompleted)
        {
            return ThreadlineDoctorCheck.Pass(
                "provider.test",
                "Provider test",
                $"Last provider test passed for {provider}: {detail}",
                metadata);
        }

        return ThreadlineDoctorCheck.Fail(
            "provider.test",
            "Provider test",
            $"Last provider test failed for {provider}: {detail} Status: {status}.",
            "Correct provider settings and run Provider test again.",
            metadata);
    }

    private static ThreadlineDoctorCheck CheckActiveSession(ThreadlineSession? activeSession) =>
        activeSession is null
            ? ThreadlineDoctorCheck.Warning("session.active", "Active session", "No active Threadline session exists.", "Start a session or click Use Session in the sidecar. The Windows shell can bootstrap one when none exists.")
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

    private static ThreadlineDoctorCheck CheckBrowserExtension(IReadOnlyList<AdapterRegistration> browserAdapters, DateTimeOffset now)
    {
        if (browserAdapters.Count == 0)
        {
            return ThreadlineDoctorCheck.Warning(
                "browser-extension.reachable",
                "Browser extension reachable",
                "No browser-extension adapter is registered with the local service.",
                "Install or reload the Chrome/Edge extension, then open its popup and click Setup / register extension so page-level context can reach the service.");
        }

        var latest = GetLatestBrowserAdapter(browserAdapters)!;
        var lastSeen = latest.LastSeenAt ?? latest.RegisteredAt;
        var age = now - lastSeen;
        var metadata = BuildBrowserAdapterMetadata(latest, now);

        if (age > BrowserHeartbeatFreshnessWindow)
        {
            return ThreadlineDoctorCheck.Warning(
                "browser-extension.reachable",
                "Browser extension reachable",
                $"Browser extension is registered but its heartbeat is stale. Last seen {lastSeen:O}.",
                "Open Chrome/Edge, reload the ThreadlineAI extension, then click Send heartbeat now in the extension popup.",
                metadata);
        }

        return ThreadlineDoctorCheck.Pass(
            "browser-extension.reachable",
            "Browser extension reachable",
            $"Browser extension heartbeat received from {latest.DisplayName}; last seen {lastSeen:O}.",
            metadata);
    }

    private static ThreadlineDoctorCheck CheckBrowserExtensionCompatibility(IReadOnlyList<AdapterRegistration> browserAdapters)
    {
        if (browserAdapters.Count == 0)
        {
            return ThreadlineDoctorCheck.Unknown(
                "browser-extension.compatibility",
                "Browser extension compatibility",
                "No browser-extension adapter has registered a version yet.",
                "Install the Build 17 extension and register it from the popup.");
        }

        var latest = GetLatestBrowserAdapter(browserAdapters)!;
        var version = GetAdapterVersion(latest);
        var metadata = BuildBrowserAdapterMetadata(latest, DateTimeOffset.UtcNow);
        metadata["expectedVersion"] = ExpectedBrowserExtensionVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return ThreadlineDoctorCheck.Warning(
                "browser-extension.compatibility",
                "Browser extension compatibility",
                "The browser extension registered without a version.",
                $"Reload the Build 17 extension package and register again. Expected version {ExpectedBrowserExtensionVersion}.",
                metadata);
        }

        if (!string.Equals(version, ExpectedBrowserExtensionVersion, StringComparison.OrdinalIgnoreCase))
        {
            return ThreadlineDoctorCheck.Warning(
                "browser-extension.compatibility",
                "Browser extension compatibility",
                $"Browser extension version {version} does not match service expectation {ExpectedBrowserExtensionVersion}.",
                "Rebuild/reload adapters/browser-extension and click Setup / register extension again.",
                metadata);
        }

        return ThreadlineDoctorCheck.Pass(
            "browser-extension.compatibility",
            "Browser extension compatibility",
            $"Browser extension version {version} is compatible with Build 17.",
            metadata);
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
            if (currentEvent.Metadata is not null)
            {
                foreach (var item in currentEvent.Metadata)
                {
                    metadata[item.Key] = item.Value;
                }
            }

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
                "Current context appears to be Chrome/Edge title-only window metadata, not full extension page text.",
                "Use the browser extension Send page or Send selection action when the answer needs page title, URL, selected text, visible text, article/main text, and DOM extraction metadata.",
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
            "Geometry is owned by the Windows sidecar process and guarded by non-fatal placement fallbacks.",
            "If geometry becomes unstable, reset local context or restart the Windows sidecar; placement failures should not crash the app.");

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
                     && !string.Equals(c.Id, "provider.configured-provider", StringComparison.OrdinalIgnoreCase)
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

        var latestBrowserAdapter = GetLatestBrowserAdapter(browserAdapters);
        var browserStatus = latestBrowserAdapter is null
            ? ThreadlineCapabilityStatus.NeedsSetup
            : IsHeartbeatFresh(latestBrowserAdapter, _clock.UtcNow) && IsBrowserExtensionCompatible(latestBrowserAdapter)
                ? ThreadlineCapabilityStatus.Ready
                : ThreadlineCapabilityStatus.Degraded;

        var browserDescription = latestBrowserAdapter is null
            ? "Browser extension is not registered with the local service yet."
            : browserStatus == ThreadlineCapabilityStatus.Ready
                ? "Browser extension heartbeat and Build 17 version are compatible."
                : "Browser extension is registered but needs a fresh heartbeat or compatible Build 17 version.";

        yield return new BrowserExtensionCapability(
            browserStatus,
            browserDescription,
            latestBrowserAdapter is null
                ? new Dictionary<string, string> { ["adapterCount"] = browserAdapters.Count.ToString(), ["expectedVersion"] = ExpectedBrowserExtensionVersion }
                : BuildBrowserAdapterMetadata(latestBrowserAdapter, _clock.UtcNow)).ToCapability();
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

    private static bool IsProviderTestAudit(AuditEvent auditEvent) =>
        string.Equals(GetMetadataValue(auditEvent, "source"), "ThreadlineProviderTest", StringComparison.OrdinalIgnoreCase);

    private static string? GetMetadataValue(AuditEvent auditEvent, string key) =>
        auditEvent.Metadata is not null && auditEvent.Metadata.TryGetValue(key, out var value) ? value : null;

    private static AdapterRegistration? GetLatestBrowserAdapter(IReadOnlyList<AdapterRegistration> browserAdapters) =>
        browserAdapters.OrderByDescending(a => a.LastSeenAt ?? a.RegisteredAt).FirstOrDefault();

    private static bool IsBrowserExtensionCompatible(AdapterRegistration adapter) =>
        string.Equals(GetAdapterVersion(adapter), ExpectedBrowserExtensionVersion, StringComparison.OrdinalIgnoreCase);

    private static string? GetAdapterVersion(AdapterRegistration adapter)
    {
        if (!string.IsNullOrWhiteSpace(adapter.Version)) return adapter.Version;
        if (adapter.Metadata is not null && adapter.Metadata.TryGetValue("extensionVersion", out var version) && !string.IsNullOrWhiteSpace(version)) return version;
        if (adapter.Metadata is not null && adapter.Metadata.TryGetValue("heartbeatVersion", out var heartbeatVersion) && !string.IsNullOrWhiteSpace(heartbeatVersion)) return heartbeatVersion;
        return null;
    }

    private static bool IsHeartbeatFresh(AdapterRegistration adapter, DateTimeOffset now) =>
        now - (adapter.LastSeenAt ?? adapter.RegisteredAt) <= BrowserHeartbeatFreshnessWindow;

    private static Dictionary<string, string> BuildBrowserAdapterMetadata(AdapterRegistration adapter, DateTimeOffset now)
    {
        var lastSeenAt = adapter.LastSeenAt ?? adapter.RegisteredAt;
        var metadata = new Dictionary<string, string>
        {
            ["adapterId"] = adapter.Id,
            ["adapterCountedKind"] = adapter.Kind.ToString(),
            ["displayName"] = adapter.DisplayName,
            ["registeredAt"] = adapter.RegisteredAt.ToString("O"),
            ["lastSeenAt"] = lastSeenAt.ToString("O"),
            ["heartbeatAgeSeconds"] = Math.Max(0, (int)(now - lastSeenAt).TotalSeconds).ToString(),
            ["version"] = GetAdapterVersion(adapter) ?? "unknown",
            ["expectedVersion"] = ExpectedBrowserExtensionVersion
        };

        if (adapter.Metadata is not null)
        {
            foreach (var item in adapter.Metadata)
            {
                metadata[item.Key] = item.Value;
            }
        }

        return metadata;
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
