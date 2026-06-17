namespace Threadline.Windows.Services;

public sealed class BrowserTabProvider : ITabProvider
{
    public bool CanInspect(ActiveWindowSnapshot window) =>
        string.Equals(window.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(window.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(window.ProcessName, "firefox", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ThreadlineTarget> GetTargets(ActiveWindowSnapshot window)
    {
        var title = window.WindowTitle ?? window.ApplicationName;
        return
        [
            new ThreadlineTarget(
                $"browser-tab:{window.Handle}:{title}",
                ThreadlineTargetKind.BrowserTab,
                window,
                title,
                "browser-extension",
                true,
                false,
                "provider-required",
                "Browser tab detected. Page context should come from the Threadline browser extension, not native UI.")
        ];
    }
}
