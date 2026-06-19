using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Threadline.Windows.Services;

public sealed class ThreadlineLocalClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ThreadlineLocalClient(string baseUrl = "http://localhost:5057", string? localAccessToken = null)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(localAccessToken))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Threadline-Token", localAccessToken);
        }
    }

    public async Task<ServiceHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<ServiceHealth>("health", cancellationToken);

    public async Task<ThreadlineSessionDto> StartSessionAsync(string name, string provider, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("sessions", new { name, provider }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<ThreadlineSessionDto>(response, cancellationToken);
    }

    public async Task<ThreadlineSessionDto?> GetActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("sessions/active", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<ThreadlineSessionDto>(response, cancellationToken);
    }

    public async Task<WindowAttachmentDto> AttachWindowAsync(string sessionId, ActiveWindowSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            applicationName = snapshot.ApplicationName,
            processName = snapshot.ProcessName ?? "Unknown",
            windowTitle = snapshot.WindowTitle ?? "Unknown",
            processId = snapshot.ProcessId,
            executablePath = snapshot.ExecutablePath,
            isForeground = true,
            metadata = new Dictionary<string, string>
            {
                ["source"] = "Threadline.Windows",
                ["windowHandle"] = snapshot.Handle.ToString()
            }
        };

        var response = await _httpClient.PostAsJsonAsync($"sessions/{sessionId}/windows/attach", request, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<WindowAttachmentDto>(response, cancellationToken);
    }

    public async Task<WindowAttachmentDto?> GetCurrentWindowAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"sessions/{sessionId}/windows/current", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<WindowAttachmentDto>(response, cancellationToken);
    }

    public async Task<ContextPreviewDto> PreviewCurrentWindowAsync(string sessionId, bool userApproved = true, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"sessions/{sessionId}/windows/current/preview", new { userApproved }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<ContextPreviewDto>(response, cancellationToken);
    }

    public async Task<ContextEventDto> StoreCurrentWindowAsync(string sessionId, bool userApproved = true, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"sessions/{sessionId}/windows/current/store", new { userApproved }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<ContextEventDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<LlmMessageDto>> ComposePromptAsync(string sessionId, string question, string? currentWindow, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"sessions/{sessionId}/prompt", new { question, currentWindow, takeRecentEvents = 20 }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<List<LlmMessageDto>>(response, cancellationToken);
    }

    public async Task<AskResponseDto> AskAsync(string sessionId, string question, string? currentWindow, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            question,
            currentWindow,
            takeRecentEvents = 20
        };

        var response = await _httpClient.PostAsJsonAsync($"sessions/{sessionId}/ask", request, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<AskResponseDto>(response, cancellationToken);
    }

    public async Task<WindowActionDto> ProposeInsertActionAsync(string sessionId, string payload, bool userApproved = true, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            kind = "InsertText",
            description = "Insert generated text into attached window",
            payload,
            userApproved,
            risk = "Medium"
        };

        var response = await _httpClient.PostAsJsonAsync($"sessions/{sessionId}/actions", request, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<WindowActionDto>(response, cancellationToken);
    }

    public async Task<WindowActionDto> CompleteActionAsync(string actionId, string resultMessage, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"actions/{actionId}/complete", new { resultMessage, failed = false }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<WindowActionDto>(response, cancellationToken);
    }

    private async Task<T> GetRequiredAsync<T>(string uri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(uri, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Threadline service returned {(int)response.StatusCode}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Threadline service returned an empty response.");
    }
}

public sealed record ServiceHealth(string Status, string Service, string Storage, bool AuthRequired, int MaxContextCharacters);
public sealed record ThreadlineSessionDto(string Id, string Name, string Status, string? ActiveProvider);
public sealed record WindowSnapshotDto(string Id, string ApplicationName, string ProcessName, string WindowTitle, int? ProcessId, string? ExecutablePath, string? Uri, bool IsForeground);
public sealed record WindowAttachmentDto(string Id, string SessionId, WindowSnapshotDto Snapshot, string Status, DateTimeOffset AttachedAt, DateTimeOffset? DetachedAt);
public sealed record ContextPreviewDto(string RedactedContent, bool WillBeStored, bool RequiresExplicitApproval, string ConsentState, IReadOnlyList<string> Warnings);
public sealed record ContextEventDto(string Id, string SessionId, string Source, string ContextType, string Content);
public sealed record LlmMessageDto(string Role, string Content);
public sealed record AskResponseDto(string Answer, IReadOnlyList<LlmMessageDto>? Messages);
public sealed record WindowActionDto(string Id, string SessionId, string? AttachmentId, string Kind, string Description, string Payload, string Risk, string Status, bool RequiresApproval, string? ResultMessage);
