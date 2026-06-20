using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Threadline.Windows.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int ShowWindowNormal = 1;
    private const int ShowWindowHide = 0;
    private const int SidecarDefaultWidth = 430;
    private const int SidecarMinimumWidth = 360;
    private const int SidecarMinimumHeight = 620;
    private const int FloatingTriggerWidth = 42;
    private const int FloatingTriggerHeight = 118;
    private const int FloatingTriggerHoverZone = 22;
    private const int SidecarScreenMargin = 12;
    private const int SidecarScreenTopOffset = 40;

    private readonly DispatcherTimer _edgeHoverTimer = new();
    private EdgeTriggerWindow? _edgeTriggerWindow;
    private bool _attachSidecarToTarget = true;
    private bool _sidecarCollapsedToHandle = true;
    private bool _sidecarWindowHiddenForTrigger = true;

    private void ConfigureSidecarWindow()
    {
        SetSidecarVisualState();
        StartFloatingEdgeTrigger();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Initial sidecar placement.");

        try
        {
            RootShell.Loaded += (_, _) =>
            {
                if (_sidecarWindowHiddenForTrigger)
                {
                    _ = RootShell.DispatcherQueue.TryEnqueue(HideMainSidecarWindow);
                }
            };
        }
        catch
        {
            // If delayed hiding cannot be registered, fall back to an immediate hide attempt.
        }

        HideMainSidecarWindow();
    }

    private void StartFloatingEdgeTrigger()
    {
        _ = EnsureEdgeTriggerWindow();

        _edgeHoverTimer.Interval = TimeSpan.FromMilliseconds(120);
        _edgeHoverTimer.Tick += (_, _) => SafeUpdateFloatingEdgeTrigger();
        _edgeHoverTimer.Start();
    }

    private EdgeTriggerWindow EnsureEdgeTriggerWindow()
    {
        if (_edgeTriggerWindow is not null) return _edgeTriggerWindow;

        _edgeTriggerWindow = new EdgeTriggerWindow();
        _edgeTriggerWindow.TriggerRequested += (_, _) => RestoreSidecarFromFloatingTrigger();
        return _edgeTriggerWindow;
    }

    private void ToggleAttachSidecarMode_Click(object sender, RoutedEventArgs e)
    {
        _attachSidecarToTarget = AttachSidecarToggle.IsChecked == true;

        if (_attachSidecarToTarget)
        {
            PlaceSidecarForTarget(GetBestSidecarTarget(), "Attach sidecar mode enabled.");
            AddTimeline("Sidecar attach mode enabled.");
        }
        else
        {
            DockSidecarToScreen("Sidecar: Screen dock mode. Attach is off.");
            AddTimeline("Sidecar screen dock mode enabled.");
        }
    }

    private void CollapseSidecarToHandle_Click(object sender, RoutedEventArgs e)
    {
        HideSidecarBehindFloatingTrigger();
    }

    private void ExpandSidecarFromHandle(object sender, PointerRoutedEventArgs e)
    {
        RestoreSidecarFromFloatingTrigger();
    }

    private void HideSidecarBehindFloatingTrigger()
    {
        _sidecarCollapsedToHandle = true;
        _sidecarWindowHiddenForTrigger = true;
        SetSidecarVisualState();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Hidden behind floating edge trigger.");
        HideMainSidecarWindow();
        AddTimeline("Sidecar hidden. Hover near the target window edge to reveal the floating trigger.");
    }

    private void RestoreSidecarFromFloatingTrigger()
    {
        _sidecarCollapsedToHandle = false;
        _sidecarWindowHiddenForTrigger = false;
        _edgeTriggerWindow?.HideTrigger();
        ShowMainSidecarWindow();
        SetSidecarVisualState();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Opened from floating edge trigger.");
        AddTimeline("Sidecar opened from floating edge trigger.");
    }

    private void HideMainSidecarWindow()
    {
        try
        {
            _ = ShowWindow(WindowNative.GetWindowHandle(this), ShowWindowHide);
        }
        catch
        {
            // If hiding fails, leave the chat visible rather than crashing the app.
        }
    }

    private void ShowMainSidecarWindow()
    {
        try
        {
            _ = ShowWindow(WindowNative.GetWindowHandle(this), ShowWindowNormal);
            Activate();
        }
        catch
        {
            // If restoring fails, the floating trigger will remain available on the next hover pass.
        }
    }

    private void SafeUpdateFloatingEdgeTrigger()
    {
        try
        {
            UpdateFloatingEdgeTrigger();
        }
        catch
        {
            _edgeTriggerWindow?.HideTrigger();
        }
    }

    private void UpdateFloatingEdgeTrigger()
    {
        if (!_sidecarWindowHiddenForTrigger || !_attachSidecarToTarget)
        {
            _edgeTriggerWindow?.HideTrigger();
            return;
        }

        var target = GetBestSidecarTarget();
        if (target is null)
        {
            var activeWindow = GetCurrentNonThreadlineWindow();
            if (activeWindow is null || activeWindow.Handle == nint.Zero || !IsWindow(activeWindow.Handle))
            {
                _edgeTriggerWindow?.HideTrigger();
                return;
            }

            target = ResolveTargetForWindow(activeWindow);
            _lastForegroundWindow = target.Window;
            _lastFollowTarget = target;
        }

        var targetWindow = target.Window;
        if (targetWindow.Handle == nint.Zero || !IsWindow(targetWindow.Handle))
        {
            _edgeTriggerWindow?.HideTrigger();
            return;
        }

        if (!GetWindowRect(targetWindow.Handle, out var targetRect) || !GetCursorPos(out var cursor))
        {
            _edgeTriggerWindow?.HideTrigger();
            return;
        }

        if (!IsCursorNearTargetEdge(cursor, targetRect, out var anchorRight))
        {
            _edgeTriggerWindow?.HideTrigger();
            return;
        }

        var sidecarHwnd = WindowNative.GetWindowHandle(this);
        var sidecarId = Win32Interop.GetWindowIdFromWindow(sidecarHwnd);
        var workArea = GetTargetWorkArea(sidecarId, targetWindow.Handle);
        var size = new SizeInt32(FloatingTriggerWidth, FloatingTriggerHeight);
        var x = anchorRight
            ? targetRect.Right - (FloatingTriggerWidth / 2)
            : targetRect.Left - (FloatingTriggerWidth / 2);
        var y = cursor.Y - (FloatingTriggerHeight / 2);

        x = ClampToArea(x, workArea.X + SidecarScreenMargin, workArea.X + workArea.Width - FloatingTriggerWidth - SidecarScreenMargin);
        y = ClampToArea(y, workArea.Y + SidecarScreenMargin, workArea.Y + workArea.Height - FloatingTriggerHeight - SidecarScreenMargin);

        EnsureEdgeTriggerWindow().ShowAt(new PointInt32(x, y), size);
    }

    private ActiveWindowSnapshot? GetCurrentNonThreadlineWindow()
    {
        var activeWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        if (activeWindow is null || IsThreadlineWindow(activeWindow)) return null;
        return activeWindow;
    }

    private static bool IsCursorNearTargetEdge(NativePoint cursor, NativeRect targetRect, out bool anchorRight)
    {
        var withinVerticalBand = cursor.Y >= targetRect.Top && cursor.Y <= targetRect.Bottom;
        var nearRight = Math.Abs(cursor.X - targetRect.Right) <= FloatingTriggerHoverZone;
        var nearLeft = Math.Abs(cursor.X - targetRect.Left) <= FloatingTriggerHoverZone;

        anchorRight = nearRight || !nearLeft;
        return withinVerticalBand && (nearRight || nearLeft);
    }

    private void SetSidecarVisualState()
    {
        try
        {
            EdgeHandlePanel.Visibility = Visibility.Collapsed;
            ChatShellPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            // Visual state can be requested during startup before every named XAML control is ready.
        }
    }

    private ThreadlineTarget? GetBestSidecarTarget()
    {
        if (_isTargetLocked && _lockedFollowTarget is not null) return _lockedFollowTarget;
        if (_selectedThreadlineTarget is not null) return _selectedThreadlineTarget;
        if (_lastFollowTarget is not null) return _lastFollowTarget;
        return null;
    }

    private void PlaceSidecarForTarget(ThreadlineTarget? target, string reason)
    {
        if (!_attachSidecarToTarget)
        {
            DockSidecarToScreen(_sidecarWindowHiddenForTrigger
                ? "Sidecar: Floating trigger armed in screen dock mode."
                : "Sidecar: Screen dock mode. Attach is off.");
            return;
        }

        if (target is null)
        {
            DockSidecarToScreen(_sidecarWindowHiddenForTrigger
                ? "Sidecar: Floating trigger armed; waiting for a target window edge hover."
                : "Sidecar: Attach mode on; waiting for a target window.");
            return;
        }

        if (TryAttachSidecarToTarget(target.Window, target.Title, reason))
        {
            return;
        }

        DockSidecarToScreen($"Sidecar: Could not attach to {target.Window.ApplicationName}; using screen dock fallback.");
    }

    private bool TryAttachSidecarToTarget(ActiveWindowSnapshot targetWindow, string targetTitle, string reason)
    {
        try
        {
            if (targetWindow.Handle == nint.Zero || !IsWindow(targetWindow.Handle))
            {
                return false;
            }

            if (!GetWindowRect(targetWindow.Handle, out var targetRect))
            {
                return false;
            }

            var sidecarHwnd = WindowNative.GetWindowHandle(this);
            var sidecarId = Win32Interop.GetWindowIdFromWindow(sidecarHwnd);
            var appWindow = AppWindow.GetFromWindowId(sidecarId);
            var workArea = GetTargetWorkArea(sidecarId, targetWindow.Handle);

            var maxWidth = Math.Max(1, workArea.Width - (SidecarScreenMargin * 2));
            var width = ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, maxWidth);

            var maxHeight = Math.Max(1, workArea.Height - (SidecarScreenMargin * 2));
            var height = ClampToArea(Math.Max(targetRect.Height, SidecarMinimumHeight), SidecarMinimumHeight, maxHeight);

            var minY = workArea.Y + SidecarScreenMargin;
            var maxY = workArea.Y + workArea.Height - height - SidecarScreenMargin;
            var y = ClampToArea(targetRect.Top, minY, Math.Max(minY, maxY));

            var rightX = targetRect.Right + SidecarScreenMargin;
            var leftX = targetRect.Left - width - SidecarScreenMargin;
            var screenRightX = workArea.X + workArea.Width - width - SidecarScreenMargin;

            var x = rightX + width <= workArea.X + workArea.Width
                ? rightX
                : leftX >= workArea.X
                    ? leftX
                    : screenRightX;

            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(x, y));

            var side = x == rightX ? "right" : x == leftX ? "left" : "screen edge";
            UpdateSidecarAttachmentStatus($"Sidecar: Attached chat on {side} of {targetWindow.ApplicationName} — {targetTitle}. {reason}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private RectInt32 GetTargetWorkArea(WindowId sidecarId, nint targetHandle)
    {
        try
        {
            var targetId = Win32Interop.GetWindowIdFromWindow(targetHandle);
            return DisplayArea.GetFromWindowId(targetId, DisplayAreaFallback.Nearest).WorkArea;
        }
        catch
        {
            return DisplayArea.GetFromWindowId(sidecarId, DisplayAreaFallback.Nearest).WorkArea;
        }
    }

    private void DockSidecarToScreen(string status)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var win = AppWindow.GetFromWindowId(id);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
            var maxWidth = Math.Max(1, area.Width - (SidecarScreenMargin * 2));
            var maxHeight = Math.Max(1, area.Height - (SidecarScreenMargin * 2));
            var width = ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, maxWidth);
            var height = ClampToArea(area.Height - 80, SidecarMinimumHeight, maxHeight);
            var y = area.Y + SidecarScreenTopOffset;

            win.Resize(new SizeInt32(width, height));
            win.Move(new PointInt32(area.X + area.Width - width - 24, y));
            UpdateSidecarAttachmentStatus(status);
        }
        catch
        {
            UpdateSidecarAttachmentStatus(status);
        }
    }

    private void UpdateSidecarAttachmentStatus(string message)
    {
        try
        {
            SidecarAttachmentText.Text = message;
        }
        catch
        {
            // The placement helper may run during startup before every named XAML control is ready.
        }
    }

    private static int ClampToArea(int value, int minimum, int maximum)
    {
        var safeMaximum = Math.Max(1, maximum);
        var safeMinimum = Math.Min(Math.Max(1, minimum), safeMaximum);
        return Math.Clamp(value, safeMinimum, safeMaximum);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Math.Max(1, Right - Left);
        public int Height => Math.Max(1, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
