using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private bool _localServiceStartupRequested;

    private async Task EnsureLocalServiceStartedAsync()
    {
        if (_localServiceStartupRequested)
        {
            return;
        }

        _localServiceStartupRequested = true;
        ServiceStatusText.Text = "Service: starting...";

        var result = await ThreadlineServiceLauncher.EnsureStartedAsync();
        ServiceStatusText.Text = result.Message;
        AddTimeline(result.Message);

        if (!result.Success)
        {
            AppendTranscript("Threadline Service", result.Message);
        }
    }
}
