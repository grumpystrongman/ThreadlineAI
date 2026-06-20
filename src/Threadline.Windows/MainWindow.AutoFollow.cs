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
        AddTimeline("Window edge trigger is on. Hover near another app edge and click AI to attach Threadline there.");
        AppendTranscript("Threadline", "Hover near a window edge and click AI to attach this sidecar to that window. Once open, the sidecar stays attached until you explicitly choose another window.");
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
            PlaceSidecarForTarget(_selectedThreadlineTarget ?? _lastFollowTarget, "Unlocked; keeping current sidecar attachment.");
            const string unlockMessage = "Unlocked. The sidecar will stay attached until you click another AI edge icon.";
            AddTimeline(unlockMessage);
            AppendTranscript("Threadline", unlockMessage);
            return;
        }

        _lockedFollowTarget = _selectedThreadlineTarget ?? _lastFollowTarget;
        if (_lockedFollowTarget is null)
        {
            const string noTargetMessage = "Nothing to lock yet. Hover over another app edge and click AI first.";
            AddTimeline(noTargetMessage);
            AppendTranscript("Threadline", noTargetMessage);
            SafeRefreshFollowTargetCard(force: true);
            return;
        }

        _isTargetLocked = true;
        CurrentWindowText.Text = BuildTargetStatus(_lockedFollowTarget, "Locked target");
        PlaceSidecarForTarget(_lockedFollowTarget, "Locked beside target.");
        var lockMessage = $"Locked target: {_lockedFollowTarget.Window.ApplicationName} — {_lockedFollowTarget.Title}";
        AddTimeline(lockMessage);
        AppendTranscript("Threadline", lockMessage);
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
            if (!_sidecarWindowHiddenForTrigger)
            {
                PlaceSidecarForTarget(_lockedFollowTarget, "Maintaining locked attachment.");
            }
            return;
        }

        // When the sidecar is open, it should not chase focus changes. The floating AI icon can retarget,
        // but the visible sidecar remains bound to the explicitly selected/clicked window until another AI icon is clicked.
        if (!_sidecarWindowHiddenForTrigger)
        {
            var attachedTarget = _selectedThreadlineTarget ?? _lastFollowTarget;
            if (attachedTarget is not null)
            {
                CurrentWindowText.Text = BuildTargetStatus(attachedTarget, "Attached target");
                return;
            }

            if (force)
            {
                CurrentWindowText.Text = "Mode: Waiting\nStatus: Hover near another app edge and click AI to attach this sidecar.";
            }
            return;
        }

        var activeWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        if (activeWindow is null)
        {
            if (force) CurrentWindowText.Text = "Mode: Edge trigger\nStatus: No foreground app detected yet.";
            return;
        }

        if (IsThreadlineWindow(activeWindow))
        {
            if (_lastFollowTarget is not null)
            {
                CurrentWindowText.Text = BuildTargetStatus(_lastFollowTarget, "Last attached target");
            }
            else if (force)
            {
                CurrentWindowText.Text = "Mode: Edge trigger\nStatus: Hover near another app edge and click AI.";
            }
            return;
        }

        var activeTarget = ResolveTargetForWindow(activeWindow);
        _lastForegroundWindow = activeTarget.Window;
        _lastFollowTarget = activeTarget;

        if (!force && string.Equals(_lastAutoFollowTargetId, activeTarget.Id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentWindowText.Text = BuildTargetStatus(activeTarget, "Available target");
            return;
        }

        _lastAutoFollowTargetId = activeTarget.Id;
        CurrentWindowText.Text = BuildTargetStatus(activeTarget, "Available target");
        var followMessage = $"Available: {activeTarget.Window.ApplicationName} — {activeTarget.Title}";
        AddTimeline(followMessage);

        if (!_hasAnnouncedFirstFollowTarget || force)
        {
            _hasAnnouncedFirstFollowTarget = true;
        }
    }

    private ThreadlineTarget ResolveTargetForWindow(ActiveWindowSnapshot activeWindow)
    {
        var targets = _tabTargetRegistry.ListTargets();
        var sameWindowTargets = targets
            .Where(target => target.Window.Handle == activeWindow.Handle)
            .ToList();

        return sameWindowTargets.FirstOrDefault(target => target.Kind != ThreadlineTargetKind.Window && target.IsActive)
            ?? sameWindowTargets.FirstOrDefault(target => target.Kind != ThreadlineTargetKind.Window)
            ?? sameWindowTargets.FirstOrDefault(target => target.IsActive)
            ?? sameWindowTargets.FirstOrDefault()
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
            "notepad-tabs" => "Notepad file/provider resolver",
            "native-ui" => "Native UI fallback",
            _ => target.ProviderKey
        };

        var readiness = target.CanReadBody || target.ProviderKey == "notepad-tabs" || target.ProviderKey == "browser-extension"
            ? "Ready to resolve on Ask"
            : "Needs provider or resolver";
        return $"Mode: {mode}\nTarget: {target.Title}\nApp: {target.Window.ApplicationName}\nSource: {source}\nConfidence: {target.Confidence}\nStatus: {readiness}";
    }
}
