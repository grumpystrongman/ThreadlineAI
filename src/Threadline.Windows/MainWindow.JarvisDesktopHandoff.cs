namespace Threadline.Windows;

public sealed partial class MainWindow
{
    /// <summary>
    /// Once the full PersonalJarvis desktop is healthy, Threadline stops pretending to be
    /// the primary assistant UI. Keep this WinUI process alive for the interactive Device
    /// Host, context capture, shuttle/edge triggers, and owner controls, but hide the legacy
    /// chat shell until the user explicitly opens it from Threadline's edge affordance.
    /// </summary>
    public void HandOffForegroundToJarvisDesktop()
    {
        try
        {
            _sidecarCollapsedToHandle = true;
            _sidecarWindowHiddenForTrigger = true;
            SetSidecarVisualState();
            HideMainSidecarWindow();
            AddTimeline("Foreground handed to the full PersonalJarvis desktop. Threadline remains active in the background for context and Windows device control.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Jarvis desktop handoff failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
