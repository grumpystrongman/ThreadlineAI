namespace Threadline.Windows.Services;

public enum ContextConfidence
{
    High,
    Medium,
    Low,
    None
}

public enum CaptureMethodKind
{
    Provider,
    UiAutomation,
    DocumentFileResolver,
    Screenshot,
    WindowTitleOnly
}

public sealed record CaptureMethodAvailability(
    CaptureMethodKind Method,
    string DisplayName,
    bool Available,
    string Guidance)
{
    public string StatusLine => Available ? $"✓ {DisplayName}" : $"• {DisplayName} — {Guidance}";
}

public sealed record ProcessIntelligence(
    string WindowTitle,
    nint WindowHandle,
    int? ProcessId,
    string ProcessName,
    string ExecutablePath,
    string ParentProcess,
    string AppType,
    IReadOnlyList<CaptureMethodAvailability> AvailableCaptureMethods)
{
    public string HandleDisplay => $"0x{WindowHandle.ToInt64():X8}";

    public string ToDisplayText()
    {
        var methods = AvailableCaptureMethods.Count == 0
            ? "None detected."
            : string.Join(Environment.NewLine, AvailableCaptureMethods.Select(method => method.StatusLine));

        return $"Window Title: {WindowTitle}\nHWND: {HandleDisplay}\nPID: {ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nExecutable: {ExecutablePath}\nParent Process: {ParentProcess}\nApp Type: {AppType}\n\nCapture Methods:\n{methods}";
    }
}

public sealed record CaptureDiagnosticsSnapshot(
    string WindowTitle,
    string WindowHandle,
    string ProcessName,
    int? ProcessId,
    string ExecutablePath,
    string CaptureMethodUsed,
    int CharactersExtracted,
    int ImagesFound,
    int SummarySize,
    ContextConfidence Confidence,
    IReadOnlyList<string> Details)
{
    public string ToDisplayText()
    {
        var details = Details.Count == 0
            ? "None."
            : string.Join(Environment.NewLine, Details.Select(detail => $"- {detail}"));

        return $"Window: {WindowTitle}\nHWND: {WindowHandle}\nProcess: {ProcessName}\nPID: {ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nExecutable: {ExecutablePath}\nCapture method used: {CaptureMethodUsed}\nChars extracted: {CharactersExtracted}\nImages found: {ImagesFound}\nSummary size: {SummarySize}\nConfidence: {Confidence.ToString().ToUpperInvariant()}\n\nDetails:\n{details}";
    }
}

public sealed class ProcessIntelligenceInspector
{
    public ProcessIntelligence Inspect(ThreadlineTarget target)
    {
        var window = target.Window;
        var processName = window.ProcessName ?? "Unknown";
        var processExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : processName + ".exe";
        var methods = BuildCaptureMethods(target, processName).ToList();

        return new ProcessIntelligence(
            window.WindowTitle ?? target.Title,
            window.Handle,
            window.ProcessId,
            processExe,
            window.ExecutablePath ?? "Unknown",
            "Unknown",
            DetectAppType(processName, target),
            methods);
    }

    private static IEnumerable<CaptureMethodAvailability> BuildCaptureMethods(ThreadlineTarget target, string processName)
    {
        var isBrowser = IsBrowser(processName) || target.Kind == ThreadlineTargetKind.BrowserTab;
        var isTextDocument = target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase);

        yield return new CaptureMethodAvailability(
            CaptureMethodKind.Provider,
            isBrowser ? "Browser Provider" : isTextDocument ? "File Provider" : "App Provider",
            isBrowser || isTextDocument,
            "No app-specific provider is registered yet.");

        yield return new CaptureMethodAvailability(
            CaptureMethodKind.UiAutomation,
            "UI Automation",
            target.CanReadBody || !isBrowser,
            "Native accessibility may expose only chrome or partial visible text.");

        yield return new CaptureMethodAvailability(
            CaptureMethodKind.DocumentFileResolver,
            "Document/File Resolver",
            isTextDocument,
            "Only enabled when the selected target can be mapped to a saved local file.");

        yield return new CaptureMethodAvailability(
            CaptureMethodKind.Screenshot,
            "Screenshot",
            true,
            "Fallback only; OCR/vision confidence must be reported honestly.");
    }

    private static string DetectAppType(string processName, ThreadlineTarget target)
    {
        if (target.Kind == ThreadlineTargetKind.BrowserTab || IsBrowser(processName)) return "Browser";
        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase)) return "Text document";
        if (processName.Contains("outlook", StringComparison.OrdinalIgnoreCase)) return "Email client";
        if (processName.Contains("winword", StringComparison.OrdinalIgnoreCase)) return "Word processor";
        if (processName.Contains("powerbi", StringComparison.OrdinalIgnoreCase)) return "Analytics client";
        if (processName.Contains("acrobat", StringComparison.OrdinalIgnoreCase) || processName.Contains("pdf", StringComparison.OrdinalIgnoreCase)) return "PDF/document viewer";
        return "Desktop app";
    }

    private static bool IsBrowser(string processName) =>
        string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "firefox", StringComparison.OrdinalIgnoreCase);
}
