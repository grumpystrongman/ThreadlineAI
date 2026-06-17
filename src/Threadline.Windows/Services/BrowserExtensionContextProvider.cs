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

        var lines = browserEvent.Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var extraction = lines.FirstOrDefault(line => line.StartsWith("Extraction:", StringComparison.OrdinalIgnoreCase)) ?? "Extraction: browser-extension";
        var title = lines.FirstOrDefault(line => line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) ?? $"Title: {target.Title}";
        var url = lines.FirstOrDefault(line => line.StartsWith("URL:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var preview = BuildPreview(lines);
        var keyDetails = new List<string> { title, url, extraction, preview }.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        return new SummarizedContext(
            target.Title,
            "browser-extension",
            "Threadline found browser extension page context for this session.",
            keyDetails,
            [],
            browserEvent.Content);
    }

    private static string BuildPreview(IReadOnlyList<string> lines)
    {
        var contentLines = lines
            .Where(line => !line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("Extraction:", StringComparison.OrdinalIgnoreCase))
            .Take(18)
            .ToList();

        if (contentLines.Count == 0) return "Preview: browser context was stored, but no text excerpt was available.";
        var preview = string.Join(" ", contentLines);
        if (preview.Length > 1600) preview = preview[..1600] + "...";
        return "Preview: " + DecodeCommonEntities(preview);
    }

    private static string DecodeCommonEntities(string value) => value
        .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
        .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
        .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
        .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
        .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase)
        .Replace("&ndash;", "–", StringComparison.OrdinalIgnoreCase)
        .Replace("&mdash;", "—", StringComparison.OrdinalIgnoreCase);
}
