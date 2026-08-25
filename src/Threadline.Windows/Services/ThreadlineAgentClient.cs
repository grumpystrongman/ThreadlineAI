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
        PropertyNameCaseInsensitive = true,
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

    public async Task<IReadOnlyList<JarvisToolApprovalDto>> GetMissionToolApprovalsAsync(string missionId, CancellationToken cancellationToken = default)
    {
        var path = $"v1/jarvis/missions/{Uri.EscapeDataString(missionId)}/tool-approvals";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) return Array.Empty<JarvisToolApprovalDto>();
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Threadline Jarvis tool-approval endpoint returned {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("approvals", out var approvals) || approvals.ValueKind != JsonValueKind.Array)
            return Array.Empty<JarvisToolApprovalDto>();

        var result = new List<JarvisToolApprovalDto>();
        foreach (var item in approvals.EnumerateArray())
        {
            result.Add(new JarvisToolApprovalDto(
                TraceId: ReadString(item, "trace_id") ?? string.Empty,
                MissionId: ReadString(item, "mission_id") ?? missionId,
                WorkerId: ReadString(item, "worker_id"),
                ToolName: ReadString(item, "tool_name") ?? "unknown tool",
                RiskTier: ReadString(item, "risk_tier") ?? "ask",
                Reason: ReadString(item, "reason") ?? "risk_tier",
                ArgsPreview: ReadString(item, "args_preview") ?? "",
                RequestedAtNs: ReadLong(item, "requested_at_ns"),
                ExpiresAtNs: ReadLong(item, "expires_at_ns")));
        }
        return result;
    }

    public Task ApproveMissionToolAsync(string missionId, string traceId, CancellationToken cancellationToken = default) =>
        PostApprovalAsync(missionId, traceId, approve: true, reason: null, cancellationToken);

    public Task DenyMissionToolAsync(string missionId, string traceId, string reason = "user_denied", CancellationToken cancellationToken = default) =>
        PostApprovalAsync(missionId, traceId, approve: false, reason, cancellationToken);

    private async Task PostApprovalAsync(string missionId, string traceId, bool approve, string? reason, CancellationToken cancellationToken)
    {
        var escapedMission = Uri.EscapeDataString(missionId);
        var escapedTrace = Uri.EscapeDataString(traceId);
        var path = $"v1/jarvis/missions/{escapedMission}/tool-approvals/{escapedTrace}/{(approve ? "approve" : "deny")}";
        using HttpResponseMessage response = approve
            ? await _httpClient.PostAsync(path, content: null, cancellationToken)
            : await _httpClient.PostAsJsonAsync(path, new { reason = string.IsNullOrWhiteSpace(reason) ? "user_denied" : reason }, _jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Jarvis tool approval returned {(int)response.StatusCode}: {body}");
        }
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

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Null ? null : value.ToString();
    }

    private static long ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return 0;
        return value.TryGetInt64(out var parsed) ? parsed : long.TryParse(value.ToString(), out parsed) ? parsed : 0;
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

public sealed record JarvisToolApprovalDto(
    string TraceId,
    string MissionId,
    string? WorkerId,
    string ToolName,
    string RiskTier,
    string Reason,
    string ArgsPreview,
    long RequestedAtNs,
    long ExpiresAtNs);
