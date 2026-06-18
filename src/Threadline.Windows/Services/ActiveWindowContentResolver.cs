namespace Threadline.Windows.Services;

public sealed class ActiveWindowContentResolver
{
    private readonly BrowserExtensionContextProvider _browserProvider = new();
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly ContextSummarizer _contextSummarizer = new();
    private readonly ActiveWindowDiagnostics _diagnostics = new();
    private static readonly string[] ChromeNoise =
    [
        "non client", "input sink", "system", "ime", "minimize", "maximize", "context help", "close",
        "application", "vertical", "horizontal", "scroll", "menu", "title bar", "toolbar", "status bar"
    ];

    public async Task<SummarizedContext> ResolveAsync(string sessionId, ThreadlineTarget target, CancellationToken cancellationToken = default)
    {
        if (target.Kind == ThreadlineTargetKind.BrowserTab)
        {
            var browser = await _browserProvider.TryGetLatestAsync(sessionId, target, cancellationToken);
            return browser is null
                ? MissingProviderContext(target, "No browser page data is available in this session yet.", "Use the browser extension to send the page or selected text to ThreadlineAI.")
                : AddRoute(browser, target, "browser-extension", "app-specific provider", "high");
        }

        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase))
        {
            var notepad = TryResolveNotepadGuardedNative(target);
            return notepad ?? NotepadNeedsProviderContext(target, _diagnostics.Inspect(target));
        }

        if (!target.CanReadBody)
        {
            return MissingProviderContext(target, target.Guidance, $"Provider confidence: {target.Confidence}.");
        }

        var nativeResult = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
        return AddRoute(_contextSummarizer.SummarizeNativeUi(nativeResult), target, "native-ui", "generic fallback", nativeResult.Success ? "medium" : "low");
    }

    private SummarizedContext? TryResolveNotepadGuardedNative(ThreadlineTarget target)
    {
        var native = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
        if (!native.Success || string.IsNullOrWhiteSpace(native.Content)) return null;
        if (native.Content.Length < 40) return null;

        var windowTitle = target.Window.WindowTitle ?? string.Empty;
        var titleMatches = windowTitle.Contains(target.Title, StringComparison.OrdinalIgnoreCase)
            || target.Title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase);
        if (!titleMatches && !target.IsActive) return null;

        var allLines = native.Content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var bodyLines = allLines
            .Select(CleanNativeLine)
            .Where(line => line.Length >= 3)
            .Where(line => !line.All(char.IsDigit))
            .Where(line => !ChromeNoise.Any(noise => line.Contains(noise, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bodyLines.Count == 0 || string.Join(" ", bodyLines).Length < 40)
        {
            return null;
        }

        var details = new List<string>
        {
            $"Resolver route: app-specific guarded native capture",
            $"Provider selected: notepad-guarded-native-ui",
            $"Confidence: guarded",
            $"Target: {target}",
            $"Native text length: {native.Content.Length}",
            $"Native raw line count: {allLines.Count}",
            $"Native body line count: {bodyLines.Count}"
        };
        details.AddRange(bodyLines.Take(12));

        return new SummarizedContext(
            target.Title,
            "notepad-guarded-native-ui",
            "Threadline captured likely Notepad document text through guarded native UI after filtering window chrome.",
            details,
            ["Guarded capture: Notepad body text came from Windows native UI, not a formal Notepad document API."],
            string.Join(Environment.NewLine, bodyLines));
    }

    private static string CleanNativeLine(string line)
    {
        var value = line.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var close = value.IndexOf(']');
            if (close >= 0 && close + 1 < value.Length)
            {
                value = value[(close + 1)..].Trim();
            }
        }

        return value.Trim(':', ' ');
    }

    private static SummarizedContext NotepadNeedsProviderContext(ThreadlineTarget target, IReadOnlyList<string> diagnostics)
    {
        var details = new List<string> { target.ToString(), "Resolver route: app-specific provider required", "Provider selected: notepad-active-document-needed", "Confidence: title-only" };
        details.AddRange(diagnostics);
        return new SummarizedContext(
            target.Title,
            "notepad-active-document-needed",
            "Threadline can identify this Notepad tab, but active document body capture is not implemented yet.",
            details,
            ["Generic native UI capture is tab-ambiguous for modern Notepad."],
            target.Window.ToDisplayText());
    }

    private static SummarizedContext MissingProviderContext(ThreadlineTarget target, string summary, string warning) =>
        new(
            target.Title,
            target.ProviderKey,
            summary,
            [target.ToString(), $"Resolver route: {target.ProviderKey}", $"Confidence: {target.Confidence}"],
            [warning],
            target.Window.ToDisplayText());

    private static SummarizedContext AddRoute(SummarizedContext context, ThreadlineTarget target, string provider, string route, string confidence)
    {
        var details = new List<string> { $"Resolver route: {route}", $"Provider selected: {provider}", $"Confidence: {confidence}", $"Target: {target}" };
        details.AddRange(context.KeyDetails);
        return new SummarizedContext(context.Title, context.Source, context.Summary, details, context.Warnings, context.RawPreview);
    }
}
