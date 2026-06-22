using Threadline.Core;
using Threadline.Infrastructure;

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
                     && !string.Equals(c.Id, "provider.configured-missing", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(c.Id, "context.current", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var capability in baseCapabilities)
        {
            yield return capability;
        }

        if (readyProviders.Count > 0)
        {
            yield return new ThreadlineCapability(
                "provider.configured",
                "Provider configured",
                $"Ready provider(s): {string.Join(", ", readyProviders.Select(p => p.ProviderName))}.",
                true,
                false,
                providers.Select(p => p.ProviderName).ToArray());
        }
        else
        {
            yield return new ThreadlineCapability(
                "provider.configured-missing",
                "Provider not ready",
                providers.Count == 0 ? "No provider settings have been saved." : "Provider settings exist, but no provider is Ready.",
                false,
                true,
                ["Settings", "ProviderTest"]);
        }

        yield return new ThreadlineCapability(
            "session.active",
            "Active session",
            activeSession is null ? "No active session exists." : $"Active session: {activeSession.Name}.",
            activeSession is not null,
            activeSession is null,
            ["SessionBootstrap"]);

        yield return new ThreadlineCapability(
            "work-thread.active",
            "Active Work Thread",
            activeWorkThread is null ? "No active Work Thread exists." : $"Active Work Thread: {activeWorkThread.Title}.",
            activeWorkThread is not null,
            activeWorkThread is null,
            ["Resume", "NewThread"]);

        var latestBrowser = GetLatestBrowserAdapter(browserAdapters);
        yield return new ThreadlineCapability(
            "browser-extension.bridge",
            "Browser extension bridge",
            latestBrowser is null ? "Browser extension is not registered." : $"Browser extension registered: {latestBrowser.DisplayName}.",
            latestBrowser is not null,
            latestBrowser is null,
            ["BrowserExtension", "ContextCapture"]);

        yield return new ThreadlineCapability(
            "context.current",
            "Current context",
            currentEvent is null ? "No approved current context has been stored for the active session." : $"Current context source: {currentEvent.Source}/{currentEvent.ContextType}.",
            currentEvent is not null,
            currentEvent is null,
            ["Follow", "Lock", "StoreWindow", "BrowserExtension"]);
    }

    private static IReadOnlyList<ThreadlineActionDescriptor> BuildRecommendedActions(ThreadlineDoctorCheckStatus readiness, IReadOnlyList<ThreadlineDoctorCheck> checks, ThreadlineActionCatalog actions)
    {
        var recommended = new List<ThreadlineActionDescriptor>();
        var actionList = actions.List();

        void Add(string actionId)
        {
            var action = actionList.FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));
            if (action is not null && recommended.All(r => !string.Equals(r.Id, action.Id, StringComparison.OrdinalIgnoreCase)))
            {
                recommended.Add(action);
            }
        }

        foreach (var check in checks.Where(c => c.Status is ThreadlineDoctorCheckStatus.Fail or ThreadlineDoctorCheckStatus.Warning))
        {
            switch (check.Id)
            {
                case "provider.configured":
                case "provider.test":
                case "provider.last-error":
                    Add("provider.test");
                    break;
                case "session.active":
                    Add("work.resume");
                    break;
                case "work-thread.active":
                    Add("work.resume");
                    Add("artifact.next-actions");
                    break;
                case "browser-extension.reachable":
                case "browser-extension.compatibility":
                    Add("adapter.browser-extension.reconnect");
                    break;
                case "context.current-source":
                    Add("context.refresh");
                    break;
            }
        }

        if (readiness == ThreadlineDoctorCheckStatus.Pass)
        {
            Add("provider.test");
            Add("artifact.summary");
            Add("artifact.next-actions");
        }

        return recommended;
    }

    private static ThreadlineDoctorCheckStatus DetermineReadiness(IReadOnlyList<ThreadlineDoctorCheck> checks)
    {
        if (checks.Any(c => c.Status == ThreadlineDoctorCheckStatus.Fail))
        {
            return ThreadlineDoctorCheckStatus.Fail;
        }

        if (checks.Any(c => c.Status == ThreadlineDoctorCheckStatus.Warning))
        {
            return ThreadlineDoctorCheckStatus.Warning;
        }

        if (checks.Any(c => c.Status == ThreadlineDoctorCheckStatus.Unknown))
        {
            return ThreadlineDoctorCheckStatus.Unknown;
        }

        return ThreadlineDoctorCheckStatus.Pass;
    }

    private static bool IsProviderTestAudit(AuditEvent auditEvent) =>
        auditEvent.EventType is AuditEventType.ProviderCallCompleted or AuditEventType.ProviderCallFailed
        && string.Equals(GetMetadataValue(auditEvent, "source"), "ProviderTest", StringComparison.OrdinalIgnoreCase);

    private static string? GetMetadataValue(AuditEvent auditEvent, string key)
    {
        if (auditEvent.Metadata is null)
        {
            return null;
        }

        return auditEvent.Metadata.TryGetValue(key, out var value) ? value : null;
    }

    private static AdapterRegistration? GetLatestBrowserAdapter(IReadOnlyList<AdapterRegistration> browserAdapters) =>
        browserAdapters.OrderByDescending(a => a.LastSeenAt ?? a.RegisteredAt).FirstOrDefault();

    private static string? GetAdapterVersion(AdapterRegistration adapter)
    {
        if (adapter.Metadata is null)
        {
            return null;
        }

        return adapter.Metadata.TryGetValue("extensionVersion", out var extensionVersion) ? extensionVersion :
            adapter.Metadata.TryGetValue("version", out var version) ? version : null;
    }

    private static Dictionary<string, string> BuildBrowserAdapterMetadata(AdapterRegistration adapter, DateTimeOffset now)
    {
        var lastSeen = adapter.LastSeenAt ?? adapter.RegisteredAt;
        var metadata = new Dictionary<string, string>
        {
            ["adapterId"] = adapter.Id,
            ["displayName"] = adapter.DisplayName,
            ["registeredAt"] = adapter.RegisteredAt.ToString("O"),
            ["lastSeenAt"] = lastSeen.ToString("O"),
            ["ageSeconds"] = Math.Max(0, (int)(now - lastSeen).TotalSeconds).ToString()
        };

        var version = GetAdapterVersion(adapter);
        if (!string.IsNullOrWhiteSpace(version))
        {
            metadata["extensionVersion"] = version;
        }

        return metadata;
    }

    private static bool LooksLikeBrowser(ContextEvent contextEvent)
    {
        if (contextEvent.Metadata is not null)
        {
            var process = contextEvent.Metadata.TryGetValue("processName", out var processName) ? processName : string.Empty;
            if (process.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
                process.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
                process.Contains("edge", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return contextEvent.Content.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            || contextEvent.Content.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase)
            || contextEvent.Content.Contains("msedge", StringComparison.OrdinalIgnoreCase);
    }
}
