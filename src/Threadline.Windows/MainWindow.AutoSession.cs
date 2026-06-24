using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int CollapsedEdgeHandleWidth = 56;
    private const int CollapsedEdgeHandleHeight = 180;
    private const int CollapsedEdgeHandleMargin = 12;
    private const uint SetWindowPosShowWindow = 0x0040;
    private static readonly nint HwndTopmost = unchecked((nint)(-1));

    private bool _sidecarSessionBootstrapStarted;
    private bool _fallbackEdgeTriggerStarted;
    private readonly DispatcherTimer _fallbackEdgeTriggerTimer = new();

    private async void RootShell_Loaded(object sender, RoutedEventArgs e)
    {
        SafeEnsureReadableCheckBoxLabels();
        StartFallbackFloatingTriggerTimer();
        SafeEnsureFallbackFloatingTriggerVisible();
        StartBrowserExtensionGuidanceTimer();

        await RunUiActionAsync(EnsureLocalServiceStartedAsync);
        await ShowFirstRunSetupWizardIfNeededAsync();

        if (_sidecarSessionBootstrapStarted) return;
        _sidecarSessionBootstrapStarted = true;
        await RunUiActionAsync(EnsureSidecarSessionReadyAsync);
    }

    private void SafeEnsureReadableCheckBoxLabels()
    {
        try
        {
            EnsureReadableCheckBoxLabels();
        }
        catch (Exception ex)
        {
            AddTimeline($"Checkbox label polish skipped: {ex.Message}");
        }
    }

    private void StartFallbackFloatingTriggerTimer()
    {
        if (_fallbackEdgeTriggerStarted) return;

        _fallbackEdgeTriggerStarted = true;
        _fallbackEdgeTriggerTimer.Interval = TimeSpan.FromMilliseconds(250);
        _fallbackEdgeTriggerTimer.Tick += (_, _) => SafeEnsureFallbackFloatingTriggerVisible();
        _fallbackEdgeTriggerTimer.Start();
    }

    private void SafeEnsureFallbackFloatingTriggerVisible()
    {
        try
        {
            EnsureFallbackFloatingTriggerVisible();
        }
        catch
        {
            // The floating trigger is a recovery path. It should never break the sidecar UI.
        }
    }

    private void EnsureFallbackFloatingTriggerVisible()
    {
        if (!_sidecarWindowHiddenForTrigger || !_sidecarCollapsedToHandle)
        {
            return;
        }

        // In attach-to-target mode, the native open-window edge hover is the primary affordance.
        // Do not show the screen-edge fallback or hide the native trigger here; doing so suppresses
        // the AI hover beside real application windows every 250ms.
        if (_attachSidecarToTarget)
        {
            return;
        }

        ShowCollapsedSidecarHandleAtScreenEdge();
    }

    private void ShowCollapsedSidecarHandleAtScreenEdge()
    {
        try
        {
            EdgeHandlePanel.Visibility = Visibility.Visible;
            ChatShellPanel.Visibility = Visibility.Collapsed;

            var hwnd = WindowNative.GetWindowHandle(this);
            _ = ShowWindow(hwnd, ShowWindowRestore);

            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;

            var width = Math.Min(CollapsedEdgeHandleWidth, Math.Max(44, area.Width - (CollapsedEdgeHandleMargin * 2)));
            var height = Math.Min(CollapsedEdgeHandleHeight, Math.Max(80, area.Height - (CollapsedEdgeHandleMargin * 2)));
            var x = area.X + area.Width - width - CollapsedEdgeHandleMargin;
            var y = area.Y + ((area.Height - height) / 2);

            if (GetCursorPos(out var cursor))
            {
                y = ClampToArea(cursor.Y - (height / 2), area.Y + CollapsedEdgeHandleMargin, area.Y + area.Height - height - CollapsedEdgeHandleMargin);
            }

            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(x, y));
            _ = SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SetWindowPosNoActivate | SetWindowPosShowWindow);
        }
        catch
        {
            // If the collapsed handle cannot be placed, leave the app running and let the next
            // timer tick try again.
        }
    }

    private async Task EnsureSidecarSessionReadyAsync()
    {
        if (_session is not null)
        {
            return;
        }

        _session = await _client.GetActiveSessionAsync();
        if (_session is null)
        {
            var provider = GetSelectedProvider();
            _session = await _client.StartSessionAsync($"Windows sidecar session {DateTimeOffset.Now:g}", provider);
            AddTimeline($"Started chat session {_session.Id} automatically.");
            AppendTranscript("Threadline Session", "Started a new chat session automatically. You can ask now.");
        }
        else
        {
            AddTimeline($"Loaded active chat session {_session.Id} automatically.");
            AppendTranscript("Threadline Session", "Loaded your active chat session. You can ask now.");
        }

        SessionText.Text = $"Session: {_session.Status} / {_session.ActiveProvider ?? "None"}";
    }
}
