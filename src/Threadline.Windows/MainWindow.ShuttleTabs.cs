using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Threadline.Windows.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int ShuttleTabWidth = 24;
    private const int ShuttleTabHeight = 72;
    private const int ShuttleTabMinimumWindowWidth = 240;
    private const int ShuttleTabMinimumWindowHeight = 160;
    private const int ShuttleTabMaximumVisibleWindows = 16;
    private const int ShuttleTabVerticalMargin = 24;
    private const int ShuttleTabVerticalSearchStep = 28;
    private const int ShuttleTabEdgeOverlap = 4;
    private const int ShuttleTabScreenMargin = 1;
    private const int GetWindowOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const int DwmWindowAttributeCloaked = 14;

    private readonly DispatcherTimer _shuttleTabTimer = new();
    private readonly List<ShuttleTabWindow> _shuttleTabs = new();
    private readonly Dictionary<ShuttleTabWindow, ThreadlineTarget> _shuttleTargets = new();

    private readonly record struct ShuttleTabPlacement(ThreadlineTarget Target, PointInt32 Location);

    private void StartShuttleTabs()
    {
        _edgeHoverTimer.Stop();
        _edgeTriggerWindow?.HideTrigger();
        if (_edgeTriggerWindow is not null)
        {
            _edgeTriggerWindow.DirectWindowHoverEnabled = false;
        }

        UpdateShuttleTerminology(
            SidecarAttachmentText,
            WorkThreadStatusText,
            CurrentContextText,
            ReceiptTrustText,
            ReceiptSourceText,
            TrustControlStatusText);

        if (_shuttleTabTimer.IsEnabled)
        {
            return;
        }

        _shuttleTabTimer.Interval = TimeSpan.FromMilliseconds(750);
        _shuttleTabTimer.Tick += (_, _) => SafeUpdateShuttleTabs();
        _shuttleTabTimer.Start();
        SafeUpdateShuttleTabs();
    }

    private static void UpdateShuttleTerminology(
        TextBlock sidecarAttachmentText,
        TextBlock workThreadStatusText,
        TextBlock currentContextText,
        TextBlock receiptTrustText,
        TextBlock receiptSourceText,
        TextBlock trustControlStatusText)
    {
        try
        {
            sidecarAttachmentText.Text = "Loom ready. Click a Shuttle tab to open a Warp Thread.";
            workThreadStatusText.Text = "Loom: ready";
            currentContextText.Text = "Choose a Shuttle tab or continue with the current Warp Thread.";
            receiptTrustText.Text = "Lineage on";
            receiptSourceText.Text = "No Shuttle yet";
            trustControlStatusText.Text = "Lineage";
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

        var placements = GetShuttleTabPlacements();
        EnsureShuttleTabCapacity(placements.Count);
        _shuttleTargets.Clear();

        for (var index = 0; index < _shuttleTabs.Count; index++)
        {
            var tab = _shuttleTabs[index];
            if (index >= placements.Count)
            {
                tab.Hide();
                continue;
            }

            var placement = placements[index];
            _shuttleTargets[tab] = placement.Target;
            tab.Label = "»";
            tab.ShowAt(placement.Location, new SizeInt32(ShuttleTabWidth, ShuttleTabHeight));
        }
    }

    private List<ShuttleTabPlacement> GetShuttleTabPlacements()
    {
        var placements = new List<ShuttleTabPlacement>();
        var occludingRects = new List<NativeRect>();
        var seen = new HashSet<nint>();

        EnumWindows((handle, _) =>
        {
            if (placements.Count >= ShuttleTabMaximumVisibleWindows)
            {
                return false;
            }

            if (!seen.Add(handle) || !TryGetVisibleTopLevelWindowRect(handle, out var rect))
            {
                return true;
            }

            if (IsThreadlineShuttleWindow(handle))
            {
                return true;
            }

            var snapshot = GetUsableWindowSnapshot(handle);
            if (snapshot is not null && IsShuttleTargetWindow(handle, snapshot, rect) && TryFindUnoccludedShuttleLocation(handle, rect, occludingRects, out var location))
            {
                placements.Add(new ShuttleTabPlacement(CreateWindowTarget(snapshot), location));
            }

            AddOccludingRect(occludingRects, rect);
            return true;
        }, nint.Zero);

        return placements;
    }

    private static bool IsShuttleTargetWindow(nint handle, ActiveWindowSnapshot snapshot, NativeRect rect)
    {
        if (handle == nint.Zero || rect.Width < ShuttleTabMinimumWindowWidth || rect.Height < ShuttleTabMinimumWindowHeight)
        {
            return false;
        }

        if (snapshot.ProcessId == Environment.ProcessId || string.IsNullOrWhiteSpace(snapshot.WindowTitle))
        {
            return false;
        }

        var exStyle = ShuttleGetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var hasOwner = ShuttleGetWindow(handle, GetWindowOwner) != nint.Zero;
        var isToolWindow = (exStyle & WsExToolWindow) != 0;
        var isExplicitAppWindow = (exStyle & WsExAppWindow) != 0;

        if ((hasOwner || isToolWindow) && !isExplicitAppWindow)
        {
            return false;
        }

        var className = GetShuttleWindowClassName(handle);
        if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetVisibleTopLevelWindowRect(nint handle, out NativeRect rect)
    {
        rect = default;
        if (handle == nint.Zero || !IsWindow(handle) || !IsWindowVisible(handle) || ShuttleIsIconic(handle) || IsWindowCloaked(handle))
        {
            return false;
        }

        if (!GetWindowRect(handle, out rect))
        {
            return false;
        }

        return rect.Width > 0 && rect.Height > 0;
    }

    private bool TryFindUnoccludedShuttleLocation(nint targetHandle, NativeRect targetRect, IReadOnlyList<NativeRect> occludingRects, out PointInt32 location)
    {
        var sidecarHwnd = WindowNative.GetWindowHandle(this);
        var sidecarId = Win32Interop.GetWindowIdFromWindow(sidecarHwnd);
        var workArea = GetTargetWorkArea(sidecarId, targetHandle);
        var x = GetRightEdgeAnchoredShuttleX(targetRect, workArea);

        var minY = Math.Max(targetRect.Top + ShuttleTabVerticalMargin, workArea.Y + ShuttleTabScreenMargin);
        var maxY = Math.Min(targetRect.Bottom - ShuttleTabHeight - ShuttleTabVerticalMargin, workArea.Y + workArea.Height - ShuttleTabHeight - ShuttleTabScreenMargin);
        if (maxY < minY)
        {
            minY = ClampToArea(targetRect.Top + ((targetRect.Height - ShuttleTabHeight) / 2), workArea.Y + ShuttleTabScreenMargin, workArea.Y + workArea.Height - ShuttleTabHeight - ShuttleTabScreenMargin);
            maxY = minY;
        }

        var centerY = ClampToArea(targetRect.Top + ((targetRect.Height - ShuttleTabHeight) / 2), minY, maxY);
        foreach (var y in EnumerateCandidateShuttleYPositions(centerY, minY, maxY))
        {
            var shuttleRect = new NativeRect
            {
                Left = x,
                Top = y,
                Right = x + ShuttleTabWidth,
                Bottom = y + ShuttleTabHeight
            };

            var edgeProbeRect = GetRightEdgeProbeRect(targetRect, y);
            if (!IsAnchoredToRightEdge(shuttleRect, targetRect))
            {
                continue;
            }

            if (!IsCoveredByHigherWindow(edgeProbeRect, occludingRects) && !IsCoveredByHigherWindow(shuttleRect, occludingRects))
            {
                location = new PointInt32(x, y);
                return true;
            }
        }

        location = default;
        return false;
    }

    private static NativeRect GetRightEdgeProbeRect(NativeRect targetRect, int shuttleTop)
    {
        var top = Math.Max(targetRect.Top, shuttleTop);
        var bottom = Math.Min(targetRect.Bottom, shuttleTop + ShuttleTabHeight);
        if (bottom <= top)
        {
            bottom = Math.Min(targetRect.Bottom, top + 1);
        }

        return new NativeRect
        {
            Left = targetRect.Right - ShuttleTabEdgeOverlap,
            Top = top,
            Right = targetRect.Right,
            Bottom = bottom
        };
    }

    private static int GetRightEdgeAnchoredShuttleX(NativeRect targetRect, RectInt32 workArea)
    {
        var preferredOutsideX = targetRect.Right - ShuttleTabEdgeOverlap;
        var edgeFlushInsideX = targetRect.Right - ShuttleTabWidth;
        var minX = workArea.X + ShuttleTabScreenMargin;
        var maxX = workArea.X + workArea.Width - ShuttleTabWidth - ShuttleTabScreenMargin;

        if (preferredOutsideX <= maxX)
        {
            return Math.Max(preferredOutsideX, minX);
        }

        // If the target is already against the monitor edge, keep the tab flush with the window's
        // right border instead of sliding it inward and making it look like content floating in the window.
        return ClampToArea(edgeFlushInsideX, minX, maxX);
    }

    private static bool IsAnchoredToRightEdge(NativeRect shuttleRect, NativeRect targetRect)
    {
        var overlapsRightEdge = shuttleRect.Left <= targetRect.Right && shuttleRect.Right >= targetRect.Right;
        var isFlushInsideRightEdge = shuttleRect.Right == targetRect.Right;
        return overlapsRightEdge || isFlushInsideRightEdge;
    }

    private static IEnumerable<int> EnumerateCandidateShuttleYPositions(int centerY, int minY, int maxY)
    {
        yield return centerY;

        for (var delta = ShuttleTabVerticalSearchStep; delta <= Math.Max(centerY - minY, maxY - centerY) + ShuttleTabVerticalSearchStep; delta += ShuttleTabVerticalSearchStep)
        {
            var up = centerY - delta;
            if (up >= minY)
            {
                yield return up;
            }

            var down = centerY + delta;
            if (down <= maxY)
            {
                yield return down;
            }
        }
    }

    private static bool IsCoveredByHigherWindow(NativeRect shuttleRect, IReadOnlyList<NativeRect> occludingRects)
    {
        foreach (var occluder in occludingRects)
        {
            if (Intersects(shuttleRect, occluder))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddOccludingRect(ICollection<NativeRect> occludingRects, NativeRect rect)
    {
        if (rect.Width >= 24 && rect.Height >= 24)
        {
            occludingRects.Add(rect);
        }
    }

    private static bool Intersects(NativeRect first, NativeRect second) =>
        first.Left < second.Right &&
        first.Right > second.Left &&
        first.Top < second.Bottom &&
        first.Bottom > second.Top;

    private static bool IsThreadlineShuttleWindow(nint handle)
    {
        _ = ShuttleGetWindowThreadProcessId(handle, out var processId);
        if (processId != Environment.ProcessId)
        {
            return false;
        }

        var className = GetShuttleWindowClassName(handle);
        var title = GetShuttleWindowTitle(handle);
        return className.StartsWith("ThreadlineShuttleTab_", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(title, "Threadline Shuttle", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetShuttleWindowClassName(nint handle)
    {
        var builder = new StringBuilder(256);
        return ShuttleGetClassName(handle, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    private static string GetShuttleWindowTitle(nint handle)
    {
        var builder = new StringBuilder(256);
        return ShuttleGetWindowText(handle, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    private static bool IsWindowCloaked(nint handle)
    {
        try
        {
            return ShuttleDwmGetWindowAttribute(handle, DwmWindowAttributeCloaked, out var cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
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

    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern nint ShuttleGetWindow(nint hWnd, int uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint ShuttleGetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint ShuttleGetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassName")]
    private static extern int ShuttleGetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowText")]
    private static extern int ShuttleGetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    private static extern bool ShuttleIsIconic(nint hWnd);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute", PreserveSig = true)]
    private static extern int ShuttleDwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
