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
    private const int StartupVisibilityWatchdogAttempts = 8;
    private static readonly TimeSpan StartupVisibilityWatchdogInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartupShuttleDelay = TimeSpan.FromMilliseconds(2400);

    public void EnsureCollapsedEdgeHandleStartedAfterActivation()
    {
        // The main sidecar is the fail-safe. It must become visible before any optional Shuttle
        // affordance or background window tracking can run.
        OpenSidecarAtStartup();
        StartStartupVisibilityWatchdog();

        // Keep a couple of low-priority placement passes after activation so the sidecar wins over
        // startup layout timing without depending on the old hover trigger.
        QueueStartupSidecarReveal();
        QueueStartupSidecarReveal();
        QueueStartupShuttleTabs();
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

    private void StartStartupVisibilityWatchdog()
    {
        try
        {
            var attempts = 0;
            var timer = new DispatcherTimer { Interval = StartupVisibilityWatchdogInterval };
            timer.Tick += (_, _) =>
            {
                attempts++;
                OpenSidecarAtStartup();
                if (attempts >= StartupVisibilityWatchdogAttempts)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }
        catch
        {
            // The direct startup open already ran. Do not fail launch if the watchdog cannot start.
        }
    }

    private void QueueStartupShuttleTabs()
    {
        try
        {
            var timer = new DispatcherTimer { Interval = StartupShuttleDelay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    StartShuttleTabs();
                    AddTimeline("Shuttle tabs armed after the main sidecar became visible.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Threadline] Shuttle startup skipped: {ex.GetType().Name}: {ex.Message}");
                    AddTimeline($"Shuttle tabs skipped at startup: {ex.Message}");
                }
            };
            timer.Start();
        }
        catch
        {
            // Shuttle tabs are optional. The main sidecar must stay visible even if queueing fails.
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
        }
        catch
        {
            // Continue into the visibility path even if optional trigger cleanup fails.
        }

        try
        {
            HideAllShuttleTabs();
        }
        catch
        {
            // Shuttle tabs must never prevent the main sidecar from appearing.
        }

        try
        {
            EdgeHandlePanel.Visibility = Visibility.Collapsed;
            ChatShellPanel.Visibility = Visibility.Visible;
            SetSidecarVisualState();
        }
        catch
        {
            // Named controls may still be settling during startup. Native window restore below is the fail-safe.
        }

        try
        {
            ShowMainSidecarWindow();
            ForceVisibleStartupWindow();
            AddTimeline("Sidecar opened visibly at startup. Shuttle tabs will arm after startup.");
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
        _ = SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SetWindowPosShowWindow);
    }
}
