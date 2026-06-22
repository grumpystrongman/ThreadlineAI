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
    ClipboardSelection,
    Screenshot,
    Ocr,
    ImageExtraction,
    LayoutAnalysis,
    TitleProcessFallback
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

public sealed record ContextCaptureConsent(
    bool ClipboardSelectionAllowed = false,
    bool ScreenshotVisionAllowed = false,
    bool ScreenshotVisionUserApproved = false,
    bool ScreenshotVisionAppAllowed = false,
    bool RawScreenshotStorageAllowed = false,
    string ScreenshotVisionConsentReason = "Screenshot, OCR, and vision capture require explicit user approval for this capture.")
{
    public static ContextCaptureConsent None { get; } = new();

    public bool CanUseScreenshotVision =>
        ScreenshotVisionAllowed &&
        ScreenshotVisionUserApproved &&
        ScreenshotVisionAppAllowed;
}

public enum ContextCaptureKind
{
    None,
    PageText,
    SelectedText,
    TitleOnly,
    Ocr,
    FileBacked,
    UiAutomation,
    ClipboardSelection,
    ScreenshotVision
}

public enum ContextProviderAttemptStatus
{
    Captured,
    Skipped,
    Missed,
    Blocked,
    Failed
}

public sealed record ContextProviderAttempt(
    string Provider,
    ContextProviderAttemptStatus Status,
    string Detail);

public sealed record ContextReceipt(
    string SourceUsed,
    ContextConfidence Confidence,
    ContextCaptureKind CaptureKind,
    IReadOnlyList<string> Captured,
    IReadOnlyList<string> NotCaptured,
    bool MissingRealWorkingContent,
    string UserMessage,
    IReadOnlyList<ContextProviderAttempt> ProviderAttempts)
{
    public bool IsPageText => CaptureKind == ContextCaptureKind.PageText;
    public bool IsSelectedText => CaptureKind == ContextCaptureKind.SelectedText;
    public bool IsTitleOnly => CaptureKind == ContextCaptureKind.TitleOnly;
    public bool IsOcr => CaptureKind == ContextCaptureKind.Ocr;
    public bool IsFileBacked => CaptureKind == ContextCaptureKind.FileBacked;
    public bool IsUiAutomation => CaptureKind == ContextCaptureKind.UiAutomation;
    public bool IsClipboardSelection => CaptureKind == ContextCaptureKind.ClipboardSelection;
    public bool IsScreenshotVision => CaptureKind == ContextCaptureKind.ScreenshotVision;

    public string ToDisplayText()
    {
        return $"Source used: {SourceUsed}\nConfidence: {Confidence}\nCapture kind: {CaptureKind}\nMissing real working content: {(MissingRealWorkingContent ? "Yes" : "No")}\n\nWhat was captured:\n{FormatList(Captured)}\n\nWhat was not captured:\n{FormatList(NotCaptured)}\n\nReceipt message: {UserMessage}\n\nProvider ladder:\n{FormatAttempts(ProviderAttempts)}";
    }

    public string ToPromptText()
    {
        return $"Context receipt:\n- Source used: {SourceUsed}\n- Confidence: {Confidence}\n- Capture kind: {CaptureKind}\n- Is page text: {IsPageText}\n- Is selected text: {IsSelectedText}\n- Is title-only: {IsTitleOnly}\n- Is OCR: {IsOcr}\n- Is file-backed: {IsFileBacked}\n- Is UI Automation: {IsUiAutomation}\n- Is screenshot/vision: {IsScreenshotVision}\n- Missing real working content: {MissingRealWorkingContent}\n- Receipt message: {UserMessage}\n\nWhat was captured:\n{FormatList(Captured)}\n\nWhat was not captured:\n{FormatList(NotCaptured)}\n\nProvider ladder:\n{FormatAttempts(ProviderAttempts)}";
    }

    private static string FormatList(IReadOnlyList<string> items) => items.Count == 0
        ? "- None."
        : string.Join(Environment.NewLine, items.Select(item => $"- {item}"));

    private static string FormatAttempts(IReadOnlyList<ContextProviderAttempt> attempts) => attempts.Count == 0
        ? "- Not recorded."
        : string.Join(Environment.NewLine, attempts.Select(attempt => $"- {attempt.Provider}: {attempt.Status} — {attempt.Detail}"));
}
