using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Threadline.Windows.Services;

public sealed class BrowserExtensionContextProvider
{
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri("http://localhost:5057/") };
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<SummarizedContext?> TryGetLatestAsync(string sessionId, ThreadlineTarget target, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"sessions/{sessionId}/events/recent?take=30", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var events = await response.Content.ReadFromJsonAsync<List<ContextEventDto>>(_jsonOptions, cancellationToken) ?? [];
        var browserEvent = events
            .Where(item => item.Source.Equals("Browser", StringComparison.OrdinalIgnoreCase) || item.ContextType.Contains("browser", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (browserEvent is null) return null;

        return new SummarizedContext(
            target.Title,
            "browser-extension",
            "Threadline found browser extension page context for this session.",
            browserEvent.Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(12).ToList(),
            [],
            browserEvent.Content);
    }
}
