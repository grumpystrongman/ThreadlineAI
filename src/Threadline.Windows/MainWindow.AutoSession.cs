using Microsoft.UI.Xaml;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private bool _sidecarSessionBootstrapStarted;

    private async void RootShell_Loaded(object sender, RoutedEventArgs e)
    {
        if (_sidecarSessionBootstrapStarted) return;
        _sidecarSessionBootstrapStarted = true;
        await RunUiActionAsync(EnsureSidecarSessionReadyAsync);
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
