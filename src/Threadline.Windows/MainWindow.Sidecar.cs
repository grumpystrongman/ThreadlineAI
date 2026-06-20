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
    private const int ShowWindowRestore = 9;
    private const int LeftMouseButtonVirtualKey = 0x01;
    private const int SidecarDefaultWidth = 430;
    private const int SidecarMinimumWidth = 360;
    private const int SidecarMinimumHeight = 620;
    private const int TargetMinimumWidth = 420;
    private const int SidecarAttachGap = 8;
    private const int FloatingTriggerWidth = 64;
    private const int FloatingTriggerHeight = 144;
    private const int FloatingTriggerHoverZone = 116;
    private const int FloatingTriggerInsetFromEdge = 96;
    private const int FloatingTriggerReachPadding = 220;
    private const int FloatingTriggerHideGraceMilliseconds = 1800;
    private const int SidecarScreenMargin = 12;
    private const int SidecarScreenTopOffset = 40;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint GetAncestorRoot = 2;

    private readonly DispatcherTimer _edgeHoverTimer = new();
    private EdgeTriggerWindow? _edgeTriggerWindow;
    private ThreadlineTarget? _floatingTriggerTarget;
    private DateTimeOffset _lastFloatingTriggerEligibleAt = DateTimeOffset.MinValue;
    private string? _attachedSidecarTargetId;
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

        _edgeHoverTimer.Interval = TimeSpan.FromMilliseconds(75);
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
        var openingTarget = _floatingTriggerTarget;
        if (openingTarget is not null)
        {
            if (IsSidecarOpenAgainstDifferentTarget(openingTarget))
            {
                _edgeTriggerWindow?.HideTrigger();
                ShowMainSidecarWindow();
                SetSidecarVisualState();
                ShowPendingConnection(openingTarget);
                return;
            }

            var isNewTarget = !string.Equals(_attachedSidecarTargetId, openingTarget.Id, StringComparison.OrdinalIgnoreCase);
            AttachSidecarToWindowTarget(openingTarget, clearDraft: isNewTarget, "Opened from floating edge trigger.");
        }

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
            _ = ShowWindow(WindowNative.GetWindowHandle(this), ShowWindowRestore);
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
        var trigger = EnsureEdgeTriggerWindow();

        if (!_attachSidecarToTarget)
        {
            trigger.HideTrigger();
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            HideFloatingTriggerIfGraceExpired(trigger);
            return;
        }

        if (trigger.IsVisible && trigger.IsCursorWithinReach(cursor.X, cursor.Y, 12) && IsLeftMouseButtonDown())
        {
            _lastFloatingTriggerEligibleAt = DateTimeOffset.Now;
            RestoreSidecarFromFloatingTrigger();
            return;
        }

        if (trigger.IsPointerInside || trigger.IsCursorWithinReach(cursor.X, cursor.Y, FloatingTriggerReachPadding))
        {
            _lastFloatingTriggerEligibleAt = DateTimeOffset.Now;
            return;
        }

        var candidateWindow = GetPreferredWindowForCursor(cursor);
        ThreadlineTarget? target = null;

        if (candidateWindow is not null && candidateWindow.Handle != nint.Zero && IsWindow(candidateWindow.Handle))
        {
            target = ResolveTargetForWindow(candidateWindow);
        }
        else if (_sidecarWindowHiddenForTrigger)
        {
            target = GetBestSidecarTarget();
        }

        if (target is null)
        {
            HideFloatingTriggerIfGraceExpired(trigger);
            return;
        }

        var targetWindow = target.Window;
        if (targetWindow.Handle == nint.Zero || !IsWindow(targetWindow.Handle))
        {
            HideFloatingTriggerIfGraceExpired(trigger);
            return;
        }

        if (!GetWindowRect(targetWindow.Handle, out var targetRect))
        {
            HideFloatingTriggerIfGraceExpired(trigger);
            return;
        }

        if (!IsCursorNearTargetEdge(cursor, targetRect, out var anchorRight))
        {
            HideFloatingTriggerIfGraceExpired(trigger);
            return;
        }

        var sidecarHwnd = WindowNative.GetWindowHandle(this);
        var sidecarId = Win32Interop.GetWindowIdFromWindow(sidecarHwnd);
        var workArea = GetTargetWorkArea(sidecarId, targetWindow.Handle);
        var size = new SizeInt32(FloatingTriggerWidth, FloatingTriggerHeight);
        var x = anchorRight
            ? targetRect.Right - FloatingTriggerWidth - FloatingTriggerInsetFromEdge
            : targetRect.Left + FloatingTriggerInsetFromEdge;
        var y = cursor.Y - (FloatingTriggerHeight / 2);

        x = ClampToArea(x, workArea.X + SidecarScreenMargin, workArea.X + workArea.Width - FloatingTriggerWidth - SidecarScreenMargin);
        y = ClampToArea(y, workArea.Y + SidecarScreenMargin, workArea.Y + workArea.Height - FloatingTriggerHeight - SidecarScreenMargin);

        _floatingTriggerTarget = target;
        _lastFloatingTriggerEligibleAt = DateTimeOffset.Now;
        trigger.ShowAt(new PointInt32(x, y), size);
    }

    private void HideFloatingTriggerIfGraceExpired(EdgeTriggerWindow trigger)
    {
        if (!trigger.IsVisible)
        {
            return;
        }

        var elapsed = DateTimeOffset.Now - _lastFloatingTriggerEligibleAt;
        if (elapsed.TotalMilliseconds <= FloatingTriggerHideGraceMilliseconds)
        {
            return;
        }

        trigger.HideTrigger();
    }

    private ActiveWindowSnapshot? GetPreferredWindowForCursor(NativePoint cursor)
    {
        var activeWindow = GetCurrentNonThreadlineWindow();
        if (activeWindow is not null && activeWindow.Handle != nint.Zero && IsWindow(activeWindow.Handle))
        {
            if (GetWindowRect(activeWindow.Handle, out var activeRect) && IsCursorNearTargetEdge(cursor, activeRect, out _))
            {
                return activeWindow;
            }
        }

        var cursorWindow = GetCursorNonThreadlineWindow(cursor);
        if (cursorWindow is not null && !IsShellOrDesktopWindow(cursorWindow))
        {
            return cursorWindow;
        }

        return activeWindow;
    }

    private ActiveWindowSnapshot? GetCurrentNonThreadlineWindow()
    {
        var activeWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        if (activeWindow is null || IsThreadlineWindow(activeWindow) || IsShellOrDesktopWindow(activeWindow)) return null;
        return activeWindow;
    }

    private ActiveWindowSnapshot? GetCursorNonThreadlineWindow(NativePoint cursor)
    {
        try
        {
            var handle = WindowFromPoint(cursor);
            if (handle == nint.Zero) return null;

            var rootHandle = GetAncestor(handle, GetAncestorRoot);
            if (rootHandle == nint.Zero) rootHandle = handle;
            if (rootHandle == nint.Zero || !IsWindow(rootHandle)) return null;

            var snapshot = _activeWindowMonitor.GetWindowSnapshot(rootHandle);
            return IsThreadlineWindow(snapshot) ? null : snapshot;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCursorNearTargetEdge(NativePoint cursor, NativeRect targetRect, out bool anchorRight)
    {
        var withinVerticalBand = cursor.Y >= targetRect.Top && cursor.Y <= targetRect.Bottom;
        var nearRight = cursor.X >= targetRect.Right - FloatingTriggerHoverZone && cursor.X <= targetRect.Right + FloatingTriggerHoverZone;
        var nearLeft = cursor.X >= targetRect.Left - FloatingTriggerHoverZone && cursor.X <= targetRect.Left + FloatingTriggerHoverZone;

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
            EnsureSidecarWindowUserResizable(appWindow);
            var workArea = GetTargetWorkArea(sidecarId, targetWindow.Handle);
            var maxWidth = Math.Max(1, workArea.Width - (SidecarScreenMargin * 2));
            var sidecarWidth = ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, maxWidth);

            if (!_sidecarWindowHiddenForTrigger)
            {
                targetRect = EnsureSideBySideTargetSpace(targetWindow.Handle, targetRect, workArea, sidecarWidth);
            }

            var maxHeight = Math.Max(1, workArea.Height - (SidecarScreenMargin * 2));
            var requestedHeight = Math.Max(targetRect.Height, SidecarMinimumHeight);
            var sidecarHeight = ClampToArea(requestedHeight, Math.Min(SidecarMinimumHeight, maxHeight), maxHeight);
            var y = ClampToArea(targetRect.Top, workArea.Y + SidecarScreenMargin, workArea.Y + workArea.Height - sidecarHeight - SidecarScreenMargin);

            var rightX = targetRect.Right + SidecarAttachGap;
            var leftX = targetRect.Left - sidecarWidth - SidecarAttachGap;
            var screenRightX = workArea.X + workArea.Width - sidecarWidth - SidecarScreenMargin;

            var x = rightX + sidecarWidth <= workArea.X + workArea.Width - SidecarScreenMargin
                ? rightX
                : leftX >= workArea.X + SidecarScreenMargin
                    ? leftX
                    : screenRightX;

            appWindow.Resize(new SizeInt32(sidecarWidth, sidecarHeight));
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

    private static void EnsureSidecarWindowUserResizable(AppWindow appWindow)
    {
        try
        {
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
            }
        }
        catch
        {
            // Keep placement working even if presenter flags are unavailable on a given Windows App SDK runtime.
        }
    }

    private NativeRect EnsureSideBySideTargetSpace(nint targetHandle, NativeRect targetRect, RectInt32 workArea, int sidecarWidth)
    {
        var workLeft = workArea.X + SidecarScreenMargin;
        var workTop = workArea.Y + SidecarScreenMargin;
        var workRight = workArea.X + workArea.Width - SidecarScreenMargin;
        var workBottom = workArea.Y + workArea.Height - SidecarScreenMargin;
        var requiredRight = targetRect.Right + SidecarAttachGap + sidecarWidth;

        if (requiredRight <= workRight && targetRect.Left >= workLeft && targetRect.Bottom <= workBottom && targetRect.Top >= workTop)
        {
            return targetRect;
        }

        var availableTargetWidth = Math.Max(1, workArea.Width - sidecarWidth - SidecarAttachGap - (SidecarScreenMargin * 2));
        var maxTargetHeight = Math.Max(1, workArea.Height - (SidecarScreenMargin * 2));
        var targetWidth = ClampToArea(targetRect.Width, Math.Min(TargetMinimumWidth, availableTargetWidth), availableTargetWidth);
        var targetHeight = ClampToArea(targetRect.Height, 1, maxTargetHeight);
        var targetLeft = workRight - sidecarWidth - SidecarAttachGap - targetWidth;
        var targetTop = ClampToArea(targetRect.Top, workTop, workBottom - targetHeight);

        targetLeft = ClampToArea(targetLeft, workLeft, Math.Max(workLeft, workRight - sidecarWidth - SidecarAttachGap - targetWidth));

        // Maximized windows ignore normal move/resize calls. Restore first so Threadline can create a Copilot-style side-by-side layout.
        _ = ShowWindow(targetHandle, ShowWindowRestore);
        _ = SetWindowPos(
            targetHandle,
            nint.Zero,
            targetLeft,
            targetTop,
            targetWidth,
            targetHeight,
            SetWindowPosNoZOrder | SetWindowPosNoActivate);

        return GetWindowRect(targetHandle, out var updatedRect) ? updatedRect : new NativeRect
        {
            Left = targetLeft,
            Top = targetTop,
            Right = targetLeft + targetWidth,
            Bottom = targetTop + targetHeight
        };
    }

    private RectInt32 GetTargetWorkArea(WindowId sidecarId, nint targetHandle)
    {
        try
        {
            var monitor = MonitorFromWindow(targetHandle, MonitorDefaultToNearest);
            if (monitor != nint.Zero)
            {
                var monitorInfo = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    return new RectInt32(
                        monitorInfo.rcWork.Left,
                        monitorInfo.rcWork.Top,
                        monitorInfo.rcWork.Width,
                        monitorInfo.rcWork.Height);
                }
            }
        }
        catch
        {
            // Fall back to Windows App SDK display area lookup below.
        }

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
            EnsureSidecarWindowUserResizable(win);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
            var maxWidth = Math.Max(1, area.Width - (SidecarScreenMargin * 2));
            var maxHeight = Math.Max(1, area.Height - (SidecarScreenMargin * 2));
            var width = ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, maxWidth);
            var height = ClampToArea(area.Height - 80, Math.Min(SidecarMinimumHeight, maxHeight), maxHeight);
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

    private static bool IsShellOrDesktopWindow(ActiveWindowSnapshot window)
    {
        var process = window.ProcessName ?? string.Empty;
        var title = window.WindowTitle ?? string.Empty;
        return string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(title) || string.Equals(title, "Program Manager", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLeftMouseButtonDown() =>
        (GetAsyncKeyState(LeftMouseButtonVirtualKey) & unchecked((short)0x8000)) != 0;

    private static int ClampToArea(int value, int minimum, int maximum)
    {
        if (maximum < minimum)
        {
            return minimum;
        }

        return Math.Clamp(value, minimum, maximum);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
