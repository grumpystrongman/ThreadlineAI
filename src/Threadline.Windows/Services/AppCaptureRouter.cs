namespace Threadline.Windows.Services;

public enum CaptureProviderKind
{
    BrowserExtension,
    NotepadText,
    IdeVisibleContext,
    OfficeDocument,
    PdfReader,
    DashboardExtensionFirst,
    OneNote,
    Terminal,
    NativeUiFallback
}

public sealed record AppCapturePlan(
    CaptureProviderKind Provider,
    string DisplayName,
    string Description,
    bool CanCaptureNow,
    string Guidance);

public sealed class AppCaptureRouter
{
    public AppCapturePlan PlanFor(ActiveWindowSnapshot target)
    {
        var process = target.ProcessName ?? string.Empty;
        var title = target.WindowTitle ?? string.Empty;

        if (IsNotepad(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.NotepadText,
                "Notepad / file-backed or visible text",
                "Use the native Notepad provider. It returns full document when a safe file path is available, otherwise visible text or title-only fallback.",
                true,
                "Notepad capture is honest by level: full document only when the file is resolved and readable; otherwise visible document, readable UI tree, or title only.");
        }

        if (IsVsCode(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.IdeVisibleContext,
                "VS Code / IDE context",
                "Use IDE title/workspace/active-file signals plus native readable UI when available.",
                true,
                "VS Code capture should not claim full editor access unless accessibility exposes text. Build 18 mainly provides active file/workspace/title and readable UI fallback.");
        }

        if (IsTerminal(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.Terminal,
                "Terminal / PowerShell visible text",
                "Use native terminal capture for visible command output when accessible.",
                true,
                "Terminal capture is visible context, not guaranteed full scrollback or command history.");
        }

        if (IsOffice(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.OfficeDocument,
                "Office document safe context",
                "Use safe title/path/UIA signals for Office documents without automatic COM extraction.",
                true,
                "Office capture avoids unsafe automatic COM automation. It reports title-only or readable UI tree unless a safer app-specific provider is later added.");
        }

        if (IsPdfReader(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.PdfReader,
                "PDF reader context",
                "Use PDF title/path and accessible visible text when available.",
                true,
                "OCR/vision fallback is intentionally not promised in this native build. The provider records that limitation in warnings.");
        }

        if (IsDashboard(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.DashboardExtensionFirst,
                "Dashboard / extension first",
                "Power BI, Tableau, and browser dashboards should prefer browser-extension context; native UI is fallback only.",
                true,
                "Dashboards often expose labels, not data semantics, through native UI. Use extension context first and treat native capture as partial.");
        }

        if (IsBrowser(process))
        {
            return new AppCapturePlan(
                CaptureProviderKind.BrowserExtension,
                "Browser extension",
                "Use the Threadline browser extension for page/tab-aware context.",
                false,
                "Browser windows should use extension context, not native UI. Native UI can see browser chrome, but it cannot reliably answer page questions like Gmail counts.");
        }

        if (IsOneNote(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.OneNote,
                "OneNote provider",
                "Use OneNote-aware capture when available; fall back to native UI only for visible note context.",
                true,
                "OneNote is a good future app-specific target. Native capture can be attempted, but a OneNote-specific provider should own the final workflow.");
        }

        return new AppCapturePlan(
            CaptureProviderKind.NativeUiFallback,
            "Generic Win32/UIA provider",
            "Use Windows native accessibility as a fallback context source.",
            true,
            "Native UI fallback may be noisy. Threadline will label the result as readable UI tree, title only, or no readable context instead of pretending it has a full document.");
    }

    private static bool IsBrowser(string process) =>
        string.Equals(process, "chrome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "msedge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsNotepad(string process, string title) =>
        string.Equals(process, "notepad", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase);

    private static bool IsVsCode(string process, string title) =>
        string.Equals(process, "Code", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "Code - Insiders", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase);

    private static bool IsOneNote(string process, string title) =>
        process.Contains("onenote", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("OneNote", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string process, string title) =>
        string.Equals(process, "WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "powershell", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "pwsh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "cmd", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase);

    private static bool IsOffice(string process, string title) =>
        string.Equals(process, "WINWORD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "EXCEL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "POWERPNT", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "MSACCESS", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" - Word", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" - Excel", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" - PowerPoint", StringComparison.OrdinalIgnoreCase);

    private static bool IsPdfReader(string process, string title) =>
        string.Equals(process, "AcroRd32", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "Acrobat", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "AcroCEF", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsDashboard(string process, string title) =>
        title.Contains("Power BI", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Tableau", StringComparison.OrdinalIgnoreCase) ||
        process.Contains("PBIDesktop", StringComparison.OrdinalIgnoreCase) ||
        process.Contains("Tableau", StringComparison.OrdinalIgnoreCase);
}
