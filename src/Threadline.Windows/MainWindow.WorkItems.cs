using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ThreadlineWorkThreadClient _workThreadClient = new();
    private WorkThreadDto? _workThread;

    private async Task EnsureActiveWorkThreadAsync(bool forceNew = false)
    {
        if (!forceNew && _workThread is not null)
        {
            return;
        }

        _workThread = forceNew ? null : await _workThreadClient.GetActiveWorkThreadAsync();
        if (_workThread is null)
        {
            _workThread = await _workThreadClient.StartWorkThreadAsync($"Work Thread {DateTimeOffset.Now:g}", "Created locally.");
        }
    }
}
