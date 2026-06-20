using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Threadline.Windows.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int SidecarDefaultWidth = 520;
    private const int SidecarMinimumWidth = 420;
    private const int SidecarMinimumHeight = 620;
    private const int SidecarScreenMargin = 12;
    private const int SidecarScreenTopOffset = 40;

    private bool _attachSidecarToTarget = true;

    private void ConfigureSidecarWindow()
    {
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
            DockSidecarToScreen("Sidecar: Screen dock mode. Attach is off.");
            return;
        }

        if (target is null)
        {
            DockSidecarToScreen("Sidecar: Attach mode on; waiting for a target window.");
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
            var preferredHeight = Math.Max(targetRect.Height, SidecarMinimumHeight);
            var height = ClampToArea(preferredHeight, SidecarMinimumHeight, maxHeight);

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
            UpdateSidecarAttachmentStatus($"Sidecar: Attached {side} of {targetWindow.ApplicationName} — {targetTitle}. {reason}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private DisplayArea GetTargetWorkArea(WindowId sidecarId, nint targetHandle)
    {
        try
        {
            var targetId = Win32Interop.GetWindowIdFromWindow(targetHandle);
            return DisplayArea.GetFromWindowId(targetId, DisplayAreaFallback.Nearest);
        }
        catch
        {
            return DisplayArea.GetFromWindowId(sidecarId, DisplayAreaFallback.Nearest);
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
            var width = ClampToArea(SidecarDefaultWidth, SidecarMinimumWidth, Math.Max(1, area.Width - (SidecarScreenMargin * 2)));
            var height = ClampToArea(area.Height - 80, SidecarMinimumHeight, Math.Max(1, area.Height - (SidecarScreenMargin * 2)));
            win.Resize(new SizeInt32(width, height));
            win.Move(new PointInt32(area.X + area.Width - width - 24, area.Y + SidecarScreenTopOffset));
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
