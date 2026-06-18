using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _autoFollowTimer = new();
    private bool _isTargetLocked;
    private string? _lastAutoFollowTargetId;

    private void StartAutoFollow()
    {
        _autoFollowTimer.Interval = TimeSpan.FromSeconds(2);
        _autoFollowTimer.Tick += (_, _) => RefreshFollowTargetCard();
        _autoFollowTimer.Start();
        AddTimeline("Auto-follow is on. Switch to another app and wait up to 2 seconds.");
        AppendTranscript("Threadline Follow", "Auto-follow is on. I will update the Current Target card when the foreground app changes. I will ignore Threadline itself so looking back at this panel does not erase the target.");
        RefreshFollowTargetCard(force: true);
    }

    private void ToggleFollowLock_Click(object sender, RoutedEventArgs e)
    {
        _isTargetLocked = !_isTargetLocked;
        RefreshFollowTargetCard(force: true);
        var message = _isTargetLocked
            ? "Locked to the current selected target. Click Follow / Lock again to resume following the foreground app."
            : "Following the foreground app again. Switch apps and wait up to 2 seconds.";
        AddTimeline(message);
        AppendTranscript("Threadline Follow", message);
    }

    private void RefreshFollowTargetCard(bool force = false)
    {
        if (_isTargetLocked && _selectedThreadlineTarget is not null)
        {
            CurrentWindowText.Text = BuildTargetStatus(_selectedThreadlineTarget, "Locked target");
            return;
        }

        var targets = _tabTargetRegistry.ListTargets();
        var activeWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        ThreadlineTarget? activeTarget = null;

        if (activeWindow is not null && !IsThreadlineWindow(activeWindow))
        {
            activeTarget = targets.FirstOrDefault(target => target.Window.Handle == activeWindow.Handle && target.IsActive)
                ?? targets.FirstOrDefault(target => target.Window.Handle == activeWindow.Handle)
                ?? new ThreadlineTarget(
                    $"window:{activeWindow.Handle}",
                    ThreadlineTargetKind.Window,
                    activeWindow,
                    activeWindow.WindowTitle ?? activeWindow.ApplicationName,
                    "native-ui",
                    true,
                    true,
                    "medium",
                    "Generic active window target. Threadline may use native UI fallback unless a better provider is available.");
        }

        if (activeTarget is null)
        {
            if (force)
            {
                CurrentWindowText.Text = "Mode: Following active app\nStatus: Waiting for a non-Threadline foreground app. Click another app and wait up to 2 seconds.";
            }
            return;
        }

        _lastForegroundWindow = activeTarget.Window;
        if (!force && string.Equals(_lastAutoFollowTargetId, activeTarget.Id, StringComparison.OrdinalIgnoreCase)) return;

        _lastAutoFollowTargetId = activeTarget.Id;
        CurrentWindowText.Text = BuildTargetStatus(activeTarget, "Following active app");
        var followMessage = $"Following: {activeTarget.Window.ApplicationName} — {activeTarget.Title}";
        AddTimeline(followMessage);
        AppendTranscript("Threadline Follow", followMessage);
    }

    private static bool IsThreadlineWindow(ActiveWindowSnapshot window) =>
        string.Equals(window.ProcessName, "Threadline.Windows", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(window.ApplicationName, "ThreadlineAI Companion", StringComparison.OrdinalIgnoreCase) ||
        (window.WindowTitle?.Contains("ThreadlineAI", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string BuildTargetStatus(ThreadlineTarget target, string mode)
    {
        var source = target.ProviderKey switch
        {
            "browser-extension" => "Browser extension",
            "notepad-tabs" => "File-backed or provider required",
            "native-ui" => "Native UI fallback",
            _ => target.ProviderKey
        };

        var readiness = target.CanReadBody ? "Ready" : "Needs provider or resolver";
        return $"Mode: {mode}\nTarget: {target.Title}\nApp: {target.Window.ApplicationName}\nSource: {source}\nConfidence: {target.Confidence}\nStatus: {readiness}";
    }
}
