namespace Threadline.Windows.Services;

public enum ContextConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public sealed record CaptureMethodAvailability(
    string Name,
    bool IsAvailable,
    string Reason);

public sealed record ProcessIntelligenceSnapshot(
    string WindowTitle,
    string HandleHex,
    int? ProcessId,
    string ProcessName,
    string? ExecutablePath,
    int? ParentProcessId,
    string? ParentProcessName,
    string AppType,
    IReadOnlyList<CaptureMethodAvailability> CaptureMethods)
{
    public string ToDisplayText()
    {
        var methods = CaptureMethods.Count == 0
            ? "None detected."
            : string.Join(Environment.NewLine, CaptureMethods.Select(method => $"{(method.IsAvailable ? "✓" : "·")} {method.Name} — {method.Reason}"));

        return $"Window Title: {WindowTitle}\nHWND: {HandleHex}\nPID: {ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nExecutable: {ProcessName}\nPath: {ExecutablePath ?? "Unknown"}\nParent PID: {ParentProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nParent Process: {ParentProcessName ?? "Unknown"}\nApp Type: {AppType}\n\nCapture Methods:\n{methods}";
    }
}

public sealed class ProcessIntelligenceService
{
    public ProcessIntelligenceSnapshot Inspect(ActiveWindowSnapshot window)
    {
        var processName = window.ProcessName ?? "unknown";
        var appType = ClassifyApp(processName, window.WindowTitle ?? string.Empty);
        var methods = BuildCaptureMethods(processName, appType, window).ToList();

        return new ProcessIntelligenceSnapshot(
            window.WindowTitle ?? "Unknown",
            $"0x{window.Handle.ToInt64():X8}",
            window.ProcessId,
            processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : processName + ".exe",
            window.ExecutablePath,
            null,
            "Not resolved in 11.8 safe process model",
            appType,
            methods);
    }

    private static IEnumerable<CaptureMethodAvailability> BuildCaptureMethods(string processName, string appType, ActiveWindowSnapshot window)
    {
        var isBrowser = IsBrowser(processName);
        var isNotepad = IsNotepad(processName, window.WindowTitle ?? string.Empty);
        var hasWindow = window.Handle != nint.Zero;

        yield return new CaptureMethodAvailability(
            "Provider",
            isBrowser || isNotepad || appType == "Office document" || appType == "PDF/document viewer" || appType == "Terminal",
            isBrowser ? "Browser extension/provider route is preferred."
                : isNotepad ? "Notepad tab/file provider route is available when a saved file can be resolved."
                : appType == "Office document" ? "Office-specific provider is planned; UI Automation can be attempted now."
                : appType == "PDF/document viewer" ? "Document provider is planned; UI Automation can be attempted now."
                : appType == "Terminal" ? "Terminal adapter/provider is preferred when available."
                : "No app-specific provider is registered yet.");

        yield return new CaptureMethodAvailability(
            "UI Automation",
            hasWindow,
            hasWindow ? "Windows accessibility/native UI text can be attempted." : "No HWND is available.");

        yield return new CaptureMethodAvailability(
            "Document/File Resolver",
            isNotepad || appType == "Office document" || appType == "PDF/document viewer",
            isNotepad ? "Saved Notepad files can be resolved by exact filename match."
                : appType == "Office document" ? "Office document resolver is planned."
                : appType == "PDF/document viewer" ? "PDF/document resolver is planned."
                : "No file/document resolver is registered for this app.");

        yield return new CaptureMethodAvailability(
            "Screenshot/OCR",
            hasWindow,
            hasWindow ? "Fallback only; OCR engine integration is not configured in this build." : "No HWND is available.");
    }

    private static string ClassifyApp(string processName, string title)
    {
        if (IsBrowser(processName)) return "Browser";
        if (IsNotepad(processName, title)) return "Text editor";
        if (IsOffice(processName)) return "Office document";
        if (IsPdf(processName, title)) return "PDF/document viewer";
        if (IsTerminal(processName)) return "Terminal";
        if (processName.Contains("chatgpt", StringComparison.OrdinalIgnoreCase)) return "AI desktop app";
        return "Desktop app";
    }

    private static bool IsBrowser(string processName) =>
        string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsNotepad(string processName, string title) =>
        string.Equals(processName, "notepad", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase);

    private static bool IsOffice(string processName) =>
        string.Equals(processName, "winword", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "excel", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "powerpnt", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "outlook", StringComparison.OrdinalIgnoreCase);

    private static bool IsPdf(string processName, string title) =>
        processName.Contains("acrobat", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("acrord", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(".pdf -", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string processName) =>
        string.Equals(processName, "windowsterminal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "powershell", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "pwsh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "cmd", StringComparison.OrdinalIgnoreCase);
}
