using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Threadline.Core;

namespace Threadline.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<string, ThreadlineSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<ContextEvent>> _events = new();
    private readonly ConcurrentDictionary<string, string> _summaries = new();

    public Task<ThreadlineSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(_sessions.TryGetValue(sessionId, out var session) ? session : null);
    public Task<ThreadlineSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessions.Values.Where(s => s.Status == SessionStatus.Active).OrderByDescending(s => s.CreatedAt).FirstOrDefault());
    public Task SaveSessionAsync(ThreadlineSession session, CancellationToken cancellationToken = default) { _sessions[session.Id] = session; return Task.CompletedTask; }
    public Task AppendEventAsync(ContextEvent contextEvent, CancellationToken cancellationToken = default) { var list = _events.GetOrAdd(contextEvent.SessionId, _ => []); lock (list) list.Add(contextEvent); return Task.CompletedTask; }
    public Task<IReadOnlyList<ContextEvent>> GetRecentEventsAsync(string sessionId, int take, CancellationToken cancellationToken = default) { if (!_events.TryGetValue(sessionId, out var list)) return Task.FromResult<IReadOnlyList<ContextEvent>>([]); lock (list) return Task.FromResult<IReadOnlyList<ContextEvent>>(list.OrderByDescending(e => e.Timestamp).Take(take).Reverse().ToArray()); }
    public Task SaveSummaryAsync(string sessionId, string summary, CancellationToken cancellationToken = default) { _summaries[sessionId] = summary; return Task.CompletedTask; }
    public Task<string?> GetLatestSummaryAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(_summaries.TryGetValue(sessionId, out var summary) ? summary : null);
}

public sealed class SessionService
{
    private readonly ISessionRepository _repository;
    private readonly IClock _clock;
    private readonly ContextPreviewBuilder _previewBuilder;
    private readonly IAuditRepository? _auditRepository;

    public SessionService(
        ISessionRepository repository,
        IClock clock,
        SecretRedactor redactor,
        CapturePolicy capturePolicy,
        ContextPreviewBuilder? previewBuilder = null,
        IAuditRepository? auditRepository = null)
    {
        _repository = repository;
        _clock = clock;
        _previewBuilder = previewBuilder ?? new ContextPreviewBuilder(capturePolicy, redactor);
        _auditRepository = auditRepository;
    }

    public async Task<ThreadlineSession> StartAsync(string name, string? provider = null, CancellationToken cancellationToken = default)
    {
        var session = ThreadlineSession.Start(name, _clock.UtcNow, provider);
        await _repository.SaveSessionAsync(session, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(session.Id, AuditEventType.SessionStarted, _clock.UtcNow, $"Session started: {session.Name}"), cancellationToken);
        return session;
    }

    public async Task<ContextPreview> PreviewContextAsync(ContextEvent contextEvent, CancellationToken cancellationToken = default)
    {
        var preview = _previewBuilder.Build(contextEvent);
        await AppendAuditAsync(
            AuditEvent.Create(contextEvent.SessionId, AuditEventType.ContextPreviewed, _clock.UtcNow, "Context previewed.", AuditMetadata(preview)),
            cancellationToken);
        return preview;
    }

    public async Task<ContextEvent> AppendContextAsync(ContextEvent contextEvent, CancellationToken cancellationToken = default)
    {
        var preview = _previewBuilder.Build(contextEvent);

        if (!preview.Decision.IsAllowed)
        {
            await AppendAuditAsync(AuditEvent.Create(contextEvent.SessionId, AuditEventType.ContextBlocked, _clock.UtcNow, preview.Decision.Reason, AuditMetadata(preview)), cancellationToken);
            throw new InvalidOperationException($"Context capture blocked: {preview.Decision.Reason}");
        }

        if (preview.RequiresExplicitApproval && !contextEvent.UserApproved)
        {
            await AppendAuditAsync(AuditEvent.Create(contextEvent.SessionId, AuditEventType.ContextBlocked, _clock.UtcNow, "Context requires explicit user approval before storage.", AuditMetadata(preview)), cancellationToken);
            throw new InvalidOperationException("Context requires explicit user approval before storage.");
        }

        var approved = preview.OriginalEvent with
        {
            Content = preview.RedactedContent,
            UserApproved = true,
            Metadata = MergeMetadata(preview.OriginalEvent.Metadata, preview.PrivacyMetadata, ConsentState.Stored)
        };

        if (preview.RedactionFindings?.Count > 0)
        {
            await AppendAuditAsync(AuditEvent.Create(contextEvent.SessionId, AuditEventType.RedactionApplied, _clock.UtcNow, $"Applied {preview.RedactionFindings.Count} redaction(s).", AuditMetadata(preview)), cancellationToken);
        }

        await _repository.AppendEventAsync(approved, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(contextEvent.SessionId, AuditEventType.ContextStored, _clock.UtcNow, $"Stored context event {approved.Id}", AuditMetadata(preview, ConsentState.Stored)), cancellationToken);
        return approved;
    }

    private static IReadOnlyDictionary<string, string> AuditMetadata(ContextPreview preview, ConsentState? overrideConsentState = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["contextEventId"] = preview.OriginalEvent.Id,
            ["contextSource"] = preview.OriginalEvent.Source.ToString(),
            ["contextType"] = preview.OriginalEvent.ContextType,
            ["sensitivity"] = preview.OriginalEvent.Sensitivity.ToString(),
            ["consentState"] = (overrideConsentState ?? preview.ConsentState).ToString(),
            ["redactionCount"] = (preview.RedactionFindings?.Count ?? 0).ToString(),
            ["requiresApproval"] = preview.RequiresExplicitApproval.ToString()
        };

        if (preview.Decision.MatchedRule is not null)
        {
            metadata["matchedRuleId"] = preview.Decision.MatchedRule.Id;
            metadata["matchedRuleSource"] = preview.Decision.MatchedRule.Source.ToString();
            metadata["matchedRuleAction"] = preview.Decision.MatchedRule.Action.ToString();
        }

        if (preview.RedactionFindings?.Count > 0)
        {
            metadata["redactionKinds"] = string.Join(",", preview.RedactionFindings.Select(f => f.Kind).Distinct());
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, string>? MergeMetadata(IReadOnlyDictionary<string, string>? original, IReadOnlyDictionary<string, string>? privacy, ConsentState consentState)
    {
        var result = new Dictionary<string, string>();
        if (original is not null)
        {
            foreach (var pair in original)
            {
                result[pair.Key] = pair.Value;
            }
        }

        if (privacy is not null)
        {
            foreach (var pair in privacy)
            {
                result[$"privacy.{pair.Key}"] = pair.Value;
            }
        }

        result["privacy.consentState"] = consentState.ToString();
        return result;
    }

    private Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        _auditRepository is null ? Task.CompletedTask : _auditRepository.AppendAuditEventAsync(auditEvent, cancellationToken);
}

public sealed class ProviderConnectionService
{
    private readonly IProviderConnectionRepository _repository;
    private readonly IAuditRepository? _auditRepository;
    private readonly IClock _clock;

    public ProviderConnectionService(IProviderConnectionRepository repository, IClock clock, IAuditRepository? auditRepository = null)
    {
        _repository = repository;
        _clock = clock;
        _auditRepository = auditRepository;
    }

    public async Task<ProviderConnection> SaveAsync(ProviderConnection connection, CancellationToken cancellationToken = default)
    {
        var saved = connection with { UpdatedAt = _clock.UtcNow };
        await _repository.SaveProviderConnectionAsync(saved, cancellationToken);
        if (_auditRepository is not null)
        {
            await _auditRepository.AppendAuditEventAsync(
                AuditEvent.Create(null, AuditEventType.ProviderConfigured, _clock.UtcNow, $"Provider configured: {connection.ProviderName}"),
                cancellationToken);
        }
        return saved;
    }

    public Task<ProviderConnection?> GetAsync(string providerName, CancellationToken cancellationToken = default) =>
        _repository.GetProviderConnectionAsync(providerName, cancellationToken);

    public Task<IReadOnlyList<ProviderConnection>> ListAsync(CancellationToken cancellationToken = default) =>
        _repository.ListProviderConnectionsAsync(cancellationToken);
}

public sealed record OpenAiCompatibleProviderOptions(string ProviderName, string BaseUrl, string ApiKey, string DefaultModel);

public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderOptions _options;
    public OpenAiCompatibleProvider(HttpClient httpClient, OpenAiCompatibleProviderOptions options) { _httpClient = httpClient; _options = options; }
    public string Name => _options.ProviderName;
    public LlmProviderCapabilities Capabilities { get; } = new(false, false, false);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl), "chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        message.Content = JsonContent.Create(new ChatCompletionRequest(model, request.Messages.Select(m => new ChatMessage(m.Role, m.Content)).ToArray(), request.Temperature, request.MaxOutputTokens));
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Provider returned an empty response.");
        return new LlmResponse(Name, model, payload.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty);
    }

    private sealed record ChatCompletionRequest([property: JsonPropertyName("model")] string Model, [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages, [property: JsonPropertyName("temperature")] double Temperature, [property: JsonPropertyName("max_tokens")] int? MaxTokens);
    private sealed record ChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content);
    private sealed record ChatCompletionResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices);
    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);
}
