using System;
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Browser extension guidance skipped: {ex.GetType().Name}: {ex.Message}");
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
        TrustControlStatusText.Text = "Browser window detected - send page for deeper context";
        AppendTranscript(
            "Threadline Context",
            "It looks like you're using a browser but I'm limited to the window title. For deeper page-aware answers, please send this page using the Threadline browser extension (click the extension icon and choose 'Send Page'), then ask again so I can use the page title, URL, and page text.");
        AddTimeline("Browser extension guidance displayed.");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
