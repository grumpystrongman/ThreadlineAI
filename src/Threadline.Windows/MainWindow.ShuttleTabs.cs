using Microsoft.UI.Xaml;
using Threadline.Windows.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int ShuttleTabWidth = 24;
    private const int ShuttleTabHeight = 72;
    private const int ShuttleTabInset = 2;
    private const int ShuttleTabMinimumWindowWidth = 240;
    private const int ShuttleTabMinimumWindowHeight = 160;
    private const int ShuttleTabMaximumVisibleWindows = 12;

    private readonly DispatcherTimer _shuttleTabTimer = new();
    private readonly List<ShuttleTabWindow> _shuttleTabs = new();
    private readonly Dictionary<ShuttleTabWindow, ThreadlineTarget> _shuttleTargets = new();

    private void StartShuttleTabs()
    {
        _edgeHoverTimer.Stop();
        _edgeTriggerWindow?.HideTrigger();
        if (_edgeTriggerWindow is not null)
        {
            _edgeTriggerWindow.DirectWindowHoverEnabled = false;
        }

        UpdateShuttleTerminology();

        if (_shuttleTabTimer.IsEnabled)
        {
            return;
        }

        _shuttleTabTimer.Interval = TimeSpan.FromMilliseconds(750);
        _shuttleTabTimer.Tick += (_, _) => SafeUpdateShuttleTabs();
        _shuttleTabTimer.Start();
        SafeUpdateShuttleTabs();
    }

    private void UpdateShuttleTerminology()
    {
        try
        {
            SidecarAttachmentText.Text = "Loom ready. Click a Shuttle tab to open a Warp Thread.";
            WorkThreadStatusText.Text = "Loom: ready";
            CurrentContextText.Text = "Choose a Shuttle tab or continue with the current Warp Thread.";
            ReceiptTrustText.Text = "Lineage on";
            ReceiptSourceText.Text = "No Shuttle yet";
            TrustControlStatusText.Text = "Lineage";
        }
        catch
        {
            // Startup-safe terminology polish only. Never block the shell.
        }
    }

    private void SafeUpdateShuttleTabs()
    {
        try
        {
            UpdateShuttleTabs();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Shuttle tabs skipped: {ex.GetType().Name}: {ex.Message}");
            HideAllShuttleTabs();
        }
    }

    private void UpdateShuttleTabs()
    {
        if (!_attachSidecarToTarget)
        {
            HideAllShuttleTabs();
            return;
        }

        var candidates = GetShuttleTabCandidates();
        EnsureShuttleTabCapacity(candidates.Count);
        _shuttleTargets.Clear();

        for (var index = 0; index < _shuttleTabs.Count; index++)
        {
            var tab = _shuttleTabs[index];
            if (index >= candidates.Count)
            {
                tab.Hide();
                continue;
            }

            var target = candidates[index];
            if (!GetWindowRect(target.Window.Handle, out var rect))
            {
                tab.Hide();
                continue;
            }

            _shuttleTargets[tab] = target;
            var location = GetShuttleTabLocation(target.Window.Handle, rect);
            tab.Label = "»";
            tab.ShowAt(location, new SizeInt32(ShuttleTabWidth, ShuttleTabHeight));
        }
    }

    private List<ThreadlineTarget> GetShuttleTabCandidates()
    {
        var targets = new List<ThreadlineTarget>();
        var seen = new HashSet<nint>();

        EnumWindows((handle, _) =>
        {
            if (targets.Count >= ShuttleTabMaximumVisibleWindows)
            {
                return false;
            }

            if (handle == nint.Zero || seen.Contains(handle) || !IsWindow(handle) || !IsWindowVisible(handle))
            {
                return true;
            }

            seen.Add(handle);
            var snapshot = GetUsableWindowSnapshot(handle);
            if (snapshot is null || snapshot.Handle == nint.Zero || !GetWindowRect(snapshot.Handle, out var rect))
            {
                return true;
            }

            if (rect.Width < ShuttleTabMinimumWindowWidth || rect.Height < ShuttleTabMinimumWindowHeight)
            {
                return true;
            }

            // When the sidecar is already open against a window, avoid placing a Shuttle tab on that
            // same source. Other windows remain available as potential Warp Threads.
            var candidate = CreateWindowTarget(snapshot);
            if (!_sidecarCollapsedToHandle && !_sidecarWindowHiddenForTrigger &&
                string.Equals(candidate.Id, _attachedSidecarTargetId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            targets.Add(candidate);
            return true;
        }, nint.Zero);

        return targets;
    }

    private PointInt32 GetShuttleTabLocation(nint targetHandle, NativeRect targetRect)
    {
        var sidecarHwnd = WindowNative.GetWindowHandle(this);
        var sidecarId = Win32Interop.GetWindowIdFromWindow(sidecarHwnd);
        var workArea = GetTargetWorkArea(sidecarId, targetHandle);

        var x = targetRect.Right - ShuttleTabWidth - ShuttleTabInset;
        var y = targetRect.Top + Math.Max(24, (targetRect.Height - ShuttleTabHeight) / 2);

        x = ClampToArea(x, workArea.X + SidecarScreenMargin, workArea.X + workArea.Width - ShuttleTabWidth - SidecarScreenMargin);
        y = ClampToArea(y, workArea.Y + SidecarScreenMargin, workArea.Y + workArea.Height - ShuttleTabHeight - SidecarScreenMargin);
        return new PointInt32(x, y);
    }

    private void EnsureShuttleTabCapacity(int count)
    {
        while (_shuttleTabs.Count < count)
        {
            var tab = new ShuttleTabWindow();
            tab.Clicked += ShuttleTab_Clicked;
            _shuttleTabs.Add(tab);
        }
    }

    private void ShuttleTab_Clicked(object? sender, EventArgs e)
    {
        if (sender is not ShuttleTabWindow tab || !_shuttleTargets.TryGetValue(tab, out var target))
        {
            return;
        }

        _floatingTriggerTarget = target;
        HideAllShuttleTabs();
        AddTimeline($"Shuttle opened {target.Window.ApplicationName} as a Warp Thread.");
        RestoreSidecarFromFloatingTrigger();
    }

    private void HideAllShuttleTabs()
    {
        foreach (var tab in _shuttleTabs)
        {
            tab.Hide();
        }

        _shuttleTargets.Clear();
    }
}
