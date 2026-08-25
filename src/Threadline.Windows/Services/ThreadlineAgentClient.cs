using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Threadline.Windows.Services;

public sealed class ThreadlineAgentClient
{
    private readonly Uri _baseAddress;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ThreadlineAgentClient(string baseUrl = "http://localhost:5057", string? localAccessToken = null)
    {
        _baseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient = new HttpClient { BaseAddress = _baseAddress };
        ThreadlineLocalApiAccess.ApplyTo(_httpClient, localAccessToken);
    }

    public async Task<AgentAskResponseDto> AskAsync(
        string sessionId,
        string question,
        string? currentWindow,
        int takeRecentEvents = 20,
        string route = "Auto",
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        var path = $"sessions/{Uri.EscapeDataString(sessionId)}/agent";
        var response = await _httpClient.PostAsJsonAsync(path, new
        {
            question,
            currentWindow,
            takeRecentEvents,
            route,
            confirmed
        }, _jsonOptions, cancellationToken);

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted or HttpStatusCode.Conflict)
        {
            return await response.Content.ReadFromJsonAsync<AgentAskResponseDto>(_jsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Threadline agent endpoint returned an empty response.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Threadline agent endpoint returned {(int)response.StatusCode}: {body}");
    }

    public async Task<JarvisMissionDetailDto> GetMissionAsync(string missionId, CancellationToken cancellationToken = default)
    {
        var path = $"v1/jarvis/missions/{Uri.EscapeDataString(missionId)}";
        return await GetRequiredAsync<JarvisMissionDetailDto>(path, cancellationToken);
    }

    public async Task<JarvisMissionResultDto> GetMissionResultAsync(string missionId, CancellationToken cancellationToken = default)
    {
        var path = $"v1/jarvis/missions/{Uri.EscapeDataString(missionId)}/result";
        return await GetRequiredAsync<JarvisMissionResultDto>(path, cancellationToken);
    }

    public async Task<JarvisMissionChangesDto> GetMissionChangesAsync(string missionId, CancellationToken cancellationToken = default)
    {
        var path = $"v1/jarvis/missions/{Uri.EscapeDataString(missionId)}/changes";
        return await GetRequiredAsync<JarvisMissionChangesDto>(path, cancellationToken);
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Threadline Jarvis endpoint returned {(int)response.StatusCode}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Threadline Jarvis endpoint returned an empty response.");
    }
}

public sealed record AgentAskResponseDto(
    string Route,
    string Reason,
    string Confidence,
    AgentDirectResponseDto? DirectResponse,
    string? MissionId,
    bool RequiresConfirmation,
    JsonElement? MissionPayload,
    int? UpstreamStatusCode);

public sealed record AgentDirectResponseDto(
    string Answer,
    IReadOnlyList<LlmMessageDto>? Messages,
    string? ProviderName,
    string? Model,
    long DurationMs);

public sealed record JarvisMissionDetailDto(
    JarvisMissionDto Mission,
    JsonElement? Events,
    JsonElement? Verdicts,
    JsonElement? WorkerSnapshots);

public sealed record JarvisMissionDto(
    string Id,
    string State,
    string? Language,
    int Iteration,
    double CostUsd,
    long CreatedMs,
    long UpdatedMs);

public sealed record JarvisMissionResultDto(
    string MissionId,
    string State,
    string? Language,
    string? Prompt,
    string? TerminalEvent,
    string? Summary,
    string? ResultUri,
    string? Reason,
    JsonElement? Artifacts,
    int ArtifactCount,
    bool Truncated);

public sealed record JarvisMissionChangesDto(
    string MissionId,
    JsonElement? Files,
    JsonElement? Changes);
