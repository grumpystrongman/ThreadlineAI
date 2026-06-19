namespace Threadline.Windows.Services;

public sealed class ActiveWindowContentResolver
{
    private readonly BrowserExtensionContextProvider _browserProvider = new();
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly ContextSummarizer _contextSummarizer = new();
    private readonly ActiveWindowDiagnostics _diagnostics = new();
    private readonly FileBackedTextResolver _fileResolver = new();
    private readonly ProcessIntelligenceInspector _processIntelligenceInspector = new();

    public async Task<SummarizedContext> ResolveAsync(string sessionId, ThreadlineTarget target, CancellationToken cancellationToken = default)
    {
        var process = _processIntelligenceInspector.Inspect(target);

        // 1. Provider first. Do not fall back to screenshots or generic native UI before checking the app-aware path.
        if (target.Kind == ThreadlineTargetKind.BrowserTab)
        {
            var browser = await _browserProvider.TryGetLatestAsync(sessionId, target, cancellationToken);
            if (browser is not null)
            {
                return AttachDiagnostics(browser, target, process, "Browser Provider", ContextConfidence.High);
            }
        }

        // 2. Document/File resolver before unsafe native body capture for known document-style targets.
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase))
        {
            var fileBacked = TryResolveFileBackedText(target, process);
            if (fileBacked is not null)
            {
                return fileBacked;
            }

            return AttachDiagnostics(NotepadNeedsProviderContext(target, _diagnostics.Inspect(target)), target, process, "Notepad/File Resolver", ContextConfidence.Low);
        }

        // 3. UI Automation for generic readable apps.
        if (target.CanReadBody)
        {
            var nativeResult = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
            var nativeSummary = _contextSummarizer.SummarizeNativeUi(nativeResult);
            var confidence = nativeResult.Success && !string.IsNullOrWhiteSpace(nativeResult.Content)
                ? nativeSummary.Confidence
                : ContextConfidence.Low;
            return AttachDiagnostics(nativeSummary, target, process, "UI Automation", confidence, nativeResult.Content.Length);
        }

        // 4. Screenshot fallback seam. It must be honest if OCR/vision is not available.
        var screenshotFallback = _contextSummarizer.SummarizeScreenshotFallback(target,
        [
            "Provider and UI Automation did not produce body text.",
            "Screenshot fallback reached, but OCR/vision extraction is not wired in this build."
        ]);
        return AttachDiagnostics(screenshotFallback, target, process, "Screenshot Fallback", ContextConfidence.None);
    }

    private SummarizedContext? TryResolveFileBackedText(ThreadlineTarget target, ProcessIntelligence process)
    {
        var result = _fileResolver.TryResolve(target.Title);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text)) return null;

        var summary = _contextSummarizer.SummarizePlainText(target.Title, "notepad-file-backed", result.Text);
        var details = new List<string>
        {
            "Resolver route: document/file resolver",
            "Provider selected: notepad-file-backed",
            "Confidence: HIGH when exact file match is unique",
            $"Target: {target}",
            $"File path: {result.Path}"
        };
        details.AddRange(summary.KeyDetails);

        var context = new SummarizedContext(
            summary.Title,
            summary.Source,
            summary.Summary,
            details,
            result.Warnings,
            summary.RawPreview,
            ContextConfidence.High);

        return AttachDiagnostics(context, target, process, "Document/File Resolver", ContextConfidence.High, result.Text.Length);
    }

    private static SummarizedContext NotepadNeedsProviderContext(ThreadlineTarget target, IReadOnlyList<string> diagnostics)
    {
        var details = new List<string>
        {
            target.ToString(),
            "Resolver route: document/file resolver",
            "Provider selected: notepad-active-document-needed",
            "Confidence: LOW"
        };
        details.AddRange(diagnostics);
        return new SummarizedContext(
            target.Title,
            "notepad-active-document-needed",
            "Threadline can identify this Notepad tab, but active document body capture is not safely resolved yet. It will not pretend it knows the body text.",
            details,
            ["Native UI body text is tab-ambiguous for modern Notepad. File-backed resolution needs a unique exact saved-file match."],
            target.Window.ToDisplayText(),
            ContextConfidence.Low);
    }

    private static SummarizedContext AttachDiagnostics(
        SummarizedContext context,
        ThreadlineTarget target,
        ProcessIntelligence process,
        string captureMethod,
        ContextConfidence confidence,
        int? charactersExtracted = null,
        int imagesFound = 0)
    {
        var details = new List<string>
        {
            "Resolver route: provider → UI Automation → document/file resolver → screenshot fallback",
            $"Process intelligence: {process.AppType}",
            $"HWND: {process.HandleDisplay}",
            $"Executable: {process.ExecutablePath}",
            $"Capture method used: {captureMethod}",
            $"Confidence: {confidence.ToString().ToUpperInvariant()}",
            $"Target: {target}"
        };
        details.AddRange(context.KeyDetails);

        var diagnosticSnapshot = new CaptureDiagnosticsSnapshot(
            target.Window.WindowTitle ?? target.Title,
            process.HandleDisplay,
            target.Window.ProcessName ?? "Unknown",
            target.Window.ProcessId,
            target.Window.ExecutablePath ?? "Unknown",
            captureMethod,
            charactersExtracted ?? context.RawPreview.Length,
            imagesFound,
            context.Summary.Length,
            confidence,
            details);

        return new SummarizedContext(
            context.Title,
            context.Source,
            context.Summary,
            details,
            context.Warnings,
            context.RawPreview,
            confidence,
            diagnosticSnapshot);
    }
}
