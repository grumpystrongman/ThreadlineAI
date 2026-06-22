using System.Net;
using System.Text;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class Build23DoctorAndActionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-22T12:00:00Z");

    [Fact]
    public async Task Doctor_WhenBrowserExtensionIsUnreachable_WarnsAndMarksCapabilityNeedsSetup()
    {
        var doctor = CreateDoctor(new FixedClock(Now), adapters: new InMemoryAdapterRegistry());

        var report = await doctor.BuildReportAsync();

        var reachable = Assert.Single(report.Checks, check => check.Id == "browser-extension.reachable");
        var capability = Assert.Single(report.Capabilities, item => item.Id == "browser-extension.bridge");
        Assert.Equal(ThreadlineDoctorCheckStatus.Warning, reachable.Status);
        Assert.Contains("No browser-extension adapter", reachable.Detail);
        Assert.Equal(ThreadlineCapabilityStatus.NeedsSetup, capability.Status);
    }

    [Fact]
    public async Task Doctor_WhenBrowserExtensionHeartbeatIsFreshAndCompatible_PassesReachability()
    {
        var clock = new FixedClock(Now);
        var adapters = new InMemoryAdapterRegistry();
        await adapters.RegisterAsync(AdapterRegistration.Create(
            AdapterKind.BrowserExtension,
            "Threadline Chrome Extension",
            AdapterPermission.WriteContext,
            clock.UtcNow,
            version: "17.0.0",
            metadata: new Dictionary<string, string> { ["extensionVersion"] = "17.0.0" }));
        var doctor = CreateDoctor(clock, adapters: adapters);

        var report = await doctor.BuildReportAsync();

        var reachable = Assert.Single(report.Checks, check => check.Id == "browser-extension.reachable");
        var compatibility = Assert.Single(report.Checks, check => check.Id == "browser-extension.compatibility");
        var capability = Assert.Single(report.Capabilities, item => item.Id == "browser-extension.bridge");
        Assert.Equal(ThreadlineDoctorCheckStatus.Pass, reachable.Status);
        Assert.Equal(ThreadlineDoctorCheckStatus.Pass, compatibility.Status);
        Assert.Equal(ThreadlineCapabilityStatus.Ready, capability.Status);
    }

    [Fact]
    public async Task Doctor_WhenBrowserExtensionHeartbeatIsStale_WarnsReachability()
    {
        var clock = new FixedClock(Now);
        var adapters = new InMemoryAdapterRegistry();
        await adapters.RegisterAsync(AdapterRegistration.Create(
            AdapterKind.BrowserExtension,
            "Threadline Chrome Extension",
            AdapterPermission.WriteContext,
            clock.UtcNow.AddMinutes(-30),
            version: "17.0.0",
            metadata: new Dictionary<string, string> { ["extensionVersion"] = "17.0.0" }));
        var doctor = CreateDoctor(clock, adapters: adapters);

        var report = await doctor.BuildReportAsync();

        var reachable = Assert.Single(report.Checks, check => check.Id == "browser-extension.reachable");
        var capability = Assert.Single(report.Capabilities, item => item.Id == "browser-extension.bridge");
        Assert.Equal(ThreadlineDoctorCheckStatus.Warning, reachable.Status);
        Assert.Contains("heartbeat is stale", reachable.Detail);
        Assert.Equal(ThreadlineCapabilityStatus.Degraded, capability.Status);
    }

    [Fact]
    public async Task ProviderProbe_WhenProviderResponds_RecordsPassAudit()
    {
        var clock = new FixedClock(Now);
        var audit = new InMemoryAuditRepository();
        var providers = new InMemoryProviderConnectionRepository();
        await providers.SaveProviderConnectionAsync(new ProviderConnection(
            "prv_local",
            "Local Provider",
            ProviderAuthType.None,
            null,
            "https://provider.test/v1",
            "local-model",
            ProviderConnectionStatus.Ready,
            clock.UtcNow));
        var probe = CreateProviderProbe(clock, audit, providers, Json(HttpStatusCode.OK, """
            {
              "choices": [
                { "message": { "role": "assistant", "content": "OK" } }
              ]
            }
            """));

        var result = await probe.TestAsync("Local Provider");
        var events = await audit.GetRecentAuditEventsAsync(null, 10);

        Assert.True(result.Success);
        Assert.Equal(ThreadlineDoctorCheckStatus.Pass, result.Status);
        Assert.Contains(events, item => item.EventType == AuditEventType.ProviderCallCompleted && item.Metadata!["source"] == "ThreadlineProviderTest");
    }

    [Fact]
    public async Task ProviderProbe_WhenProviderFails_RecordsFailureAuditWithoutThrowing()
    {
        var clock = new FixedClock(Now);
        var audit = new InMemoryAuditRepository();
        var providers = new InMemoryProviderConnectionRepository();
        await providers.SaveProviderConnectionAsync(new ProviderConnection(
            "prv_local",
            "Local Provider",
            ProviderAuthType.None,
            null,
            "https://provider.test/v1",
            "local-model",
            ProviderConnectionStatus.Ready,
            clock.UtcNow));
        var probe = CreateProviderProbe(clock, audit, providers, Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));

        var result = await probe.TestAsync("Local Provider");
        var events = await audit.GetRecentAuditEventsAsync(null, 10);

        Assert.False(result.Success);
        Assert.Equal(ThreadlineDoctorCheckStatus.Fail, result.Status);
        Assert.Contains("Provider test failed", result.Detail);
        Assert.Contains(events, item => item.EventType == AuditEventType.ProviderCallFailed && item.Metadata!["source"] == "ThreadlineProviderTest");
    }

    [Fact]
    public async Task RegisteredResumeAction_BootstrapsWorkThreadWhenNoneExists()
    {
        var workThreads = new InMemoryWorkThreadRepository();
        var runner = CreateActionRunner(workThreads);

        var result = await runner.ExecuteAsync("work.resume");
        var active = await workThreads.GetActiveWorkThreadAsync();

        Assert.Equal("Succeeded", result.Status);
        Assert.NotNull(active);
        Assert.Equal(active.Id, result.WorkThreadId);
    }

    [Fact]
    public async Task ArtifactActions_CreateCopyExportAndRegenerateWithoutCrashing()
    {
        var workThreads = new InMemoryWorkThreadRepository();
        var artifactHistory = new InMemoryArtifactHistoryRepository();
        var runner = CreateActionRunner(workThreads, artifactHistory);

        var created = await runner.ExecuteAsync("artifact.summary", new ThreadlineActionRunRequest(
            Transcript: "Build 23 needs a manual QA checklist and smoke test script.",
            ContextSummary: "Release confidence work is underway."));
        var copied = await runner.ExecuteAsync("artifact.copy", new ThreadlineActionRunRequest(WorkThreadId: created.WorkThreadId, ArtifactId: created.Artifact!.Id));
        var exported = await runner.ExecuteAsync("artifact.export", new ThreadlineActionRunRequest(WorkThreadId: created.WorkThreadId, ArtifactId: created.Artifact.Id));
        var regenerated = await runner.ExecuteAsync("artifact.regenerate", new ThreadlineActionRunRequest(WorkThreadId: created.WorkThreadId, ArtifactId: created.Artifact.Id, Content: "Regenerate with Build 23 validation notes."));

        Assert.Equal("Succeeded", created.Status);
        Assert.Equal("Succeeded", copied.Status);
        Assert.Equal("Succeeded", exported.Status);
        Assert.Equal("Succeeded", regenerated.Status);
        Assert.Contains("content", copied.Metadata!.Keys);
        Assert.Equal("text/markdown", exported.Metadata!["contentType"]);
        Assert.Equal(2, artifactHistory.Versions.Count);
    }

    private static ThreadlineDoctorService CreateDoctor(FixedClock clock, InMemoryAdapterRegistry adapters)
    {
        var audit = new InMemoryAuditRepository();
        var providerRepository = new InMemoryProviderConnectionRepository();
        var providerService = new ProviderConnectionService(providerRepository, clock, audit);
        return new ThreadlineDoctorService(
            TestOptions(),
            providerService,
            new InMemorySessionRepository(),
            new InMemoryWorkThreadRepository(),
            adapters,
            audit,
            new CapabilityRegistry(),
            new ThreadlineActionCatalog(),
            clock);
    }

    private static ThreadlineProviderProbeService CreateProviderProbe(
        FixedClock clock,
        InMemoryAuditRepository audit,
        InMemoryProviderConnectionRepository providers,
        HttpResponseMessage response)
    {
        var providerService = new ProviderConnectionService(providers, clock, audit);
        var secrets = new SecretService(new InMemorySecretStore(), clock, audit);
        return new ThreadlineProviderProbeService(
            providerService,
            secrets,
            new StaticHttpClientFactory(new HttpClient(new StaticResponseHandler(response))),
            audit,
            clock);
    }

    private static ThreadlineActionExecutionService CreateActionRunner(
        InMemoryWorkThreadRepository workThreads,
        InMemoryArtifactHistoryRepository? artifactHistory = null) =>
        new(
            new ThreadlineActionCatalog(),
            workThreads,
            artifactHistory ?? new InMemoryArtifactHistoryRepository(),
            maintenance: null!,
            new FixedClock(Now));

    private static ThreadlineServiceOptions TestOptions() =>
        new(
            RequireApiToken: false,
            ApiToken: null,
            ApiTokenPath: Path.GetTempFileName(),
            MaxContextCharacters: 200_000,
            MaxSessionNameCharacters: 120,
            RetentionDays: 30,
            LocalOnlyMode: false,
            CorsAllowedOrigins: new HashSet<string>());

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string payload) =>
        new(statusCode)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryProviderConnectionRepository : IProviderConnectionRepository
    {
        private readonly Dictionary<string, ProviderConnection> _providers = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveProviderConnectionAsync(ProviderConnection connection, CancellationToken cancellationToken = default)
        {
            _providers[connection.ProviderName] = connection;
            return Task.CompletedTask;
        }

        public Task<ProviderConnection?> GetProviderConnectionAsync(string providerName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_providers.TryGetValue(providerName, out var provider) ? provider : null);

        public Task<IReadOnlyList<ProviderConnection>> ListProviderConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderConnection>>(_providers.Values.OrderBy(provider => provider.ProviderName).ToArray());
    }

    private sealed class InMemoryAuditRepository : IAuditRepository
    {
        private readonly List<AuditEvent> _events = [];

        public Task AppendAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            _events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> GetRecentAuditEventsAsync(string? sessionId, int take, CancellationToken cancellationToken = default)
        {
            var results = _events
                .Where(item => sessionId is null || item.SessionId == sessionId)
                .OrderBy(item => item.Timestamp)
                .TakeLast(Math.Max(1, take))
                .ToArray();
            return Task.FromResult<IReadOnlyList<AuditEvent>>(results);
        }
    }

    private sealed class InMemoryWorkThreadRepository : IWorkThreadRepository
    {
        private readonly Dictionary<string, WorkThread> _threads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ConversationMessage>> _messages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<WorkContextEvent>> _contexts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ContextReceiptRecord> _receipts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<WorkArtifact>> _artifacts = new(StringComparer.OrdinalIgnoreCase);

        public Task<WorkThread?> GetWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_threads.TryGetValue(workThreadId, out var thread) ? thread : null);

        public Task<WorkThread?> GetActiveWorkThreadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_threads.Values.Where(thread => thread.Status == WorkThreadStatus.Open).OrderByDescending(thread => thread.UpdatedAt).FirstOrDefault());

        public Task<IReadOnlyList<WorkThread>> ListWorkThreadsAsync(int take = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkThread>>(_threads.Values.OrderByDescending(thread => thread.UpdatedAt).Take(take).ToArray());

        public Task SaveWorkThreadAsync(WorkThread workThread, CancellationToken cancellationToken = default)
        {
            _threads[workThread.Id] = workThread;
            return Task.CompletedTask;
        }

        public Task AppendWorkContextEventAsync(WorkContextEvent contextEvent, CancellationToken cancellationToken = default)
        {
            var list = _contexts.GetValueOrDefault(contextEvent.WorkThreadId) ?? [];
            list.Add(contextEvent);
            _contexts[contextEvent.WorkThreadId] = list;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkContextEvent>> GetRecentWorkContextEventsAsync(string workThreadId, int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkContextEvent>>((_contexts.GetValueOrDefault(workThreadId) ?? []).OrderByDescending(item => item.CreatedAt).Take(take).Reverse().ToArray());

        public Task AppendConversationMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
        {
            var list = _messages.GetValueOrDefault(message.WorkThreadId) ?? [];
            list.Add(message);
            _messages[message.WorkThreadId] = list;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(string workThreadId, int take = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConversationMessage>>((_messages.GetValueOrDefault(workThreadId) ?? []).OrderByDescending(item => item.CreatedAt).Take(take).Reverse().ToArray());

        public Task SaveContextReceiptAsync(ContextReceiptRecord contextReceipt, CancellationToken cancellationToken = default)
        {
            _receipts[contextReceipt.Id] = contextReceipt;
            return Task.CompletedTask;
        }

        public Task<ContextReceiptRecord?> GetContextReceiptAsync(string contextReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_receipts.TryGetValue(contextReceiptId, out var receipt) ? receipt : null);

        public Task SaveArtifactAsync(WorkArtifact artifact, CancellationToken cancellationToken = default)
        {
            var list = _artifacts.GetValueOrDefault(artifact.WorkThreadId) ?? [];
            var index = list.FindIndex(item => string.Equals(item.Id, artifact.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) list[index] = artifact;
            else list.Add(artifact);
            _artifacts[artifact.WorkThreadId] = list;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkArtifact>> GetArtifactsAsync(string workThreadId, int take = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkArtifact>>((_artifacts.GetValueOrDefault(workThreadId) ?? []).OrderByDescending(item => item.UpdatedAt).Take(take).ToArray());
    }

    private sealed class InMemoryArtifactHistoryRepository : IArtifactHistoryRepository
    {
        public List<WorkArtifactVersion> Versions { get; } = [];

        public Task SaveArtifactVersionAsync(WorkArtifact artifact, string operation, string? actionId = null, CancellationToken cancellationToken = default)
        {
            Versions.Add(new WorkArtifactVersion(
                $"av_{Guid.NewGuid():N}",
                artifact.Id,
                artifact.WorkThreadId,
                Versions.Count(item => item.ArtifactId == artifact.Id) + 1,
                artifact.ArtifactType,
                artifact.Title,
                artifact.Content,
                operation,
                actionId,
                artifact.UpdatedAt,
                artifact.ContextReceiptId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkArtifactVersion>> GetArtifactHistoryAsync(string artifactId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkArtifactVersion>>(Versions.Where(item => item.ArtifactId == artifactId).ToArray());
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        public Task<SecretDescriptor> SetSecretAsync(string name, string secretValue, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SecretDescriptor($"secret://{name}", name, SecretProtectionKind.InMemory, Now, Metadata: metadata));

        public Task<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult<string?>("secret");
        public Task<SecretDescriptor?> DescribeSecretAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult<SecretDescriptor?>(null);
        public Task<bool> DeleteSecretAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StaticHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StaticResponseHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_response);
    }
}
