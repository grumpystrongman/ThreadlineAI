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
        ThreadlineLocalApiAccess.ApplyTo(_httpClient, localAccessToken);
    }

    public async Task<ServiceHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<ServiceHealth>("health", cancellationToken);

    public async Task<ThreadlineVersionInfoDto> GetVersionAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<ThreadlineVersionInfoDto>("version", cancellationToken);

    public async Task<ThreadlineDiagnosticsExportDto> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("diagnostics/export", null, cancellationToken);
        return await ReadRequiredAsync<ThreadlineDiagnosticsExportDto>(response, cancellationToken);
    }

    public async Task<ThreadlineLocalDataPlanDto> GetLocalDataClearPlanAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<ThreadlineLocalDataPlanDto>("local-data/clear-plan", cancellationToken);

    public async Task<ThreadlineLocalDataClearResultDto> ClearLocalDataAsync(bool includeDiagnostics = true, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("local-data/clear", new { confirmation = "CLEAR THREADLINE LOCAL DATA", includeDiagnostics }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<ThreadlineLocalDataClearResultDto>(response, cancellationToken);
    }

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
            ["build"] = "21-commercial-lifecycle"
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

    public async Task<IReadOnlyList<ScreenshotVisionPolicyDto>> GetScreenshotVisionPoliciesAsync(CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<List<ScreenshotVisionPolicyDto>>("privacy/screenshot-vision-policies", cancellationToken);

    public async Task SaveScreenshotVisionPolicyAsync(string appKey, string policy, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("privacy/screenshot-vision-policies", new { appKey, policy }, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteScreenshotVisionPolicyAsync(string appKey, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"privacy/screenshot-vision-policies/{Uri.EscapeDataString(appKey)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<TranscriptionResultDto> TranscribeAudioAsync(string audioFilePath, string? provider = null, string? language = null, bool translate = false, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("transcribe", new { audioFilePath, provider, language, translate }, _jsonOptions, cancellationToken);
        return await ReadRequiredAsync<TranscriptionResultDto>(response, cancellationToken);
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

public sealed record ServiceHealth(string Status, string Service, string Storage, bool AuthRequired, int MaxContextCharacters, string? ProductVersion = null, string? ServiceVersion = null, string? ApiCompatibility = null, string? ExpectedBrowserExtensionVersion = null);
public sealed record ThreadlineVersionInfoDto(string ProductName, string ProductVersion, string ServiceName, string ServiceAssemblyVersion, string EntryAssemblyVersion, string BuildChannel, string ApiCompatibility, string DatabaseSchemaVersion, string ExpectedBrowserExtensionVersion, DateTimeOffset GeneratedAt);
public sealed record ThreadlineDiagnosticsExportDto(string ExportPath, DateTimeOffset CreatedAt, ThreadlineLifecycleManifestDto Manifest);
public sealed record ThreadlineLifecycleManifestDto(ThreadlineVersionInfoDto Version, string Readiness, bool LocalOnlyMode, bool ApiTokenRequired, bool ApiTokenPresent, IReadOnlyList<string> CorsAllowedOrigins, string DatabasePath, string DiagnosticsRoot, string ContentRootPath, string EnvironmentName, IReadOnlyList<ThreadlineLocalDataTargetDto> LocalDataTargets, IReadOnlyList<string> Notes);
public sealed record ThreadlineLocalDataPlanDto(string ConfirmationPhrase, string Warning, IReadOnlyList<ThreadlineLocalDataTargetDto> Targets);
public sealed record ThreadlineLocalDataTargetDto(string Id, string DisplayName, string Kind, string Path, bool Exists);
public sealed record ThreadlineLocalDataClearResultDto(bool Success, string Message, IReadOnlyList<ThreadlineLocalDataRemovalDto> Removals);
public sealed record ThreadlineLocalDataRemovalDto(string Id, string Path, bool Removed, string Detail);
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
public sealed record ScreenshotVisionPolicyDto(string AppKey, string Policy);
public sealed record TranscriptionResultDto(string Transcript, string Provider, string? Language, long DurationMs, IReadOnlyDictionary<string, string>? Metadata);
