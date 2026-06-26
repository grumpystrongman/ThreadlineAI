namespace Threadline.Windows.Services;

public sealed class ProcessIntelligenceService
{
    public ProcessIntelligence Inspect(ThreadlineTarget target)
    {
        var window = target.Window;
        var processName = window.ProcessName ?? "Unknown";
        var appType = Classify(processName, window.WindowTitle ?? target.Title);
        var methods = BuildCaptureMethods(target, appType);

        return new ProcessIntelligence(
            window.WindowTitle ?? target.Title,
            window.Handle,
            window.ProcessId,
            processName,
            window.ExecutablePath,
            null,
            null,
            appType,
            methods);
    }

    private static IReadOnlyList<CaptureMethodAvailability> BuildCaptureMethods(ThreadlineTarget target, ActiveAppType appType)
    {
        var methods = new List<CaptureMethodAvailability>
        {
            new(
                CaptureMethodKind.Provider,
                ProviderDisplayName(target.ProviderKey),
                target.Kind == ThreadlineTargetKind.BrowserTab || target.ProviderKey.Equals("browser-extension", StringComparison.OrdinalIgnoreCase) || target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase),
                ProviderNotes(target)),
            new(
                CaptureMethodKind.FileResolver,
                "Document/File Resolver",
                target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase) || appType == ActiveAppType.TextEditor,
                "Available when the visible document title can be mapped to a unique local file. Threadline will not guess across multiple matches."),
            new(
                CaptureMethodKind.UiAutomation,
                "UI Automation",
                target.CanReadBody || appType is ActiveAppType.OfficeDocument or ActiveAppType.MailClient or ActiveAppType.PdfViewer or ActiveAppType.AnalyticsTool or ActiveAppType.DesktopApp,
                "Windows accessibility text can be attempted, but confidence depends on what the app exposes. Browser and tabbed-editor body text should use stronger providers."),
            new(
                CaptureMethodKind.ClipboardSelection,
                "Clipboard/Selection",
                false,
                "Consent-gated. Threadline must not read clipboard or selected text unless the user explicitly allows it for that capture."),
            new(
                CaptureMethodKind.Screenshot,
                "Screenshot/Vision",
                true,
                "Implemented as a last-resort fallback, but blocked unless the user gives visible one-time approval and the app is not denied."),
            new(
                CaptureMethodKind.Ocr,
                "OCR",
                true,
                "Implemented after approved screenshot capture. OCR text is redacted before prompt/provider handoff where possible."),
            new(
                CaptureMethodKind.ImageExtraction,
                "Image Extraction",
                false,
                "Raw image provider handoff is not implemented yet. Redacted OCR/summary text is sent instead."),
            new(
                CaptureMethodKind.LayoutAnalysis,
                "Layout Analysis",
                false,
                "Only a local text vision summary is produced. Full visual layout analysis remains future work."),
            new(
                CaptureMethodKind.TitleProcessFallback,
                "Title/process fallback",
                true,
                "Always available. This is metadata only and must say when Threadline lacks the real page or document body.")
        };

        return methods;
    }

    private static string ProviderDisplayName(string providerKey) => providerKey switch
    {
        "browser-extension" => "Browser Provider",
        "notepad-tabs" => "Notepad Provider",
        "native-ui" => "Native Window Provider",
        _ => providerKey
    };

    private static string ProviderNotes(ThreadlineTarget target)
    {
        if (target.Kind == ThreadlineTargetKind.BrowserTab) return "Preferred for browser page/tab context.";
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase)) return "Can identify tabs; body text still needs file or active-document resolution.";
        return "No app-specific provider is registered for this target yet.";
    }

    private static ActiveAppType Classify(string processName, string title)
    {
        if (IsAny(processName, "chrome", "msedge", "firefox")) return ActiveAppType.Browser;
        if (IsAny(processName, "notepad", "code")) return ActiveAppType.TextEditor;
        if (IsAny(processName, "winword", "excel", "powerpnt") || title.Contains("Word", StringComparison.OrdinalIgnoreCase)) return ActiveAppType.OfficeDocument;
        if (IsAny(processName, "outlook", "olk")) return ActiveAppType.MailClient;
        if (title.Contains(".pdf", StringComparison.OrdinalIgnoreCase) || processName.Contains("acrobat", StringComparison.OrdinalIgnoreCase)) return ActiveAppType.PdfViewer;
        if (processName.Contains("powerbi", StringComparison.OrdinalIgnoreCase) || title.Contains("Power BI", StringComparison.OrdinalIgnoreCase)) return ActiveAppType.AnalyticsTool;
        if (IsAny(processName, "windowsterminal", "powershell", "pwsh", "cmd")) return ActiveAppType.Terminal;
        return ActiveAppType.DesktopApp;
    }

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}
