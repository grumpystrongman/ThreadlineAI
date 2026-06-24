using Microsoft.UI.Dispatching;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    public void EnsureCollapsedEdgeHandleStartedAfterActivation()
    {
        StartFallbackFloatingTriggerTimer();
        SafeShowCollapsedEdgeHandleAfterActivation();

        // ConfigureSidecarWindow still has legacy hide behavior during startup. Queue multiple
        // WinUI-handle reveal passes after activation so the collapsed handle wins without relying
        // on a separate native popup window.
        QueueCollapsedEdgeHandleReveal();
        QueueCollapsedEdgeHandleReveal();
    }

    private void QueueCollapsedEdgeHandleReveal()
    {
        try
        {
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                SafeShowCollapsedEdgeHandleAfterActivation();
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SafeShowCollapsedEdgeHandleAfterActivation);
            });
        }
        catch
        {
            // This is a startup recovery path. Leave the app running if queueing is unavailable.
        }
    }

    private void SafeShowCollapsedEdgeHandleAfterActivation()
    {
        try
        {
            if (!_sidecarWindowHiddenForTrigger || !_sidecarCollapsedToHandle)
            {
                return;
            }

            ShowCollapsedSidecarHandleAtScreenEdge();
        }
        catch
        {
            // This is a startup recovery path. Leave the app running if the handle cannot be placed.
        }
    }
}
