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
    private const int SidecarDefaultWidth = 430;
    private const int SidecarMinimumWidth = 360;
    private const int SidecarMinimumHeight = 620;
    private const int SidecarHandleWidth = 44;
    private const int SidecarHandleHeight = 180;
    private const int SidecarScreenMargin = 12;
    private const int SidecarScreenTopOffset = 40;

    private bool _attachSidecarToTarget = true;
    private bool _sidecarCollapsedToHandle;

    private void ConfigureSidecarWindow()
    {
        SetSidecarVisualState();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Initial sidecar placement.");
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
        _sidecarCollapsedToHandle = true;
        SetSidecarVisualState();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Collapsed to edge handle.");
        AddTimeline("Sidecar collapsed to edge handle.");
    }

    private void ExpandSidecarFromHandle(object sender, PointerRoutedEventArgs e)
    {
        if (!_sidecarCollapsedToHandle) return;

        _sidecarCollapsedToHandle = false;
        SetSidecarVisualState();
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Restored from edge handle.");
        AddTimeline("Sidecar restored from edge handle.");
    }

    private void SetSidecarVisualState()
    {
        try
        {
            EdgeHandlePanel.Visibility = _sidecarCollapsedToHandle ? Visibility.Visible : Visibility.Collapsed;
            ChatShellPanel.Visibility = _sidecarCollapsedToHandle ? Visibility.Collapsed : Visibility.Visible;
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
            DockSidecarToScreen(_sidecarCollapsedToHandle
                ? "Sidecar: Edge handle visible in screen dock mode."
                : "Sidecar: Screen dock mode. Attach is off.");
            return;
        }

        if (target is null)
        {
            DockSidecarToScreen(_sidecarCollapsedToHandle
                ? "Sidecar: Edge handle visible; waiting for a target window."
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
            var width = _sidecarCollapsedToHandle
                ? ClampToArea(SidecarHandleWidth, SidecarHandleWidth, maxWidth)
                : ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, maxWidth);

            var maxHeight = Math.Max(1, workArea.Height - (SidecarScreenMargin * 2));
            var height = _sidecarCollapsedToHandle
                ? ClampToArea(SidecarHandleHeight, SidecarHandleHeight, maxHeight)
                : ClampToArea(Math.Max(targetRect.Height, SidecarMinimumHeight), SidecarMinimumHeight, maxHeight);

            var minY = workArea.Y + SidecarScreenMargin;
            var maxY = workArea.Y + workArea.Height - height - SidecarScreenMargin;
            var desiredY = _sidecarCollapsedToHandle
                ? targetRect.Top + ((targetRect.Height - height) / 2)
                : targetRect.Top;
            var y = ClampToArea(desiredY, minY, Math.Max(minY, maxY));

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
            var mode = _sidecarCollapsedToHandle ? "Edge handle" : "Attached chat";
            UpdateSidecarAttachmentStatus($"Sidecar: {mode} on {side} of {targetWindow.ApplicationName} — {targetTitle}. {reason}");
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
            var width = _sidecarCollapsedToHandle
                ? ClampToArea(SidecarHandleWidth, SidecarHandleWidth, maxWidth)
                : ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, maxWidth);
            var height = _sidecarCollapsedToHandle
                ? ClampToArea(SidecarHandleHeight, SidecarHandleHeight, maxHeight)
                : ClampToArea(area.Height - 80, SidecarMinimumHeight, maxHeight);
            var y = _sidecarCollapsedToHandle
                ? area.Y + Math.Max(SidecarScreenMargin, (area.Height - height) / 2)
                : area.Y + SidecarScreenTopOffset;

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

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);
}
