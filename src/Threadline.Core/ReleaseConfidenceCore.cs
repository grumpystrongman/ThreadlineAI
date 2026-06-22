using System.Text.Json;

namespace Threadline.Core;

public sealed record ContextSourceClassification(
    ContextSource Source,
    string Reason,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed class ContextSourceClassifier
{
    public ContextSourceClassification Classify(ContextEvent contextEvent)
    {
        ArgumentNullException.ThrowIfNull(contextEvent);
        return Classify(
            contextEvent.ApplicationName,
            contextEvent.ProcessName,
            contextEvent.WindowTitle,
            contextEvent.Uri,
            contextEvent.ContextType,
            contextEvent.Metadata);
    }

    public ContextSourceClassification Classify(WindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Classify(
            snapshot.ApplicationName,
            snapshot.ProcessName,
            snapshot.WindowTitle,
            snapshot.Uri,
            snapshot.Metadata is not null && snapshot.Metadata.TryGetValue("nativeContext.providerName", out var provider)
                ? provider
                : "window-snapshot",
            snapshot.Metadata);
    }

    public ContextSourceClassification Classify(
        string? applicationName,
        string? processName,
        string? windowTitle,
        string? uri,
        string? contextType,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var normalizedMetadata = NormalizeMetadata(metadata);
        var process = processName ?? string.Empty;
        var application = applicationName ?? string.Empty;
        var title = windowTitle ?? string.Empty;
        var type = contextType ?? string.Empty;
        var location = uri ?? string.Empty;

        if (ContainsAny(type, "screenshot", "screen-capture") || HasMetadataFlag(normalizedMetadata, "capture.kind", "screenshot"))
        {
            return Result(ContextSource.Screenshot, "Context type identifies a screenshot capture.", normalizedMetadata);
        }

        if (ContainsAny(type, "ui-automation", "uia", "automation") || normalizedMetadata.Keys.Any(key => key.StartsWith("nativeContext.", StringComparison.OrdinalIgnoreCase)))
        {
            return Result(ContextSource.UiAutomation, "Native UI Automation metadata is present.", normalizedMetadata);
        }

        if (ContainsAny(type, "selection", "selected-text") || HasMetadataFlag(normalizedMetadata, "capture.kind", "selection"))
        {
            return Result(ContextSource.UserSelection, "Context type identifies a user selection.", normalizedMetadata);
        }

        if (LooksLikeBrowser(process, application, title, location) || HasMetadataFlag(normalizedMetadata, "adapterKind", AdapterKind.BrowserExtension.ToString()))
        {
            return Result(ContextSource.Browser, "Browser process, URL, or browser-extension adapter metadata detected.", normalizedMetadata);
        }

        if (ContainsAny(process, "pwsh", "powershell") || ContainsAny(application, "powershell"))
        {
            return Result(ContextSource.PowerShell, "PowerShell process detected.", normalizedMetadata);
        }

        if (ContainsAny(process, "windowsterminal", "terminal", "cmd", "bash", "zsh", "fish") || ContainsAny(application, "terminal", "command prompt"))
        {
            return Result(ContextSource.Terminal, "Terminal process detected.", normalizedMetadata);
        }

        if (!string.IsNullOrWhiteSpace(application) || !string.IsNullOrWhiteSpace(process) || !string.IsNullOrWhiteSpace(title))
        {
            return Result(ContextSource.ActiveWindow, "Window metadata is available but no richer source was detected.", normalizedMetadata);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            return Result(ContextSource.Manual, "Context type is present without app/window metadata.", normalizedMetadata);
        }

        return Result(ContextSource.Unknown, "Insufficient source metadata to classify context.", normalizedMetadata);
    }

    private static ContextSourceClassification Result(ContextSource source, string reason, IReadOnlyDictionary<string, string> metadata) =>
        new(source, reason, metadata);

    private static Dictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static bool HasMetadataFlag(IReadOnlyDictionary<string, string> metadata, string key, string value) =>
        metadata.TryGetValue(key, out var actual) && string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBrowser(string process, string application, string title, string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        || ContainsAny(process, "chrome", "msedge", "firefox", "brave", "browser")
        || ContainsAny(application, "chrome", "edge", "firefox", "brave", "browser")
        || ContainsAny(title, " - Google Chrome", " - Microsoft Edge", " - Mozilla Firefox");

    private static bool ContainsAny(string value, params string[] patterns) =>
        !string.IsNullOrWhiteSpace(value) && patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
}

public sealed record SidecarGeometryState(
    int X,
    int Y,
    int Width,
    int Height,
    bool IsAttached,
    DateTimeOffset SavedAt)
{
    public const int MinimumWidth = 320;
    public const int MinimumHeight = 240;

    public static SidecarGeometryState Create(int x, int y, int width, int height, bool isAttached, DateTimeOffset savedAt) =>
        new(x, y, Math.Max(MinimumWidth, width), Math.Max(MinimumHeight, height), isAttached, savedAt);

    public bool IsUsable => Width >= MinimumWidth && Height >= MinimumHeight;
}

public sealed class JsonSidecarGeometryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;

    public JsonSidecarGeometryStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Geometry store path is required.", nameof(path));
        }

        _path = path;
    }

    public async Task SaveAsync(SidecarGeometryState geometry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await using var file = File.Create(_path);
        await JsonSerializer.SerializeAsync(file, geometry, JsonOptions, cancellationToken);
    }

    public async Task<SidecarGeometryState?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;

        await using var file = File.OpenRead(_path);
        var geometry = await JsonSerializer.DeserializeAsync<SidecarGeometryState>(file, JsonOptions, cancellationToken);
        return geometry is null || !geometry.IsUsable ? null : geometry;
    }
}

public sealed record UiAutomationFakeWindow(
    string ApplicationName,
    string ProcessName,
    string WindowTitle,
    string ExtractedText,
    DateTimeOffset CapturedAt)
{
    public WindowSnapshot ToWindowSnapshot() =>
        WindowSnapshot.Create(
            CapturedAt,
            ApplicationName,
            ProcessName,
            WindowTitle,
            metadata: new Dictionary<string, string>
            {
                ["nativeContext.providerName"] = "Fake UI Automation",
                ["nativeContext.level"] = "FullText",
                ["nativeContext.levelDisplay"] = "UI Automation fake window",
                ["nativeContext.content"] = ExtractedText,
                ["nativeContext.guidance"] = "Deterministic fake-window context used by release confidence tests."
            });
}
