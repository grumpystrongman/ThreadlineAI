using Microsoft.UI.Dispatching;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    public void EnsureCollapsedEdgeHandleStartedAfterActivation()
    {
        StartFallbackFloatingTriggerTimer();
        SafeEnsureFallbackFloatingTriggerVisible();

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
                SafeEnsureFallbackFloatingTriggerVisible();
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SafeEnsureFallbackFloatingTriggerVisible);
            });
        }
        catch
        {
            // This is a startup recovery path. Leave the app running if queueing is unavailable.
        }
    }
}
