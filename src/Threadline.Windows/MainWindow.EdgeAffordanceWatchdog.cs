using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int DeterministicEdgeTriggerWidth = 64;
    private const int DeterministicEdgeTriggerHeight = 144;
    private const int DeterministicEdgeTriggerMargin = 12;

    private bool _deterministicEdgeAffordanceStarted;
    private readonly DispatcherTimer _deterministicEdgeAffordanceTimer = new();

    private void StartDeterministicEdgeAffordanceWatchdog()
    {
        if (_deterministicEdgeAffordanceStarted)
        {
            return;
        }

        _deterministicEdgeAffordanceStarted = true;
        _deterministicEdgeAffordanceTimer.Interval = TimeSpan.FromMilliseconds(150);
        _deterministicEdgeAffordanceTimer.Tick += (_, _) => SafeEnsureDeterministicEdgeAffordance();
        _deterministicEdgeAffordanceTimer.Start();
        SafeEnsureDeterministicEdgeAffordance();
    }

    private void SafeEnsureDeterministicEdgeAffordance()
    {
        try
        {
            EnsureDeterministicEdgeAffordance();
        }
        catch (Exception ex)
        {
            AddTimeline($"Edge affordance watchdog skipped: {ex.Message}");
        }
    }

    private void EnsureDeterministicEdgeAffordance()
    {
        if (!_sidecarCollapsedToHandle && !_sidecarWindowHiddenForTrigger)
        {
            return;
        }

        // The sidecar is conceptually collapsed. Make the real WinUI handle visible and also
        // pin the native trigger to the edge so the user always has something to hover/click.
        EdgeHandlePanel.Visibility = Visibility.Visible;
        ChatShellPanel.Visibility = Visibility.Collapsed;

        ShowCollapsedSidecarHandleAtScreenEdge();
        PinNativeEdgeTriggerToScreenEdge();
    }

    private void PinNativeEdgeTriggerToScreenEdge()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;

        var x = area.X + area.Width - DeterministicEdgeTriggerWidth - DeterministicEdgeTriggerMargin;
        var y = area.Y + ((area.Height - DeterministicEdgeTriggerHeight) / 2);

        if (GetCursorPos(out var cursor))
        {
            y = ClampToArea(
                cursor.Y - (DeterministicEdgeTriggerHeight / 2),
                area.Y + DeterministicEdgeTriggerMargin,
                area.Y + area.Height - DeterministicEdgeTriggerHeight - DeterministicEdgeTriggerMargin);
        }

        _floatingTriggerTarget ??= GetBestSidecarTarget();
        _lastFloatingTriggerEligibleAt = DateTimeOffset.Now;
        EnsureEdgeTriggerWindow().ShowAt(
            new PointInt32(x, y),
            new SizeInt32(DeterministicEdgeTriggerWidth, DeterministicEdgeTriggerHeight));
    }
}
