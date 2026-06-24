namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private void StartDeterministicEdgeAffordanceWatchdog()
    {
        // Disabled: the deterministic watchdog forced a topmost native trigger on every tick,
        // which can interfere with normal window focus/target detection. The collapsed WinUI
        // edge handle is now managed by MainWindow.AutoSession instead.
    }

    private void SafeEnsureDeterministicEdgeAffordance()
    {
        // No-op. See StartDeterministicEdgeAffordanceWatchdog.
    }
}
