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
    private enum SidecarDockingBehavior
    {
        ResizeCurrentWindowToMakeRoom,
        ScreenDock
    }

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
    private const int FloatingTriggerHoverZone = 24;
    private const int FloatingTriggerInsetFromEdge = 24;
    private const int FloatingTriggerReachPadding = 96;
    private const int SidecarScreenMargin = 12;
    private const int SidecarScreenTopOffset = 40;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly DispatcherTimer _edgeHoverTimer = new();
    private EdgeTriggerWindow? _edgeTriggerWindow;
    private ThreadlineTarget? _floatingTriggerTarget;
    private string? _attachedSidecarTargetId;
    private bool _attachSidecarToTarget = true;
    private SidecarDockingBehavior _sidecarDockingBehavior = SidecarDockingBehavior.ResizeCurrentWindowToMakeRoom;
    private bool _sidecarCollapsedToHandle = true;
    private bool _sidecarWindowHiddenForTrigger;

    private void ConfigureSidecarWindow()
    {
        SetSidecarVisualState();
        UpdateAttachSidecarButtonLabel();
        StartFloatingEdgeTrigger();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Initial sidecar placement.");

        try
        {
            RootShell.Loaded += (_, _) =>
            {
                if (_sidecarCollapsedToHandle)
                {
                    _ = RootShell.DispatcherQueue.TryEnqueue(ShowCollapsedSidecarHandleAtScreenEdge);
                }
                else if (_sidecarWindowHiddenForTrigger)
                {
                    _ = RootShell.DispatcherQueue.TryEnqueue(HideMainSidecarWindow);
                }
            };
        }
        catch
        {
            // If delayed placement cannot be registered, fall back to the immediate attempt below.
        }

        if (_sidecarCollapsedToHandle)
        {
            _sidecarWindowHiddenForTrigger = false;
            ShowCollapsedSidecarHandleAtScreenEdge();
        }
        else if (_sidecarWindowHiddenForTrigger)
        {
            HideMainSidecarWindow();
        }
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
        _sidecarDockingBehavior = _attachSidecarToTarget
            ? SidecarDockingBehavior.ResizeCurrentWindowToMakeRoom
            : SidecarDockingBehavior.ScreenDock;

        if (_attachSidecarToTarget)
        {
            PlaceSidecarForTarget(GetBestSidecarTarget(), "Resize-to-make-room sidecar mode enabled.");
            AddTimeline("Resize-to-make-room sidecar mode enabled.");
        }
        else
        {
            DockSidecarToScreen("Sidecar: Screen dock mode. Resize-to-make-room is off.");
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
        _sidecarWindowHiddenForTrigger = false;
        _floatingTriggerTarget = null;
        _edgeTriggerWindow?.HideTrigger();
        SetSidecarVisualState();
        ShowCollapsedSidecarHandleAtScreenEdge();
        AddTimeline("Sidecar collapsed to the visible edge handle. Hover near an app window edge or click the handle to reopen.");
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

        if (!_attachSidecarToTarget || !_sidecarCollapsedToHandle)
        {
            _floatingTriggerTarget = null;
            trigger.HideTrigger();
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            _floatingTriggerTarget = null;
            trigger.HideTrigger();
            return;
        }

        if (trigger.IsVisible && trigger.IsCursorWithinReach(cursor.X, cursor.Y, 12) && IsLeftMouseButtonDown())
        {
            RestoreSidecarFromFloatingTrigger();
            return;
        }

        if (trigger.IsPointerInside || trigger.IsCursorWithinReach(cursor.X, cursor.Y, FloatingTriggerReachPadding))
        {
            return;
        }

        if (!TryGetWindowNearCursorEdge(cursor, out var targetWindow, out var targetRect, out var anchorRight))
        {
            _floatingTriggerTarget = null;
            trigger.HideTrigger();
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

        _floatingTriggerTarget = CreateWindowTarget(targetWindow);
        trigger.ShowAt(new PointInt32(x, y), size);
    }

    private bool TryGetWindowNearCursorEdge(NativePoint cursor, out ActiveWindowSnapshot targetWindow, out NativeRect targetRect, out bool anchorRight)
    {
        ActiveWindowSnapshot? bestWindow = null;
        var bestRect = default(NativeRect);
        var bestAnchorRight = false;
        var bestDistance = int.MaxValue;

        EnumWindows((handle, _) =>
        {
            if (handle == nint.Zero || !IsWindow(handle) || !IsWindowVisible(handle)) return true;

            var snapshot = GetUsableWindowSnapshot(handle);
            if (snapshot is null) return true;
            if (!GetWindowRect(snapshot.Handle, out var rect)) return true;
            if (!IsCursorNearTargetEdge(cursor, rect, out var candidateAnchorRight)) return true;

            var distance = Math.Min(Math.Abs(cursor.X - rect.Left), Math.Abs(cursor.X - rect.Right));
            if (distance < bestDistance)
            {
                bestWindow = snapshot;
                bestRect = rect;
                bestAnchorRight = candidateAnchorRight;
                bestDistance = distance;
            }

            return true;
        }, nint.Zero);

        targetWindow = bestWindow!;
        targetRect = bestRect;
        anchorRight = bestAnchorRight;
        return bestWindow is not null;
    }

    private ActiveWindowSnapshot? GetUsableWindowSnapshot(nint handle)
    {
        if (handle == nint.Zero || !IsWindow(handle)) return null;

        var snapshot = _activeWindowMonitor.GetWindowSnapshot(handle);
        if (IsThreadlineWindow(snapshot) || IsShellOrDesktopWindow(snapshot)) return null;

        // Chromium and Electron apps often expose child/chrome HWNDs. Keep them if they resolve to
        // a real process even when the window title is sparse; the context resolver can still fall
        // back to native UI or extension-backed tab data.
        return string.IsNullOrWhiteSpace(snapshot.ProcessName) && string.IsNullOrWhiteSpace(snapshot.WindowTitle)
            ? null
            : snapshot;
    }

    private static bool IsCursorNearTargetEdge(NativePoint cursor, NativeRect targetRect, out bool anchorRight)
    {
        var withinVerticalBand = cursor.Y >= targetRect.Top - 12 && cursor.Y <= targetRect.Bottom + 12;
        var nearRight = cursor.X >= targetRect.Right - FloatingTriggerHoverZone && cursor.X <= targetRect.Right + FloatingTriggerHoverZone;
        var nearLeft = cursor.X >= targetRect.Left - FloatingTriggerHoverZone && cursor.X <= targetRect.Left + FloatingTriggerHoverZone;

        anchorRight = nearRight || !nearLeft;
        return withinVerticalBand && (nearRight || nearLeft);
    }

    private void SetSidecarVisualState()
    {
        try
        {
            if (_sidecarCollapsedToHandle)
            {
                EdgeHandlePanel.Visibility = Visibility.Visible;
                ChatShellPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                EdgeHandlePanel.Visibility = Visibility.Collapsed;
                ChatShellPanel.Visibility = Visibility.Visible;
            }
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
                : "Sidecar: Screen dock mode. Resize-to-make-room is off.");
            return;
        }

        if (target is null)
        {
            DockSidecarToScreen(_sidecarWindowHiddenForTrigger
                ? "Sidecar: Floating trigger armed; waiting for a target window edge hover."
                : "Sidecar: Resize-to-make-room mode on; waiting for a target window.");
            return;
        }

        if (TryAttachSidecarToTarget(target.Window, target.Title, reason))
        {
            return;
        }

        DockSidecarToScreen($"Sidecar: Could not resize {target.Window.ApplicationName}; using screen dock fallback.");
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

            if (!_sidecarWindowHiddenForTrigger && _sidecarDockingBehavior == SidecarDockingBehavior.ResizeCurrentWindowToMakeRoom)
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
            UpdateSidecarAttachmentStatus($"Sidecar: Resize-to-make-room on {side} of {targetWindow.ApplicationName} — {targetTitle}. {reason}");
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
        var targetLeft = workLeft;
        var targetTop = ClampToArea(targetRect.Top, workTop, workBottom - targetHeight);

        // Maximized windows ignore normal move/resize calls. Restore first so Threadline can create a Copilot-style side-by-side layout.
        _ = ShowWindow(targetHandle, ShowWindowRestore);
        var moved = SetWindowPos(
            targetHandle,
            nint.Zero,
            targetLeft,
            targetTop,
            targetWidth,
            targetHeight,
            SetWindowPosNoZOrder | SetWindowPosNoActivate);

        return moved && GetWindowRect(targetHandle, out var updatedRect) ? updatedRect : new NativeRect
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

    private static ThreadlineTarget CreateWindowTarget(ActiveWindowSnapshot window) =>
        new(
            $"window:{window.Handle}",
            ThreadlineTargetKind.Window,
            window,
            window.WindowTitle ?? window.ApplicationName,
            "native-ui",
            true,
            true,
            "medium",
            "Generic app window target. Threadline may use native UI fallback unless a better provider is available.");

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

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

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
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

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