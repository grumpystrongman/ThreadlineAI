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

        try
        {
            SessionBindingStatusText.Text = $"Active Work Thread: {_workThread.Title}\nStatus: {_workThread.Status} • Thread: {_workThread.Id}";
        }
        catch
        {
            // Non-fatal UI update.
        }
    }

    private async Task PersistWorkMessageAsync(string role, string content)
    {
        if (_workThread is null || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        try
        {
            await _workThreadClient.SaveConversationMessageAsync(_workThread.Id, role, content);
        }
        catch (Exception ex)
        {
            AddTimeline("Work message save failed: " + ex.Message);
        }
    }
}
