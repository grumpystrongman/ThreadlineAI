using Microsoft.UI.Dispatching;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    public void EnsureCollapsedEdgeHandleStartedAfterActivation()
    {
        StartFallbackFloatingTriggerTimer();
        StartDeterministicEdgeAffordanceWatchdog();
        SafeEnsureFallbackFloatingTriggerVisible();
        SafeEnsureDeterministicEdgeAffordance();

        // ConfigureSidecarWindow registers an older Loaded handler that can still queue a hide.
        // Queue multiple reveal passes after activation so the collapsed WinUI handle wins the
        // startup race deterministically instead of depending on hover/native popup behavior.
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
                SafeEnsureDeterministicEdgeAffordance();
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    SafeEnsureFallbackFloatingTriggerVisible();
                    SafeEnsureDeterministicEdgeAffordance();
                });
            });
        }
        catch
        {
            // This is a startup recovery path. Leave the app running if queueing is unavailable.
        }
    }
}
