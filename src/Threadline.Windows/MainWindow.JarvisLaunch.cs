using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int JarvisLaunchWidth = 760;
    private const int JarvisLaunchHeight = 820;
    private const int JarvisLaunchMargin = 28;

    /// <summary>
    /// Makes the personal AI the primary launch surface. Threadline remains the context,
    /// memory, privacy, and tooling engine underneath; sidecar/shuttle behavior remains
    /// available after the initial conversation window is visible.
    /// </summary>
    public void EnsureJarvisFrontAndCenterStartedAfterActivation()
    {
        ApplyJarvisLaunchIdentity();
        WireJarvisComposer();
        OpenJarvisAtStartup();
        QueueJarvisLaunchReveal();
        QueueJarvisLaunchReveal();
        QueueStartupShuttleTabs();
    }

    private void ApplyJarvisLaunchIdentity()
    {
        Title = "AIKA / JARVIS";
        QuestionBox.PlaceholderText = "Ask AIKA / JARVIS... (Ctrl+Enter to send)";
        SidecarAttachmentText.Text = "Personal AI online · Threadline context engine underneath";
        TrustControlStatusText.Text = "Local-first · Governed";

        ReplaceVisibleText(RootShell, "ThreadlineAI", "AIKA / JARVIS");

        _transcriptMessages.Clear();
        AppendTranscript(
            "AIKA / JARVIS",
            "I'm here. Ask me a question, give me a task, or tell me what you want done. Threadline will supply the approved context; Jarvis can take on agentic work when the request needs action.");
    }

    private static void ReplaceVisibleText(DependencyObject root, string oldText, string newText)
    {
        if (root is TextBlock textBlock && string.Equals(textBlock.Text, oldText, StringComparison.Ordinal))
        {
            textBlock.Text = newText;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ReplaceVisibleText(VisualTreeHelper.GetChild(root, index), oldText, newText);
        }
    }

    private void QueueJarvisLaunchReveal()
    {
        try
        {
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                OpenJarvisAtStartup();
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, OpenJarvisAtStartup);
            });
        }
        catch
        {
            // Startup positioning is best-effort. The activated window remains available.
        }
    }

    private void OpenJarvisAtStartup()
    {
        try
        {
            _sidecarCollapsedToHandle = false;
            _sidecarWindowHiddenForTrigger = false;
            _floatingTriggerTarget = null;
            _edgeTriggerWindow?.HideTrigger();
            HideAllShuttleTabs();
        }
        catch
        {
            // Optional trigger/shuttle cleanup must never prevent the main assistant from opening.
        }

        try
        {
            EdgeHandlePanel.Visibility = Visibility.Collapsed;
            ChatShellPanel.Visibility = Visibility.Visible;
            SetSidecarVisualState();
        }
        catch
        {
            // Native window placement below is the startup fail-safe.
        }

        try
        {
            ShowMainSidecarWindow();
            CenterJarvisLaunchWindow();
            AddTimeline("AIKA / JARVIS opened front and center; Threadline sidecar behavior remains available.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Jarvis startup reveal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void CenterJarvisLaunchWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _ = ShowWindow(hwnd, ShowWindowRestore);
        Activate();

        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        EnsureSidecarWindowUserResizable(appWindow);

        var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min(JarvisLaunchWidth, Math.Max(420, area.Width - (JarvisLaunchMargin * 2)));
        var height = Math.Min(JarvisLaunchHeight, Math.Max(640, area.Height - (JarvisLaunchMargin * 2)));
        var x = area.X + Math.Max(JarvisLaunchMargin, (area.Width - width) / 2);
        var y = area.Y + Math.Max(JarvisLaunchMargin, (area.Height - height) / 2);

        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(x, y));
    }
}
