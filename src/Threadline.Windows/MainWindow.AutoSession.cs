using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private bool _sidecarSessionBootstrapStarted;
    private bool _fallbackEdgeTriggerStarted;
    private readonly DispatcherTimer _fallbackEdgeTriggerTimer = new();

    private async void RootShell_Loaded(object sender, RoutedEventArgs e)
    {
        StartFallbackFloatingTriggerTimer();
        StartBrowserExtensionGuidanceTimer();

        if (_sidecarSessionBootstrapStarted) return;
        _sidecarSessionBootstrapStarted = true;
        await RunUiActionAsync(EnsureSidecarSessionReadyAsync);
    }

    private void StartFallbackFloatingTriggerTimer()
    {
        if (_fallbackEdgeTriggerStarted) return;

        _fallbackEdgeTriggerStarted = true;
        _fallbackEdgeTriggerTimer.Interval = TimeSpan.FromMilliseconds(1500);
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
        var trigger = EnsureEdgeTriggerWindow();

        if (!_sidecarWindowHiddenForTrigger || !_attachSidecarToTarget)
        {
            trigger.HideTrigger();
            return;
        }

        // Important: if the fallback trigger is already visible, keep it stable.
        // Repeated ShowAt calls cause the visual blink Jeff saw when hovering near the AI button.
        if (trigger.IsVisible)
        {
            _lastFloatingTriggerEligibleAt = DateTimeOffset.Now;
            return;
        }

        var hasCursor = GetCursorPos(out var cursor);
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;

        const int triggerWidth = 64;
        const int triggerHeight = 144;
        const int margin = 12;

        var x = area.X + area.Width - triggerWidth - margin;
        var y = hasCursor
            ? ClampToArea(cursor.Y - (triggerHeight / 2), area.Y + margin, area.Y + area.Height - triggerHeight - margin)
            : area.Y + ((area.Height - triggerHeight) / 2);

        _floatingTriggerTarget ??= GetBestSidecarTarget();
        _lastFloatingTriggerEligibleAt = DateTimeOffset.Now;
        trigger.ShowAt(new PointInt32(x, y), new SizeInt32(triggerWidth, triggerHeight));
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

        SessionText.Text = $"Session: {_session.Status} / {_session.ActiveProvider ?? \"None\"}";
    }
}
