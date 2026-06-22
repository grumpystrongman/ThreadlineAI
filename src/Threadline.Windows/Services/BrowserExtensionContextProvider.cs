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
        var response = await _httpClient.GetAsync($"sessions/{sessionId}/events/recent?take=50", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var events = await response.Content.ReadFromJsonAsync<List<ContextEventDto>>(_jsonOptions, cancellationToken) ?? [];
        var browserEvents = events
            .Where(item => item.Source.Equals("Browser", StringComparison.OrdinalIgnoreCase) || item.ContextType.Contains("browser", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var browserEvent = browserEvents.FirstOrDefault(item => item.ContextType.Equals("browser-selection", StringComparison.OrdinalIgnoreCase))
            ?? browserEvents.FirstOrDefault();

        if (browserEvent is null) return null;

        var lines = browserEvent.Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var extraction = lines.FirstOrDefault(line => line.StartsWith("Extraction:", StringComparison.OrdinalIgnoreCase)) ?? "Extraction: browser-extension";
        var title = lines.FirstOrDefault(line => line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) ?? $"Title: {target.Title}";
        var url = lines.FirstOrDefault(line => line.StartsWith("URL:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        var preview = BuildPreview(lines);
        var contextType = $"Context type: {browserEvent.ContextType}";
        var keyDetails = new List<string> { title, url, extraction, contextType, preview }.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        var isSelection = browserEvent.ContextType.Contains("selection", StringComparison.OrdinalIgnoreCase) || extraction.Contains("selection", StringComparison.OrdinalIgnoreCase);
        var captureKind = isSelection ? ContextCaptureKind.SelectedText : ContextCaptureKind.PageText;
        var captured = new List<string>
        {
            isSelection ? "Browser selected text supplied by extension." : "Browser page text supplied by extension.",
            title,
            string.IsNullOrWhiteSpace(url) ? "URL was not supplied by the extension payload." : url,
            $"Stored browser event: {browserEvent.ContextType}",
            $"Captured characters: {browserEvent.Content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        };
        var notCaptured = new List<string>
        {
            "Native browser page body was not read through UI Automation.",
            "The Windows client has not yet verified this event against the active tab URL; it is the latest browser context event in the session."
        };
        if (isSelection)
        {
            notCaptured.Add("Full page text was not captured unless it was also included by the extension event.");
        }
        else
        {
            notCaptured.Add("Explicit selected text was not captured unless it was also included by the extension event.");
        }

        var receipt = new ContextReceipt(
            "browser-extension",
            ContextConfidence.High,
            captureKind,
            captured,
            notCaptured,
            false,
            isSelection
                ? "Threadline has selected text from the browser extension."
                : "Threadline has page text from the browser extension.",
            []);

        return new SummarizedContext(
            target.Title,
            "browser-extension",
            isSelection
                ? "Threadline found browser extension selected-text context for this session."
                : "Threadline found browser extension page context for this session.",
            keyDetails,
            [],
            browserEvent.Content,
            ContextConfidence.High,
            Receipt: receipt);
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
