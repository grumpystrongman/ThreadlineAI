using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public static class LlmProviderFactory
{
    private static readonly HashSet<string> AnthropicProviderNames = new(StringComparer.OrdinalIgnoreCase) { "Claude", "Anthropic" };

    public static bool IsAnthropicProvider(string providerName) =>
        AnthropicProviderNames.Contains(providerName);

    public static ILlmProvider Create(HttpClient httpClient, string providerName, string baseUrl, string apiKey, string defaultModel)
    {
        if (IsAnthropicProvider(providerName))
        {
            return new AnthropicProvider(httpClient, new AnthropicProviderOptions(baseUrl, apiKey, defaultModel));
        }

        return new OpenAiCompatibleProvider(httpClient, new OpenAiCompatibleProviderOptions(providerName, baseUrl, apiKey, defaultModel));
    }
}

public sealed record OpenAiCompatibleProviderOptions(string ProviderName, string BaseUrl, string ApiKey, string DefaultModel);

public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderOptions _options;
    public OpenAiCompatibleProvider(HttpClient httpClient, OpenAiCompatibleProviderOptions options) { _httpClient = httpClient; _options = options; }
    public string Name => _options.ProviderName;
    public LlmProviderCapabilities Capabilities { get; } = new(false, true, false);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        var hasImages = request.Messages.Any(m => m.HasImages);
        return ShouldUseResponsesApi(_options) && !hasImages
            ? await CompleteWithResponsesApiAsync(request, model, cancellationToken)
            : await CompleteWithChatCompletionsAsync(request, model, cancellationToken);
    }

    private async Task<LlmResponse> CompleteWithResponsesApiAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        using var message = CreateProviderRequest("responses");
        message.Content = JsonContent.Create(new ResponsesApiRequest(model, request.Messages.Select(m => new ResponsesInputMessage(NormalizeResponsesRole(m.Role), m.Content)).ToArray(), request.MaxOutputTokens), options: JsonOptions);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ResponsesApiResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Provider returned an empty response.");
        return new LlmResponse(Name, model, ExtractResponsesOutputText(payload), new Dictionary<string, string> { ["providerEndpoint"] = "responses" });
    }

    private async Task<LlmResponse> CompleteWithChatCompletionsAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        using var message = CreateProviderRequest("chat/completions");
        var chatMessages = request.Messages.Select(BuildChatMessage).ToArray();
        message.Content = JsonContent.Create(new ChatCompletionRequest(model, chatMessages, request.Temperature, request.MaxOutputTokens), options: JsonOptions);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Provider returned an empty response.");
        return new LlmResponse(Name, model, payload.Choices.FirstOrDefault()?.Message?.ContentText ?? string.Empty, new Dictionary<string, string> { ["providerEndpoint"] = "chat/completions" });
    }

    private HttpRequestMessage CreateProviderRequest(string relativePath)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUri(_options.BaseUrl, relativePath));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        return message;
    }

    private static Uri BuildEndpointUri(string baseUrl, string relativePath)
    {
        var normalizedBaseUrl = baseUrl.Trim();
        if (!normalizedBaseUrl.EndsWith('/', StringComparison.Ordinal))
        {
            normalizedBaseUrl += "/";
        }

        return new Uri(new Uri(normalizedBaseUrl), relativePath);
    }

    private static bool ShouldUseResponsesApi(OpenAiCompatibleProviderOptions options)
    {
        if (options.ProviderName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            && baseUri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeResponsesRole(string role) => role switch
    {
        "system" => "system",
        "developer" => "developer",
        "assistant" => "assistant",
        _ => "user"
    };

    private static string ExtractResponsesOutputText(ResponsesApiResponse payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.OutputText))
        {
            return payload.OutputText;
        }

        if (payload.Output is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in payload.Output)
        {
            if (item.Content is null)
            {
                continue;
            }

            foreach (var content in item.Content)
            {
                if (!string.IsNullOrWhiteSpace(content.Text))
                {
                    parts.Add(content.Text);
                }
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private sealed record ResponsesApiRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<ResponsesInputMessage> Input,
        [property: JsonPropertyName("max_output_tokens")] int? MaxOutputTokens);

    private sealed record ResponsesInputMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponsesApiResponse(
        [property: JsonPropertyName("output_text")] string? OutputText,
        [property: JsonPropertyName("output")] IReadOnlyList<ResponsesOutputItem>? Output);

    private sealed record ResponsesOutputItem([property: JsonPropertyName("content")] IReadOnlyList<ResponsesContentItem>? Content);
    private sealed record ResponsesContentItem([property: JsonPropertyName("text")] string? Text);

    private static ChatMessage BuildChatMessage(LlmMessage m)
    {
        if (!m.HasImages)
            return new ChatMessage(m.Role, new ChatContentValue(m.Content));

        var parts = new List<object>();
        parts.Add(new ChatContentTextPart("text", m.Content));
        foreach (var img in m.ImageAttachments!)
        {
            var dataUri = $"data:{img.MediaType};base64,{Convert.ToBase64String(img.ImageBytes)}";
            parts.Add(new ChatContentImagePart("image_url", new ChatImageUrl(dataUri)));
        }
        return new ChatMessage(m.Role, new ChatContentValue(parts));
    }

    private sealed record ChatCompletionRequest([property: JsonPropertyName("model")] string Model, [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages, [property: JsonPropertyName("temperature")] double Temperature, [property: JsonPropertyName("max_tokens")] int? MaxTokens);

    /// Content can be a string or an array of content parts for vision.
    [JsonConverter(typeof(ChatContentValueConverter))]
    private readonly record struct ChatContentValue
    {
        public string? StringValue { get; }
        public IReadOnlyList<object>? ArrayValue { get; }
        public ChatContentValue(string s) { StringValue = s; ArrayValue = null; }
        public ChatContentValue(IReadOnlyList<object> a) { StringValue = null; ArrayValue = a; }
    }

    private sealed class ChatContentValueConverter : JsonConverter<ChatContentValue>
    {
        public override ChatContentValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new ChatContentValue(reader.GetString()!);
            // For reading responses, we only need strings
            using var doc = JsonDocument.ParseValue(ref reader);
            return new ChatContentValue(doc.RootElement.GetRawText());
        }

        public override void Write(Utf8JsonWriter writer, ChatContentValue value, JsonSerializerOptions options)
        {
            if (value.ArrayValue is not null)
                JsonSerializer.Serialize(writer, value.ArrayValue, options);
            else
                writer.WriteStringValue(value.StringValue ?? string.Empty);
        }
    }

    private sealed record ChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] ChatContentValue Content)
    {
        // For deserialization from responses where content is always a string
        [JsonIgnore]
        public string ContentText => Content.StringValue ?? string.Empty;
    }
    private sealed record ChatContentTextPart([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("text")] string Text);
    private sealed record ChatContentImagePart([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("image_url")] ChatImageUrl ImageUrl);
    private sealed record ChatImageUrl([property: JsonPropertyName("url")] string Url);
    private sealed record ChatCompletionResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices);
    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);
}

public sealed record AnthropicProviderOptions(string BaseUrl, string ApiKey, string DefaultModel);

public sealed class AnthropicProvider : ILlmProvider
{
    private const string AnthropicVersion = "2023-06-01";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient _httpClient;
    private readonly AnthropicProviderOptions _options;

    public AnthropicProvider(HttpClient httpClient, AnthropicProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "Claude";
    public LlmProviderCapabilities Capabilities { get; } = new(false, true, false);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        var (systemPrompt, userMessages) = SeparateSystemPrompt(request.Messages);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUri(_options.BaseUrl, "messages"));
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);

        var body = new AnthropicMessagesRequest(
            model,
            request.MaxOutputTokens ?? 2200,
            systemPrompt,
            userMessages.Select(BuildAnthropicMessage).ToArray());

        httpRequest.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AnthropicMessagesResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Anthropic returned an empty response.");

        var text = ExtractResponseText(payload);
        return new LlmResponse(Name, payload.Model ?? model, text, new Dictionary<string, string>
        {
            ["providerEndpoint"] = "messages",
            ["stopReason"] = payload.StopReason ?? "unknown"
        });
    }

    private static (string? SystemPrompt, IReadOnlyList<LlmMessage> Messages) SeparateSystemPrompt(IReadOnlyList<LlmMessage> messages)
    {
        string? systemPrompt = null;
        var userMessages = new List<LlmMessage>();

        foreach (var message in messages)
        {
            if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                systemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? message.Content : systemPrompt + "\n\n" + message.Content;
            }
            else
            {
                userMessages.Add(message);
            }
        }

        return (systemPrompt, userMessages);
    }

    private static string ExtractResponseText(AnthropicMessagesResponse payload)
    {
        if (payload.Content is null || payload.Content.Count == 0)
        {
            return string.Empty;
        }

        var parts = payload.Content
            .Where(c => c.Type == "text" && !string.IsNullOrWhiteSpace(c.Text))
            .Select(c => c.Text!)
            .ToList();

        return string.Join(Environment.NewLine, parts);
    }

    private static Uri BuildEndpointUri(string baseUrl, string relativePath)
    {
        var normalizedBaseUrl = baseUrl.Trim();
        if (!normalizedBaseUrl.EndsWith('/', StringComparison.Ordinal))
        {
            normalizedBaseUrl += "/";
        }

        return new Uri(new Uri(normalizedBaseUrl), relativePath);
    }

    private static AnthropicMessage BuildAnthropicMessage(LlmMessage m)
    {
        if (!m.HasImages)
            return new AnthropicMessage(m.Role, new AnthropicContentValue(m.Content));

        var parts = new List<object>();
        foreach (var img in m.ImageAttachments!)
        {
            var b64 = Convert.ToBase64String(img.ImageBytes);
            parts.Add(new AnthropicImageContentBlock("image", new AnthropicImageSource("base64", img.MediaType, b64)));
        }
        parts.Add(new AnthropicTextContentBlock("text", m.Content));
        return new AnthropicMessage(m.Role, new AnthropicContentValue(parts));
    }

    private sealed record AnthropicMessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    [JsonConverter(typeof(AnthropicContentValueConverter))]
    private readonly record struct AnthropicContentValue
    {
        public string? StringValue { get; }
        public IReadOnlyList<object>? ArrayValue { get; }
        public AnthropicContentValue(string s) { StringValue = s; ArrayValue = null; }
        public AnthropicContentValue(IReadOnlyList<object> a) { StringValue = null; ArrayValue = a; }
    }

    private sealed class AnthropicContentValueConverter : JsonConverter<AnthropicContentValue>
    {
        public override AnthropicContentValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new AnthropicContentValue(reader.GetString()!);
            using var doc = JsonDocument.ParseValue(ref reader);
            return new AnthropicContentValue(doc.RootElement.GetRawText());
        }

        public override void Write(Utf8JsonWriter writer, AnthropicContentValue value, JsonSerializerOptions options)
        {
            if (value.ArrayValue is not null)
                JsonSerializer.Serialize(writer, value.ArrayValue, options);
            else
                writer.WriteStringValue(value.StringValue ?? string.Empty);
        }
    }

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] AnthropicContentValue Content);

    private sealed record AnthropicTextContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record AnthropicImageContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("source")] AnthropicImageSource Source);

    private sealed record AnthropicImageSource(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("media_type")] string MediaType,
        [property: JsonPropertyName("data")] string Data);

    private sealed record AnthropicMessagesResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicResponseContentBlock>? Content,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("stop_reason")] string? StopReason);

    private sealed record AnthropicResponseContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
