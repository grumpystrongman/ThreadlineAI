using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _autoFollowTimer = new();
    private bool _isTargetLocked;
    private string? _lastAutoFollowTargetId;
    private ThreadlineTarget? _lastFollowTarget;
    private ThreadlineTarget? _lockedFollowTarget;
    private bool _hasAnnouncedFirstFollowTarget;

    private void StartAutoFollow()
    {
        _autoFollowTimer.Interval = TimeSpan.FromSeconds(1);
        _autoFollowTimer.Tick += (_, _) => SafeRefreshFollowTargetCard();
        _autoFollowTimer.Start();
        AddTimeline("Follow mode is on. Switch to another app, then return here.");
        AppendTranscript("Threadline Follow", "Follow mode is on. I remember the last non-Threadline app you used and keep it visible when you return to this sidecar.");
        SafeRefreshFollowTargetCard(force: true);
    }

    private void ToggleFollowLock_Click(object sender, RoutedEventArgs e)
    {
        if (_isTargetLocked)
        {
            _isTargetLocked = false;
            _lockedFollowTarget = null;
            _lastAutoFollowTargetId = null;
            SafeRefreshFollowTargetCard(force: true);
            const string unlockMessage = "Unlocked. Following the last active app again.";
            AddTimeline(unlockMessage);
            AppendTranscript("Threadline Follow", unlockMessage);
            return;
        }

        _lockedFollowTarget = _selectedThreadlineTarget ?? _lastFollowTarget;
        if (_lockedFollowTarget is null)
        {
            const string noTargetMessage = "Nothing to lock yet. Switch to another app first, then return to Threadline.";
            AddTimeline(noTargetMessage);
            AppendTranscript("Threadline Follow", noTargetMessage);
            SafeRefreshFollowTargetCard(force: true);
            return;
        }

        _isTargetLocked = true;
        CurrentWindowText.Text = BuildTargetStatus(_lockedFollowTarget, "Locked target");
        var lockMessage = $"Locked target: {_lockedFollowTarget.Window.ApplicationName} — {_lockedFollowTarget.Title}";
        AddTimeline(lockMessage);
        AppendTranscript("Threadline Follow", lockMessage);
    }

    private void SafeRefreshFollowTargetCard(bool force = false)
    {
        try
        {
            RefreshFollowTargetCard(force);
        }
        catch (Exception ex)
        {
            AddTimeline("Follow update failed: " + ex.Message);
        }
    }

    private void RefreshFollowTargetCard(bool force = false)
    {
        if (_isTargetLocked && _lockedFollowTarget is not null)
        {
            CurrentWindowText.Text = BuildTargetStatus(_lockedFollowTarget, "Locked target");
            return;
        }

        var activeWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        if (activeWindow is null)
        {
            if (force) CurrentWindowText.Text = "Mode: Follow\nStatus: No foreground app detected yet.";
            return;
        }

        if (IsThreadlineWindow(activeWindow))
        {
            if (_lastFollowTarget is not null)
            {
                CurrentWindowText.Text = BuildTargetStatus(_lastFollowTarget, "Following last active app");
            }
            else if (force)
            {
                CurrentWindowText.Text = "Mode: Follow\nStatus: Switch to another app, then return to Threadline.";
            }
            return;
        }

        var activeTarget = ResolveTargetForWindow(activeWindow);
        _lastForegroundWindow = activeTarget.Window;
        _lastFollowTarget = activeTarget;

        if (!force && string.Equals(_lastAutoFollowTargetId, activeTarget.Id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentWindowText.Text = BuildTargetStatus(activeTarget, "Following active app");
            return;
        }

        _lastAutoFollowTargetId = activeTarget.Id;
        CurrentWindowText.Text = BuildTargetStatus(activeTarget, "Following active app");
        var followMessage = $"Following: {activeTarget.Window.ApplicationName} — {activeTarget.Title}";
        AddTimeline(followMessage);

        if (!_hasAnnouncedFirstFollowTarget || force)
        {
            _hasAnnouncedFirstFollowTarget = true;
            AppendTranscript("Threadline Follow", followMessage);
        }
    }

    private ThreadlineTarget ResolveTargetForWindow(ActiveWindowSnapshot activeWindow)
    {
        var targets = _tabTargetRegistry.ListTargets();
        return targets.FirstOrDefault(target => target.Window.Handle == activeWindow.Handle && target.IsActive)
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
