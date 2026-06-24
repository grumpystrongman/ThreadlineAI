using Microsoft.UI.Dispatching;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    public void EnsureCollapsedEdgeHandleStartedAfterActivation()
    {
        StartFallbackFloatingTriggerTimer();
        OpenSidecarAtStartup();

        // Keep a couple of low-priority placement passes after activation so the sidecar wins over
        // startup layout timing without depending on the collapsed hover trigger.
        QueueStartupSidecarReveal();
        QueueStartupSidecarReveal();
    }

    private void StartFallbackFloatingTriggerTimer()
    {
        if (!_edgeHoverTimer.IsEnabled)
        {
            StartFloatingEdgeTrigger();
        }
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

            ShowMainSidecarWindow();
            SetSidecarVisualState();
            PlaceSidecarForTarget(GetBestSidecarTarget(), "Sidecar opened at startup.");
        }
        catch
        {
            // Startup should not fail just because the sidecar could not be placed immediately.
        }
    }
}
