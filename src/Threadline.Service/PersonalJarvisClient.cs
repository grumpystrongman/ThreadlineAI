using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Threadline.Service;

public sealed record JarvisApiResponse(HttpStatusCode StatusCode, JsonElement Payload)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

public sealed class PersonalJarvisClient
{
    private readonly HttpClient _httpClient;
    private readonly JarvisRuntimeOptions _options;

    public PersonalJarvisClient(HttpClient httpClient, JarvisRuntimeOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient.BaseAddress = _options.BaseAddress;
        _httpClient.Timeout = _options.RequestTimeout;
    }

    public JarvisRuntimeOptions Options => _options;

    public Task<JarvisApiResponse> ProbeAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/missions?limit=1"), cancellationToken);

    public Task<JarvisApiResponse> DispatchMissionAsync(
        string prompt,
        string language = "en",
        bool confirmed = false,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, "api/missions/dispatch", new
        {
            prompt,
            language,
            confirmed
        }, cancellationToken);

    public Task<JarvisApiResponse> GetMissionAsync(string missionId, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/missions/{Encode(missionId)}"), cancellationToken);

    public Task<JarvisApiResponse> GetMissionResultAsync(string missionId, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/missions/{Encode(missionId)}/result"), cancellationToken);

    public Task<JarvisApiResponse> GetMissionChangesAsync(string missionId, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/missions/{Encode(missionId)}/changes"), cancellationToken);

    public Task<JarvisApiResponse> CancelMissionAsync(string missionId, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, $"api/missions/{Encode(missionId)}/cancel"), cancellationToken);

    public Task<JarvisApiResponse> GetToolApprovalsAsync(string missionId, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/missions/{Encode(missionId)}/tool-approvals"), cancellationToken);

    public Task<JarvisApiResponse> ApproveToolCallAsync(
        string missionId,
        string traceId,
        CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(
            HttpMethod.Post,
            $"api/missions/{Encode(missionId)}/tool-approvals/{Encode(traceId)}/approve"), cancellationToken);

    public Task<JarvisApiResponse> DenyToolCallAsync(
        string missionId,
        string traceId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            HttpMethod.Post,
            $"api/missions/{Encode(missionId)}/tool-approvals/{Encode(traceId)}/deny",
            new { reason = string.IsNullOrWhiteSpace(reason) ? "user_denied" : reason.Trim() },
            cancellationToken);

    private async Task<JarvisApiResponse> SendJsonAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JarvisApiResponse> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            request.Dispose();
            return JsonResponse(HttpStatusCode.ServiceUnavailable, new
            {
                enabled = false,
                reachable = false,
                status = "disabled",
                error = "PersonalJarvis runtime bridge is disabled."
            });
        }

        try
        {
            using (request)
            using (var response = await _httpClient.SendAsync(request, cancellationToken))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                JsonElement payload;
                if (string.IsNullOrWhiteSpace(body))
                {
                    payload = JsonSerializer.SerializeToElement(new { });
                }
                else
                {
                    try
                    {
                        payload = JsonSerializer.Deserialize<JsonElement>(body);
                    }
                    catch (JsonException)
                    {
                        payload = JsonSerializer.SerializeToElement(new
                        {
                            error = "PersonalJarvis returned a non-JSON response.",
                            detail = body.Length <= 2000 ? body : body[..2000]
                        });
                    }
                }

                return new JarvisApiResponse(response.StatusCode, payload);
            }
        }
        catch (HttpRequestException ex)
        {
            return JsonResponse(HttpStatusCode.ServiceUnavailable, new
            {
                enabled = true,
                reachable = false,
                status = "unreachable",
                error = "PersonalJarvis is not reachable.",
                detail = ex.Message,
                baseAddress = _options.BaseAddress.ToString()
            });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JsonResponse(HttpStatusCode.GatewayTimeout, new
            {
                enabled = true,
                reachable = false,
                status = "timeout",
                error = "PersonalJarvis timed out.",
                detail = $"No response within {_options.RequestTimeout.TotalSeconds:0} seconds.",
                baseAddress = _options.BaseAddress.ToString()
            });
        }
    }

    private static JarvisApiResponse JsonResponse(HttpStatusCode statusCode, object value) =>
        new(statusCode, JsonSerializer.SerializeToElement(value));

    private static string Encode(string value) => Uri.EscapeDataString(value?.Trim() ?? string.Empty);
}
