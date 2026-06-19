namespace Threadline.Windows.Services;

public enum ContextConfidence
{
    None,
    Low,
    Medium,
    High
}

public enum CaptureMethodKind
{
    Provider,
    UiAutomation,
    FileResolver,
    Screenshot,
    Ocr,
    ImageExtraction,
    LayoutAnalysis
}

public enum ActiveAppType
{
    Unknown,
    Browser,
    TextEditor,
    OfficeDocument,
    MailClient,
    PdfViewer,
    AnalyticsTool,
    Terminal,
    DesktopApp
}

public sealed record CaptureMethodAvailability(
    CaptureMethodKind Method,
    string DisplayName,
    bool IsAvailable,
    string Notes);

public sealed record ProcessIntelligence(
    string WindowTitle,
    nint WindowHandle,
    int? ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string? ParentProcessName,
    int? ParentProcessId,
    ActiveAppType AppType,
    IReadOnlyList<CaptureMethodAvailability> CaptureMethods)
{
    public string WindowHandleDisplay => $"0x{WindowHandle.ToInt64():X8}";

    public string ToDisplayText()
    {
        var methods = CaptureMethods.Count == 0
            ? "- None detected."
            : string.Join(Environment.NewLine, CaptureMethods.Select(method => $"- {(method.IsAvailable ? "✓" : "•")} {method.DisplayName}: {method.Notes}"));

        return $"Window Title: {WindowTitle}\nHWND: {WindowHandleDisplay}\nPID: {ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nExecutable: {ExecutablePath ?? ProcessName}\nParent Process: {ParentProcessName ?? "Unknown"}\nApp Type: {AppType}\n\nCapture Methods:\n{methods}";
    }
}

public sealed record CaptureDiagnostics(
    string WindowTitle,
    string ProcessName,
    int? ProcessId,
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
            ? "- None."
            : string.Join(Environment.NewLine, Details.Select(detail => $"- {detail}"));

        return $"Window: {WindowTitle}\nProcess: {ProcessName}\nPID: {ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nCapture method used: {CaptureMethodUsed}\nChars extracted: {CharactersExtracted}\nImages found: {ImagesFound}\nSummary size: {SummarySize}\nConfidence: {Confidence}\n\nDetails:\n{details}";
    }
}
