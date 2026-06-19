namespace Threadline.Windows.Services;

public sealed class ActiveWindowContentResolver
{
    private readonly BrowserExtensionContextProvider _browserProvider = new();
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly ContextSummarizer _contextSummarizer = new();
    private readonly ActiveWindowDiagnostics _diagnostics = new();
    private readonly FileBackedTextResolver _fileResolver = new();
    private readonly ProcessIntelligenceService _processIntelligence = new();

    public async Task<SummarizedContext> ResolveAsync(string sessionId, ThreadlineTarget target, CancellationToken cancellationToken = default)
    {
        var process = _processIntelligence.Inspect(target);

        if (target.Kind == ThreadlineTargetKind.BrowserTab)
        {
            var browser = await _browserProvider.TryGetLatestAsync(sessionId, target, cancellationToken);
            return browser is null
                ? WithDiagnostics(MissingProviderContext(target, process, "No browser page data is available in this session yet.", "Use the browser extension to send the page or selected text to ThreadlineAI.", ContextConfidence.Low), target, process, "browser-provider-missing")
                : WithDiagnostics(AddRoute(browser, target, process, "browser-extension", "app-specific provider", ContextConfidence.High), target, process, "browser-extension");
        }

        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase))
        {
            var fileBacked = TryResolveNotepadFileBacked(target, process);
            return fileBacked is not null
                ? WithDiagnostics(fileBacked, target, process, "file-resolver")
                : WithDiagnostics(NotepadNeedsProviderContext(target, process, _diagnostics.Inspect(target)), target, process, "notepad-title-only");
        }

        if (target.CanReadBody)
        {
            var nativeResult = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
            var nativeSummary = AddRoute(
                _contextSummarizer.SummarizeNativeUi(nativeResult),
                target,
                process,
                "native-ui",
                "ui automation fallback",
                nativeResult.Success ? ContextConfidence.Medium : ContextConfidence.Low);

            if (nativeResult.Success && !string.IsNullOrWhiteSpace(nativeResult.Content))
            {
                return WithDiagnostics(nativeSummary, target, process, "native-ui");
            }
        }

        var fileResolver = TryResolveGenericFileBacked(target, process);
        if (fileResolver is not null)
        {
            return WithDiagnostics(fileResolver, target, process, "file-resolver");
        }

        return WithDiagnostics(ScreenshotFallbackContext(target, process), target, process, "screenshot-fallback-placeholder");
    }

    private SummarizedContext? TryResolveNotepadFileBacked(ThreadlineTarget target, ProcessIntelligence process)
    {
        var result = _fileResolver.TryResolve(target.Title);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text)) return null;

        var summary = _contextSummarizer.SummarizePlainText(target.Title, "notepad-file-backed", result.Text);
        var details = new List<string>
        {
            "Resolver route: file-backed Notepad tab",
            "Provider selected: notepad-file-backed",
            "Confidence: high when exact file match is unique",
            $"Target: {target}",
            $"File path: {result.Path}"
        };
        details.AddRange(summary.KeyDetails);

        return new SummarizedContext(
            summary.Title,
            summary.Source,
            summary.Summary,
            details,
            result.Warnings,
            summary.RawPreview,
            ContextConfidence.High,
            process);
    }

    private SummarizedContext? TryResolveGenericFileBacked(ThreadlineTarget target, ProcessIntelligence process)
    {
        var result = _fileResolver.TryResolve(target.Title);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text)) return null;

        var summary = _contextSummarizer.SummarizePlainText(target.Title, "file-backed-document", result.Text);
        var details = new List<string>
        {
            "Resolver route: file/document resolver",
            "Provider selected: file-backed-document",
            $"Target: {target}",
            $"File path: {result.Path}"
        };
        details.AddRange(summary.KeyDetails);

        return new SummarizedContext(
            summary.Title,
            summary.Source,
            summary.Summary,
            details,
            result.Warnings,
            summary.RawPreview,
            ContextConfidence.High,
            process);
    }

    private static SummarizedContext NotepadNeedsProviderContext(ThreadlineTarget target, ProcessIntelligence process, IReadOnlyList<string> diagnostics)
    {
        var details = new List<string> { target.ToString(), "Resolver route: app-specific provider required", "Provider selected: notepad-active-document-needed", "Confidence: Low" };
        details.AddRange(diagnostics);
        return new SummarizedContext(
            target.Title,
            "notepad-active-document-needed",
            "Threadline can identify this Notepad tab, but active document body capture is not safely resolved yet.",
            details,
            ["Native UI body text is tab-ambiguous for modern Notepad. File-backed resolution needs a unique exact saved-file match."],
            target.Window.ToDisplayText(),
            ContextConfidence.Low,
            process);
    }

    private static SummarizedContext MissingProviderContext(ThreadlineTarget target, ProcessIntelligence process, string summary, string warning, ContextConfidence confidence) =>
        new(
            target.Title,
            target.ProviderKey,
            summary,
            [target.ToString(), $"Resolver route: {target.ProviderKey}", $"Confidence: {confidence}"],
            [warning],
            target.Window.ToDisplayText(),
            confidence,
            process);

    private static SummarizedContext ScreenshotFallbackContext(ThreadlineTarget target, ProcessIntelligence process) =>
        new(
            target.Title,
            "screenshot-fallback-placeholder",
            "Threadline could not extract reliable provider, UI Automation, or file-backed text. Screenshot/OCR is the fallback route, but OCR and layout analysis are not enabled in this build. Based only on visible window metadata, Threadline should answer cautiously.",
            [target.ToString(), "Resolver route: screenshot fallback", "Capture method: screenshot placeholder", "Confidence: Low"],
            ["Screenshot capture, OCR, image extraction, and layout analysis are planned fallback stages. This build exposes the route without pretending the visual content has been read."],
            target.Window.ToDisplayText(),
            ContextConfidence.Low,
            process);

    private static SummarizedContext AddRoute(SummarizedContext context, ThreadlineTarget target, ProcessIntelligence process, string provider, string route, ContextConfidence confidence)
    {
        var details = new List<string> { $"Resolver route: {route}", $"Provider selected: {provider}", $"Confidence: {confidence}", $"Target: {target}" };
        details.AddRange(context.KeyDetails);
        return new SummarizedContext(context.Title, context.Source, context.Summary, details, context.Warnings, context.RawPreview, confidence, process);
    }

    private static SummarizedContext WithDiagnostics(SummarizedContext context, ThreadlineTarget target, ProcessIntelligence process, string method)
    {
        var diagnostics = new CaptureDiagnostics(
            target.Window.WindowTitle ?? target.Title,
            target.Window.ProcessName ?? "Unknown",
            target.Window.ProcessId,
            method,
            string.IsNullOrWhiteSpace(context.RawPreview) ? 0 : context.RawPreview.Length,
            0,
            context.Summary.Length,
            context.Confidence,
            context.KeyDetails);

        return context with { Process = process, Diagnostics = diagnostics };
    }
}
