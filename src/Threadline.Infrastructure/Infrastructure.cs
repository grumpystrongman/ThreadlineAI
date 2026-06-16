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
    private readonly SecretRedactor _redactor;
    private readonly CapturePolicy _capturePolicy;

    public SessionService(ISessionRepository repository, IClock clock, SecretRedactor redactor, CapturePolicy capturePolicy)
    {
        _repository = repository;
        _clock = clock;
        _redactor = redactor;
        _capturePolicy = capturePolicy;
    }

    public async Task<ThreadlineSession> StartAsync(string name, string? provider = null, CancellationToken cancellationToken = default)
    {
        var session = ThreadlineSession.Start(name, _clock.UtcNow, provider);
        await _repository.SaveSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task<ContextEvent> AppendContextAsync(ContextEvent contextEvent, CancellationToken cancellationToken = default)
    {
        var decision = _capturePolicy.Evaluate(contextEvent);
        if (!decision.IsAllowed) throw new InvalidOperationException($"Context capture blocked: {decision.Reason}");
        var redacted = contextEvent with { Content = _redactor.Redact(contextEvent.Content), UserApproved = contextEvent.UserApproved && !decision.RequiresExplicitApproval };
        await _repository.AppendEventAsync(redacted, cancellationToken);
        return redacted;
    }
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
