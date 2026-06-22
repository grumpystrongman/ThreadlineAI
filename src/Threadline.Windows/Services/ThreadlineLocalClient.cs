using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Threadline.Windows.Services;

public sealed class ThreadlineLocalClient
{
    private readonly Uri _baseAddress;
    private readonly HttpClient _httpClient;
    private readonly NativeWindowContextProvider _nativeWindowContextProvider = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ThreadlineLocalClient(string baseUrl = "http://localhost:5057", string? localAccessToken = null)
    {
        _baseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient = new HttpClient { BaseAddress = _baseAddress };
        if (!string.IsNullOrWhiteSpace(localAccessToken))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Threadline-Token", localAccessToken);
        }
    }

    public async Task<ServiceHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<ServiceHealth>("health", cancellationToken);

    public async Task<ThreadlineDoctorReportDto> GetDoctorAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<ThreadlineDoctorReportDto>("doctor", cancellationToken);

    public async Task<ProviderTestResultDto> TestProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var path = $"providers/{Uri.EscapeDataString(providerName)}/test";
        var response = await _httpClient.PostAsync(path, null, cancellationToken);
        return await ReadRequiredAsync<ProviderTestResultDto>(response, cancellationToken);
    }

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
        var nativeContext = _nativeWindowContextProvider.Capture(snapshot);
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "Threadline.Windows",
            ["windowHandle"] = snapshot.Handle.ToString(),
            ["build"] = "18-native-window-context-providers"
        };

        foreach (var pair in nativeContext.ToWindowMetadata())
        {
            metadata[pair.Key] = pair.Value;
        }

        var request = new
        {
            applicationName = snapshot.ApplicationName,
            processName = snapshot.ProcessName ?? "Unknown",
            windowTitle = snapshot.WindowTitle ?? "Unknown",
            processId = snapshot.ProcessId,
            executablePath = snapshot.ExecutablePath,
            isForeground = true,
            metadata
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

    public async Task<AskResponseDto> AskAsync(string sessionId, string question, string? currentWindow, int takeRecentEvents = 20, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            question,
            currentWindow,
            takeRecentEvents
        };

        var path = $"sessions/{sessionId}/ask";
        var response = await _httpClient.PostAsJsonAsync(path, request, _jsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ThreadlineEndpointNotFoundException($"POST {new Uri(_baseAddress, path)} returned 404. The sidecar reached a Threadline service on {_baseAddress}, but that running service does not expose the Ask endpoint. Stop any old Threadline.Service process, pull/build the latest repo, and restart the service from the current source tree.");
        }

        return await ReadRequiredAsync<AskResponseDto>(response, cancellationToken);
    }

    public async Task SaveProviderCredentialAsync(string providerName, string apiKey, string baseUrl, string defaultModel, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            secretValue = apiKey,
            authType = "ApiKey",
            baseUrl,
            defaultModel,
            status = "Ready",
            metadata = new Dictionary<string, string>
            {
                ["source"] = "Threadline.Windows provider settings"
            }
        };

        var response = await _httpClient.PostAsJsonAsync($"providers/{providerName}/credential", request, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SaveLocalProviderAsync(string providerName, string baseUrl, string defaultModel, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            providerName,
            authType = "LocalEndpoint",
            baseUrl,
            defaultModel,
            status = "Ready",
            metadata = new Dictionary<string, string>
            {
                ["source"] = "Threadline.Windows provider settings"
            }
        };

        var response = await _httpClient.PostAsJsonAsync("providers", request, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Threadline service returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Threadline service returned {(int)response.StatusCode}: {body}");
    }
}

public sealed class ThreadlineEndpointNotFoundException : InvalidOperationException
{
    public ThreadlineEndpointNotFoundException(string message) : base(message)
    {
    }
}

public sealed record ServiceHealth(string Status, string Service, string Storage, bool AuthRequired, int MaxContextCharacters);
public sealed record ThreadlineDoctorReportDto(string Readiness, DateTimeOffset CreatedAt, IReadOnlyList<ThreadlineDoctorCheckDto> Checks, IReadOnlyList<ThreadlineCapabilityDto>? Capabilities, IReadOnlyList<ThreadlineActionDefinitionDto>? Actions);
public sealed record ThreadlineDoctorCheckDto(string Id, string DisplayName, string Status, string Detail, string? Remediation, IReadOnlyDictionary<string, string>? Metadata);
public sealed record ThreadlineCapabilityDto(string Id, string Category, string DisplayName, string Status, string Description, IReadOnlyDictionary<string, string>? Metadata);
public sealed record ThreadlineActionDefinitionDto(string Id, string Kind, string DisplayName, string Description, string? RequiredCapabilityId, bool RequiresActiveSession, bool RequiresActiveWorkThread);
public sealed record ProviderTestResultDto(string ProviderName, bool Success, string Status, string Detail, long DurationMs, string? Model, IReadOnlyDictionary<string, string>? Metadata);
public sealed record ThreadlineSessionDto(string Id, string Name, string Status, string? ActiveProvider);
public sealed record WindowSnapshotDto(string Id, string ApplicationName, string ProcessName, string WindowTitle, int? ProcessId, string? ExecutablePath, string? Uri, bool IsForeground);
public sealed record WindowAttachmentDto(string Id, string SessionId, WindowSnapshotDto Snapshot, string Status, DateTimeOffset AttachedAt, DateTimeOffset? DetachedAt);
public sealed record ContextPreviewDto(string RedactedContent, bool WillBeStored, bool RequiresExplicitApproval, string ConsentState, IReadOnlyList<string> Warnings);
public sealed record ContextEventDto(string Id, string SessionId, string Source, string ContextType, string Content);
public sealed record LlmMessageDto(string Role, string Content);
public sealed record AskResponseDto(string Answer, IReadOnlyList<LlmMessageDto>? Messages);
public sealed record WindowActionDto(string Id, string SessionId, string? AttachmentId, string Kind, string Description, string Payload, string Risk, string Status, bool RequiresApproval, string? ResultMessage);
