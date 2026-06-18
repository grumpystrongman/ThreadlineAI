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
        _autoFollowTimer.Interval = TimeSpan.FromSeconds(3);
        _autoFollowTimer.Tick += (_, _) => RefreshFollowTargetCard();
        _autoFollowTimer.Start();
        RefreshFollowTargetCard();
    }

    private void ToggleFollowLock_Click(object sender, RoutedEventArgs e)
    {
        _isTargetLocked = !_isTargetLocked;
        RefreshFollowTargetCard(force: true);
        AddTimeline(_isTargetLocked ? "Context target locked." : "Following active app.");
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

        if (activeWindow is not null)
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

        if (activeTarget is null) return;

        _lastForegroundWindow = activeTarget.Window;
        if (!force && string.Equals(_lastAutoFollowTargetId, activeTarget.Id, StringComparison.OrdinalIgnoreCase)) return;

        _lastAutoFollowTargetId = activeTarget.Id;
        CurrentWindowText.Text = BuildTargetStatus(activeTarget, "Following active app");
    }

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
