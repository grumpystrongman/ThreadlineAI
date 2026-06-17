namespace Threadline.Windows.Services;

public enum CaptureProviderKind
{
    BrowserExtension,
    NotepadTabs,
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

        if (IsBrowser(process))
        {
            return new AppCapturePlan(
                CaptureProviderKind.BrowserExtension,
                "Browser extension",
                "Use the Threadline browser extension for page/tab-aware context.",
                false,
                "Browser windows should use extension context, not native UI. Native UI can see browser chrome, but it cannot reliably answer page questions like Gmail counts.");
        }

        if (IsNotepad(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.NotepadTabs,
                "Notepad tab provider",
                "Use a Notepad-aware provider so selected tab content is not mixed with other tabs.",
                false,
                "Modern Notepad exposes tab bodies ambiguously through native UI. Threadline needs a Notepad tab/document provider before it can safely summarize body text.");
        }

        if (IsOneNote(process, title))
        {
            return new AppCapturePlan(
                CaptureProviderKind.OneNote,
                "OneNote provider",
                "Use OneNote-aware capture when available; fall back to native UI only for visible note context.",
                true,
                "OneNote is a good Phase 11 target. Native capture can be attempted, but a OneNote-specific provider should own the final workflow.");
        }

        if (IsTerminal(process))
        {
            return new AppCapturePlan(
                CaptureProviderKind.Terminal,
                "Terminal adapter",
                "Use the PowerShell/terminal adapter for command history and output.",
                false,
                "Terminal windows should use adapter context instead of native UI when possible.");
        }

        return new AppCapturePlan(
            CaptureProviderKind.NativeUiFallback,
            "Native UI fallback",
            "Use Windows native accessibility as a fallback context source.",
            true,
            "Native UI fallback may be noisy. Threadline will summarize and warn when confidence is low.");
    }

    private static bool IsBrowser(string process) =>
        string.Equals(process, "chrome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "msedge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsNotepad(string process, string title) =>
        string.Equals(process, "notepad", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase);

    private static bool IsOneNote(string process, string title) =>
        process.Contains("onenote", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("OneNote", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string process) =>
        string.Equals(process, "windowsterminal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "powershell", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "pwsh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "cmd", StringComparison.OrdinalIgnoreCase);
}
