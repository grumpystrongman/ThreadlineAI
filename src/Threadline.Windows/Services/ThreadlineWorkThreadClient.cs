using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Threadline.Windows.Services;

public sealed class ThreadlineWorkThreadClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ThreadlineWorkThreadClient(string baseUrl = "http://localhost:5057", string? localAccessToken = null)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(localAccessToken))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Threadline-Token", localAccessToken);
        }
    }

    public async Task<WorkThreadDto?> GetActiveWorkThreadAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("work-threads/active", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<WorkThreadDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkThreadDto>> ListWorkThreadsAsync(int take = 25, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"work-threads?take={take}", cancellationToken);
        return await ReadRequiredAsync<List<WorkThreadDto>>(response, cancellationToken);
    }

    public async Task<WorkThreadDto> StartWorkThreadAsync(string title, string? description = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("work-threads", new { title, description }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<WorkThreadDto>(response, cancellationToken);
    }

    public async Task<WorkThreadDto> ResumeWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"work-threads/{workThreadId}/resume", null, cancellationToken);
        return await ReadRequiredAsync<WorkThreadDto>(response, cancellationToken);
    }

    public async Task<WorkThreadDto> RenameWorkThreadAsync(string workThreadId, string title, string? description = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"work-threads/{workThreadId}/rename",
            new { title, description },
            _jsonOptions,
            cancellationToken);
        return await ReadRequiredAsync<WorkThreadDto>(response, cancellationToken);
    }

    public async Task<WorkThreadDto> CloseWorkThreadAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"work-threads/{workThreadId}/close", null, cancellationToken);
        return await ReadRequiredAsync<WorkThreadDto>(response, cancellationToken);
    }

    public async Task SaveWorkContextEventAsync(string workThreadId, string sourceType, string sourceName, string? appName, string? windowTitle, string? url, string? contentSummary, string captureMode = "Followed", CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"work-threads/{workThreadId}/context-events",
            new { sourceType, sourceName, appName, windowTitle, url, contentSummary, captureMode },
            _jsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SaveConversationMessageAsync(string workThreadId, string role, string content, string? contextReceiptId = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"work-threads/{workThreadId}/messages",
            new { role, content, contextReceiptId },
            _jsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationMessageDto>> GetConversationMessagesAsync(string workThreadId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"work-threads/{workThreadId}/messages?take=200", cancellationToken);
        return await ReadRequiredAsync<List<ConversationMessageDto>>(response, cancellationToken);
    }

    public async Task<ContextReceiptDto> SaveContextReceiptAsync(string workThreadId, string usedSourcesJson, string? notUsedSourcesJson = null, string? limitations = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"work-threads/{workThreadId}/context-receipts",
            new { usedSourcesJson, notUsedSourcesJson, limitations },
            _jsonOptions,
            cancellationToken);
        return await ReadRequiredAsync<ContextReceiptDto>(response, cancellationToken);
    }

    public async Task<WorkArtifactDto> SaveArtifactAsync(string workThreadId, string artifactType, string title, string content, string? contextReceiptId = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"work-threads/{workThreadId}/artifacts",
            new { artifactType, title, content, contextReceiptId },
            _jsonOptions,
            cancellationToken);
        return await ReadRequiredAsync<WorkArtifactDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkArtifactDto>> GetArtifactsAsync(string workThreadId, int take = 25, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"work-threads/{workThreadId}/artifacts?take={take}", cancellationToken);
        return await ReadRequiredAsync<List<WorkArtifactDto>>(response, cancellationToken);
    }

    private async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Threadline service returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Threadline work-thread service returned {(int)response.StatusCode}: {body}");
    }
}

public sealed record WorkThreadDto(string Id, string Title, string? Description, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ClosedAt, DateTimeOffset? LastResumedAt);
public sealed record ConversationMessageDto(string Id, string WorkThreadId, string Role, string Content, DateTimeOffset CreatedAt, string? ContextReceiptId);
public sealed record ContextReceiptDto(string Id, string WorkThreadId, string UsedSourcesJson, string? NotUsedSourcesJson, string? Limitations, DateTimeOffset CreatedAt);
public sealed record WorkArtifactDto(string Id, string WorkThreadId, string ArtifactType, string Title, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? ContextReceiptId);
