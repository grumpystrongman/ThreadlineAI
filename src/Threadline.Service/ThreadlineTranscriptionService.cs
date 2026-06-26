using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Threadline.Core;
using Threadline.Infrastructure;
using Threadline.Infrastructure.Security;

namespace Threadline.Service;

public sealed record TranscriptionRequest(
    string AudioFilePath,
    string? Provider = null,
    string? Language = null,
    bool Translate = false);

public sealed record TranscriptionResponse(
    string Transcript,
    string Provider,
    string? Language,
    long DurationMs,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed class ThreadlineTranscriptionService
{
    private readonly ProviderConnectionService _providers;
    private readonly SecretService _secrets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuditRepository _audit;
    private readonly IClock _clock;

    public ThreadlineTranscriptionService(
        ProviderConnectionService providers,
        SecretService secrets,
        IHttpClientFactory httpClientFactory,
        IAuditRepository audit,
        IClock clock)
    {
        _providers = providers;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
        _audit = audit;
        _clock = clock;
    }

    public async Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.AudioFilePath))
        {
            throw new InvalidOperationException($"Audio file not found: {request.AudioFilePath}");
        }

        var provider = await ResolveProviderAsync(request.Provider, cancellationToken);
        if (provider is null)
        {
            throw new InvalidOperationException("No configured provider was found for transcription. Save provider settings first.");
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException($"Provider '{provider.ProviderName}' is missing a base URL.");
        }

        var apiKey = await ResolveApiKeyAsync(provider, cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var transcript = LlmProviderFactory.IsAnthropicProvider(provider.ProviderName)
                ? throw new InvalidOperationException("Anthropic/Claude does not support audio transcription. Use an OpenAI-compatible provider for transcription.")
                : await TranscribeWithWhisperApiAsync(provider, apiKey, request, cancellationToken);

            stopwatch.Stop();

            await _audit.AppendAuditEventAsync(
                AuditEvent.Create(null, AuditEventType.ProviderCallCompleted, _clock.UtcNow,
                    $"Transcription completed via {provider.ProviderName}",
                    new Dictionary<string, string>
                    {
                        ["source"] = "ThreadlineTranscription",
                        ["provider"] = provider.ProviderName,
                        ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["audioFile"] = Path.GetFileName(request.AudioFilePath),
                        ["transcriptLength"] = transcript.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }),
                cancellationToken);

            return new TranscriptionResponse(
                transcript,
                provider.ProviderName,
                request.Language,
                stopwatch.ElapsedMilliseconds,
                new Dictionary<string, string>
                {
                    ["audioFile"] = Path.GetFileName(request.AudioFilePath),
                    ["endpoint"] = "audio/transcriptions"
                });
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            stopwatch.Stop();
            await _audit.AppendAuditEventAsync(
                AuditEvent.Create(null, AuditEventType.ProviderCallFailed, _clock.UtcNow,
                    $"Transcription failed via {provider.ProviderName}: {ex.Message}",
                    new Dictionary<string, string>
                    {
                        ["source"] = "ThreadlineTranscription",
                        ["provider"] = provider.ProviderName,
                        ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["errorType"] = ex.GetType().Name
                    }),
                cancellationToken);
            throw;
        }
    }

    private async Task<string> TranscribeWithWhisperApiAsync(
        ProviderConnection provider,
        string apiKey,
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = provider.BaseUrl!.Trim();
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        var endpoint = request.Translate
            ? new Uri(new Uri(baseUrl), "audio/translations")
            : new Uri(new Uri(baseUrl), "audio/transcriptions");

        using var httpClient = _httpClientFactory.CreateClient(nameof(ThreadlineTranscriptionService));
        using var content = new MultipartFormDataContent();

        var audioBytes = await File.ReadAllBytesAsync(request.AudioFilePath, cancellationToken);
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", Path.GetFileName(request.AudioFilePath));
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("text"), "response_format");

        if (!string.IsNullOrWhiteSpace(request.Language) && !request.Translate)
        {
            content.Add(new StringContent(request.Language), "language");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        // Whisper API with response_format=text returns plain text
        // But some providers return JSON, so try to parse it
        if (responseText.TrimStart().StartsWith('{'))
        {
            try
            {
                var json = JsonSerializer.Deserialize<WhisperJsonResponse>(responseText);
                if (!string.IsNullOrWhiteSpace(json?.Text))
                    return json.Text.Trim();
            }
            catch (JsonException)
            {
                // Fall through to return raw text
            }
        }

        return responseText.Trim();
    }

    private async Task<ProviderConnection?> ResolveProviderAsync(string? providerName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return await _providers.GetAsync(providerName.Trim(), cancellationToken);
        }

        var providers = await _providers.ListAsync(cancellationToken);
        // Prefer OpenAI for transcription since it has native Whisper support
        return providers.FirstOrDefault(p => p.Status == ProviderConnectionStatus.Ready &&
                p.ProviderName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            ?? providers.FirstOrDefault(p => p.Status == ProviderConnectionStatus.Ready &&
                !LlmProviderFactory.IsAnthropicProvider(p.ProviderName))
            ?? providers.FirstOrDefault(p => p.Status == ProviderConnectionStatus.Ready);
    }

    private async Task<string> ResolveApiKeyAsync(ProviderConnection provider, CancellationToken cancellationToken)
    {
        if (provider.AuthType is ProviderAuthType.None or ProviderAuthType.LocalEndpoint)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(provider.CredentialReference))
        {
            throw new InvalidOperationException($"Provider '{provider.ProviderName}' is missing a credential reference.");
        }

        return await _secrets.GetValueAsync(provider.CredentialReference, cancellationToken)
            ?? throw new InvalidOperationException($"Provider '{provider.ProviderName}' credential could not be resolved.");
    }

    private sealed record WhisperJsonResponse(
        [property: JsonPropertyName("text")] string? Text);
}
