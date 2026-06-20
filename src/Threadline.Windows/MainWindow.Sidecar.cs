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
    private const int PreferredSidecarWidth = 740;
    private const int MinimumSidecarWidth = 560;
    private const int MinimumSidecarHeight = 640;
    private const int SidecarGap = 12;
    private const int SidecarScreenMargin = 24;

    private AppWindow? _sidecarAppWindow;
    private bool _isAttachedSidecarEnabled = true;
    private string? _lastSidecarAttachedTargetId;

    private void ConfigureSidecarWindow()
    {
        try
        {
            _sidecarAppWindow = GetSidecarAppWindow();
            DockSidecarToScreen();
            UpdateSidecarAttachmentControls("Sidecar: attached mode waiting for target.", attachedModeEnabled: true);
        }
        catch
        {
            // Window placement is a convenience layer. A placement failure should never stop the sidecar from opening.
        }
    }

    private void ToggleAttachSidecar_Click(object sender, RoutedEventArgs e)
    {
        _isAttachedSidecarEnabled = !_isAttachedSidecarEnabled;
        _lastSidecarAttachedTargetId = null;

        if (!_isAttachedSidecarEnabled)
        {
            DockSidecarToScreen();
            UpdateSidecarAttachmentControls("Sidecar: screen docked. It will not follow the active target.", attachedModeEnabled: false);
            AddTimeline("Sidecar switched to screen dock mode.");
            AppendTranscript("Threadline Sidecar", "Screen dock mode is on. The sidecar will stay parked at the edge of the screen instead of attaching beside the active target.");
            return;
        }

        if (TryAttachSidecarToBestTarget(force: true))
        {
            AddTimeline("Sidecar attach mode resumed.");
            AppendTranscript("Threadline Sidecar", "Attached mode is on. The sidecar will follow the selected, locked, or last active target window.");
            return;
        }

        UpdateSidecarAttachmentControls("Sidecar: attached mode on; waiting for a non-Threadline target.", attachedModeEnabled: true);
        AddTimeline("Sidecar attach mode is waiting for a target.");
        AppendTranscript("Threadline Sidecar", "Attached mode is on. Switch to another app or choose a target, then the sidecar will attach beside it.");
    }

    private bool TryAttachSidecarToBestTarget(bool force = false)
    {
        if (!_isAttachedSidecarEnabled) return false;

        var target = GetBestSidecarTarget();
        return AttachSidecarToTarget(target, force);
    }

    private bool AttachSidecarToTarget(ThreadlineTarget? target, bool force = false)
    {
        if (!_isAttachedSidecarEnabled || target is null) return false;

        if (!TryPlaceSidecarBesideWindow(target.Window, out var placement))
        {
            DockSidecarToScreen();
            UpdateSidecarAttachmentControls("Sidecar: could not read target bounds; using screen dock fallback.", attachedModeEnabled: true);
            return false;
        }

        var isNewAttachment = force || !string.Equals(_lastSidecarAttachedTargetId, target.Id, StringComparison.OrdinalIgnoreCase);
        _lastSidecarAttachedTargetId = target.Id;
        UpdateSidecarAttachmentControls($"Sidecar: attached to {target.Window.ApplicationName} — {target.Title}\n{placement}", attachedModeEnabled: true);

        if (isNewAttachment)
        {
            AddTimeline($"Attached sidecar beside {target.Window.ApplicationName}: {target.Title}");
        }

        return true;
    }

    private ThreadlineTarget? GetBestSidecarTarget()
    {
        if (_isTargetLocked && _lockedFollowTarget is not null) return _lockedFollowTarget;
        if (_selectedThreadlineTarget is not null) return _selectedThreadlineTarget;
        if (_lastFollowTarget is not null) return _lastFollowTarget;
        return null;
    }

    private bool TryPlaceSidecarBesideWindow(ActiveWindowSnapshot window, out string placement)
    {
        placement = "Placement unavailable.";
        if (window.Handle == nint.Zero || !GetWindowRect(window.Handle, out var targetBounds))
        {
            return false;
        }

        if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
        {
            return false;
        }

        var targetWindowId = Win32Interop.GetWindowIdFromWindow(window.Handle);
        var area = DisplayArea.GetFromWindowId(targetWindowId, DisplayAreaFallback.Nearest).WorkArea;

        var maxWidth = Math.Max(360, area.Width - (SidecarScreenMargin * 2));
        var width = Math.Min(PreferredSidecarWidth, maxWidth);
        if (maxWidth >= MinimumSidecarWidth)
        {
            width = Math.Max(width, MinimumSidecarWidth);
        }

        var maxHeight = Math.Max(420, area.Height - (SidecarScreenMargin * 2));
        var height = Math.Min(maxHeight, Math.Max(MinimumSidecarHeight, targetBounds.Height));

        var workLeft = area.X;
        var workTop = area.Y;
        var workRight = area.X + area.Width;
        var workBottom = area.Y + area.Height;

        string side;
        int x;
        if (targetBounds.Right + SidecarGap + width <= workRight)
        {
            side = "right of target";
            x = targetBounds.Right + SidecarGap;
        }
        else if (targetBounds.Left - SidecarGap - width >= workLeft)
        {
            side = "left of target";
            x = targetBounds.Left - SidecarGap - width;
        }
        else
        {
            side = "screen-right overlay";
            x = workRight - width - SidecarScreenMargin;
        }

        var y = ClampInt(targetBounds.Top, workTop + SidecarScreenMargin, workBottom - height - SidecarScreenMargin);

        var win = GetSidecarAppWindow();
        win.Resize(new SizeInt32(width, height));
        win.Move(new PointInt32(x, y));

        placement = $"Placement: {side} • {width}x{height}";
        return true;
    }

    private void DockSidecarToScreen()
    {
        try
        {
            var win = GetSidecarAppWindow();
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;

            var maxWidth = Math.Max(360, area.Width - (SidecarScreenMargin * 2));
            var width = Math.Min(PreferredSidecarWidth, maxWidth);
            if (maxWidth >= MinimumSidecarWidth)
            {
                width = Math.Max(width, MinimumSidecarWidth);
            }

            var height = Math.Min(Math.Max(MinimumSidecarHeight, area.Height - 80), Math.Max(420, area.Height - (SidecarScreenMargin * 2)));
            win.Resize(new SizeInt32(width, height));
            win.Move(new PointInt32(area.X + area.Width - width - SidecarScreenMargin, area.Y + SidecarScreenMargin));
        }
        catch
        {
            // Non-fatal placement fallback.
        }
    }

    private AppWindow GetSidecarAppWindow()
    {
        if (_sidecarAppWindow is not null)
        {
            return _sidecarAppWindow;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        _sidecarAppWindow = AppWindow.GetFromWindowId(id);
        return _sidecarAppWindow;
    }

    private void UpdateSidecarAttachmentControls(string status, bool attachedModeEnabled)
    {
        try
        {
            SidecarAttachmentText.Text = status;
            AttachSidecarButton.Content = attachedModeEnabled ? "Screen Dock" : "Attach Sidecar";
        }
        catch
        {
            // The placement status is helpful but not required for app function.
        }
    }

    private static int ClampInt(int value, int minimum, int maximum)
    {
        if (maximum < minimum) return minimum;
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
}
