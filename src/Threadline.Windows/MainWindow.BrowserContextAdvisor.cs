using Microsoft.UI.Xaml;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _browserExtensionGuidanceTimer = new();
    private bool _browserExtensionGuidanceStarted;
    private bool _browserExtensionGuidanceShown;

    private void StartBrowserExtensionGuidanceTimer()
    {
        if (_browserExtensionGuidanceStarted) return;

        _browserExtensionGuidanceStarted = true;
        _browserExtensionGuidanceTimer.Interval = TimeSpan.FromSeconds(3);
        _browserExtensionGuidanceTimer.Tick += (_, _) => SafeUpdateBrowserExtensionGuidance();
        _browserExtensionGuidanceTimer.Start();
    }

    private void SafeUpdateBrowserExtensionGuidance()
    {
        try
        {
            UpdateBrowserExtensionGuidance();
        }
        catch
        {
            // Browser guidance is advisory. It should never interrupt the sidecar workflow.
        }
    }

    private void UpdateBrowserExtensionGuidance()
    {
        var context = CurrentContextText.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(context))
        {
            return;
        }

        var hasBrowserExtensionContext = ContainsAny(context,
            "browser-extension",
            "Source: browser",
            "Source: Browser",
            "Status: Browser");

        if (hasBrowserExtensionContext)
        {
            TrustControlStatusText.Text = "Browser page context active";
            return;
        }

        var looksLikeBrowserWindow = ContainsAny(context,
            "chrome",
            "google chrome",
            "microsoft edge",
            "msedge",
            "firefox",
            "opera",
            "brave",
            "browser",
            "http://",
            "https://");

        if (!looksLikeBrowserWindow || _browserExtensionGuidanceShown)
        {
            return;
        }

        _browserExtensionGuidanceShown = true;
        TrustControlStatusText.Text = "Use browser extension for page depth";
        AppendTranscript(
            "Threadline Context",
            "I can see the browser/app shell, but deeper page-aware answers need the Threadline browser extension. Click the extension, choose Send Page, then refresh or ask again so I can use the page title, URL, and page text instead of only the window title.");
        AddTimeline("Showed browser extension guidance for deeper page context.");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
