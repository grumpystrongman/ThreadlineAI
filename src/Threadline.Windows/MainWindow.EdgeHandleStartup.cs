using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int StartupSidecarWidth = 430;
    private const int StartupSidecarHeight = 760;
    private const int StartupSidecarMargin = 24;

    public void EnsureCollapsedEdgeHandleStartedAfterActivation()
    {
        StartShuttleTabs();
        OpenSidecarAtStartup();

        // Keep a couple of low-priority placement passes after activation so the sidecar wins over
        // startup layout timing without depending on the old hover trigger.
        QueueStartupSidecarReveal();
        QueueStartupSidecarReveal();
    }

    private void QueueStartupSidecarReveal()
    {
        try
        {
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                OpenSidecarAtStartup();
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, OpenSidecarAtStartup);
            });
        }
        catch
        {
            // This is a startup recovery path. Leave the app running if queueing is unavailable.
        }
    }

    private void OpenSidecarAtStartup()
    {
        try
        {
            _sidecarCollapsedToHandle = false;
            _sidecarWindowHiddenForTrigger = false;
            _floatingTriggerTarget = null;
            _edgeTriggerWindow?.HideTrigger();
            HideAllShuttleTabs();

            EdgeHandlePanel.Visibility = Visibility.Collapsed;
            ChatShellPanel.Visibility = Visibility.Visible;
            ShowMainSidecarWindow();
            ForceVisibleStartupWindow();
            AddTimeline("Sidecar opened visibly at startup. Shuttle tabs are available on eligible windows.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Startup sidecar reveal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ForceVisibleStartupWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _ = ShowWindow(hwnd, ShowWindowRestore);
        Activate();

        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        EnsureSidecarWindowUserResizable(appWindow);

        var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min(StartupSidecarWidth, Math.Max(360, area.Width - (StartupSidecarMargin * 2)));
        var height = Math.Min(StartupSidecarHeight, Math.Max(620, area.Height - (StartupSidecarMargin * 2)));
        var x = area.X + area.Width - width - StartupSidecarMargin;
        var y = area.Y + StartupSidecarMargin;

        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(x, y));
        _ = SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SetWindowPosNoActivate | SetWindowPosShowWindow);
    }
}
