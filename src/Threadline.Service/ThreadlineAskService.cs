using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;

namespace Threadline.Service;

public sealed class ThreadlineAskService
{
    private const int DefaultMaxOutputTokens = 2200;

    private readonly ISessionRepository _sessions;
    private readonly ProviderConnectionService _providers;
    private readonly SecretService _secrets;
    private readonly PromptComposer _promptComposer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClock _clock;
    private readonly ThreadlineServiceOptions _options;
    private readonly SecretRedactor _redactor;
    private readonly IAuditRepository? _auditRepository;

    public ThreadlineAskService(
        ISessionRepository sessions,
        ProviderConnectionService providers,
        SecretService secrets,
        PromptComposer promptComposer,
        IHttpClientFactory httpClientFactory,
        IClock clock,
        ThreadlineServiceOptions options,
        SecretRedactor redactor,
        IAuditRepository? auditRepository = null)
    {
        _sessions = sessions;
        _providers = providers;
        _secrets = secrets;
        _promptComposer = promptComposer;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _options = options;
        _redactor = redactor;
        _auditRepository = auditRepository;
    }

    public async Task<AskResponse> AskAsync(string sessionId, ComposePromptRequest request, CancellationToken cancellationToken = default)
    {
        var session = await _sessions.GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Threadline session was not found.");

        if (session.Status != SessionStatus.Active)
        {
            throw new InvalidOperationException("Threadline session is not active.");
        }

        if (string.IsNullOrWhiteSpace(session.ActiveProvider))
        {
            throw new InvalidOperationException("This session does not have an active provider. Start the session with a provider or configure one before asking.");
        }

        var providerConnection = await _providers.GetAsync(session.ActiveProvider, cancellationToken)
            ?? throw new InvalidOperationException($"Provider '{session.ActiveProvider}' is not configured.");

        if (providerConnection.Status != ProviderConnectionStatus.Ready)
        {
            throw new InvalidOperationException($"Provider '{providerConnection.ProviderName}' is not ready. Current status: {providerConnection.Status}.");
        }

        if (string.IsNullOrWhiteSpace(providerConnection.BaseUrl))
        {
            throw new InvalidOperationException($"Provider '{providerConnection.ProviderName}' is missing a base URL.");
        }

        if (string.IsNullOrWhiteSpace(providerConnection.DefaultModel))
        {
            throw new InvalidOperationException($"Provider '{providerConnection.ProviderName}' is missing a default model.");
        }

        if (_options.LocalOnlyMode && !IsLocalProvider(providerConnection))
        {
            await AppendAuditAsync(
                AuditEvent.Create(sessionId, AuditEventType.ProviderCallFailed, _clock.UtcNow, $"Provider call blocked by local-only mode: {providerConnection.ProviderName}", new Dictionary<string, string>
                {
                    ["provider"] = providerConnection.ProviderName,
                    ["model"] = providerConnection.DefaultModel,
                    ["localOnlyMode"] = "true"
                }),
                cancellationToken);
            throw new InvalidOperationException("Local-only mode is enabled. Remote provider calls are blocked for this session.");
        }

        var apiKey = await ResolveApiKeyAsync(providerConnection, cancellationToken);
        var events = await _sessions.GetRecentEventsAsync(sessionId, request.TakeRecentEvents ?? 20, cancellationToken);
        var summary = await _sessions.GetLatestSummaryAsync(sessionId, cancellationToken);
        var redactedQuestion = _redactor.Redact(request.Question.Trim());
        var redactedCurrentWindow = string.IsNullOrWhiteSpace(request.CurrentWindow) ? null : _redactor.Redact(request.CurrentWindow);
        var messages = _promptComposer.Compose(new ThreadlinePromptContext(redactedQuestion, redactedCurrentWindow, summary, events));
        var llmRequest = new LlmRequest(providerConnection.DefaultModel, messages, Temperature: 0.2, MaxOutputTokens: DefaultMaxOutputTokens);
        var provider = LlmProviderFactory.Create(
            _httpClientFactory.CreateClient(nameof(ThreadlineAskService)),
            providerConnection.ProviderName, providerConnection.BaseUrl, apiKey, providerConnection.DefaultModel);

        var payloadHash = HashMessages(messages);
        await AppendAuditAsync(
            AuditEvent.Create(sessionId, AuditEventType.ProviderCallStarted, _clock.UtcNow, $"Provider context prepared and call started: {providerConnection.ProviderName}", new Dictionary<string, string>
            {
                ["provider"] = providerConnection.ProviderName,
                ["model"] = providerConnection.DefaultModel,
                ["messageCount"] = messages.Count.ToString(),
                ["recentEventCount"] = events.Count.ToString(),
                ["contextPayloadSha256"] = payloadHash,
                ["promptCharacterCount"] = messages.Sum(message => message.Content.Length).ToString(),
                ["contextEventIds"] = string.Join(',', events.Select(item => item.Id).Take(50)),
                ["localOnlyMode"] = _options.LocalOnlyMode.ToString()
            }),
            cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await provider.CompleteAsync(llmRequest, cancellationToken);
            stopwatch.Stop();

            await AppendAuditAsync(
                AuditEvent.Create(sessionId, AuditEventType.ProviderCallCompleted, _clock.UtcNow, $"Provider call completed: {providerConnection.ProviderName}", new Dictionary<string, string>
                {
                    ["provider"] = response.ProviderName,
                    ["model"] = response.Model,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["answerLength"] = response.Content.Length.ToString(),
                    ["contextPayloadSha256"] = payloadHash
                }),
                cancellationToken);

            return new AskResponse(response.Content, messages, response.ProviderName, response.Model, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await AppendAuditAsync(
                AuditEvent.Create(sessionId, AuditEventType.ProviderCallFailed, _clock.UtcNow, $"Provider call failed: {providerConnection.ProviderName}", new Dictionary<string, string>
                {
                    ["provider"] = providerConnection.ProviderName,
                    ["model"] = providerConnection.DefaultModel,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["errorType"] = ex.GetType().Name,
                    ["errorMessage"] = _redactor.Redact(ex.Message),
                    ["contextPayloadSha256"] = payloadHash
                }),
                cancellationToken);

            throw;
        }
    }

    private async Task<string> ResolveApiKeyAsync(ProviderConnection providerConnection, CancellationToken cancellationToken)
    {
        if (providerConnection.AuthType is ProviderAuthType.None or ProviderAuthType.LocalEndpoint)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(providerConnection.CredentialReference))
        {
            throw new InvalidOperationException($"Provider '{providerConnection.ProviderName}' is missing a credential reference.");
        }

        return await _secrets.GetValueAsync(providerConnection.CredentialReference, cancellationToken)
            ?? throw new InvalidOperationException($"Provider '{providerConnection.ProviderName}' credential could not be resolved.");
    }

    private static bool IsLocalProvider(ProviderConnection providerConnection)
    {
        if (!Uri.TryCreate(providerConnection.BaseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string HashMessages(IReadOnlyList<LlmMessage> messages)
    {
        var payload = string.Join('\n', messages.Select(message => $"{message.Role}:{message.Content}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        _auditRepository is null ? Task.CompletedTask : _auditRepository.AppendAuditEventAsync(auditEvent, cancellationToken);
}
