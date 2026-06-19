using System.Diagnostics;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;

namespace Threadline.Service;

public sealed class ThreadlineAskService
{
    private const int DefaultMaxOutputTokens = 1200;

    private readonly ISessionRepository _sessions;
    private readonly ProviderConnectionService _providers;
    private readonly SecretService _secrets;
    private readonly PromptComposer _promptComposer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClock _clock;
    private readonly IAuditRepository? _auditRepository;

    public ThreadlineAskService(
        ISessionRepository sessions,
        ProviderConnectionService providers,
        SecretService secrets,
        PromptComposer promptComposer,
        IHttpClientFactory httpClientFactory,
        IClock clock,
        IAuditRepository? auditRepository = null)
    {
        _sessions = sessions;
        _providers = providers;
        _secrets = secrets;
        _promptComposer = promptComposer;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
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

        var apiKey = await ResolveApiKeyAsync(providerConnection, cancellationToken);
        var events = await _sessions.GetRecentEventsAsync(sessionId, request.TakeRecentEvents ?? 20, cancellationToken);
        var summary = await _sessions.GetLatestSummaryAsync(sessionId, cancellationToken);
        var messages = _promptComposer.Compose(new ThreadlinePromptContext(request.Question.Trim(), request.CurrentWindow, summary, events));
        var llmRequest = new LlmRequest(providerConnection.DefaultModel, messages, Temperature: 0.2, MaxOutputTokens: DefaultMaxOutputTokens);
        var provider = new OpenAiCompatibleProvider(
            _httpClientFactory.CreateClient(nameof(ThreadlineAskService)),
            new OpenAiCompatibleProviderOptions(providerConnection.ProviderName, providerConnection.BaseUrl, apiKey, providerConnection.DefaultModel));

        await AppendAuditAsync(
            AuditEvent.Create(sessionId, AuditEventType.ProviderCallStarted, _clock.UtcNow, $"Provider call started: {providerConnection.ProviderName}", new Dictionary<string, string>
            {
                ["provider"] = providerConnection.ProviderName,
                ["model"] = providerConnection.DefaultModel,
                ["messageCount"] = messages.Count.ToString(),
                ["recentEventCount"] = events.Count.ToString()
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
                    ["answerLength"] = response.Content.Length.ToString()
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
                    ["errorMessage"] = ex.Message
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

    private Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        _auditRepository is null ? Task.CompletedTask : _auditRepository.AppendAuditEventAsync(auditEvent, cancellationToken);
}
