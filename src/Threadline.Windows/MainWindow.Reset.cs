using Microsoft.UI.Xaml;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            ClearLocalWorkingState();
            TimelineList.Items.Clear();
            _transcriptMessages.Clear();
            await StartSessionAsync();
            AppendTranscript("Threadline", "New Threadline chat started. Pick an open app/tab, then ask Threadline about that target.");
            AddTimeline("New chat started and local UI cleared.");
        });
    }

    private async void ClearSharedContext_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("context.clear"));

    private async Task ClearSharedContextActionAsync()
    {
        string? serviceMessage = null;
        if (_activeWorkThread is not null)
        {
            try
            {
                var result = await _workThreadClient.RunActionAsync("context.clear", _activeWorkThread.Id);
                serviceMessage = result.Message;
            }
            catch (Exception ex)
            {
                AddTimeline("Durable context clear failed: " + ex.Message);
                serviceMessage = "Local context cleared, but durable Work Thread context clear failed: " + ex.Message;
            }
        }

        ClearLocalWorkingState();
        AppendTranscript("Threadline", serviceMessage ?? "Shared local context cleared. Conversation and Work Thread memory were not deleted.");
        CurrentWindowText.Text = "No target window.";
        AddTimeline("Cleared shared context through registered action.");
    }

    private void ClearLocalWorkingState()
    {
        _attachment = null;
        _lastAction = null;
        _lastNativeUiResult = null;
        _lastContextSummary = null;
        _lastArtifact = null;
        _pendingConnectionTarget = null;
        _selectedTargetWindow = null;
        _selectedThreadlineTarget = null;
        _lastAutoFollowTargetId = null;
        if (!_isTargetLocked)
        {
            _lastFollowTarget = null;
        }
        ResetCurrentContextPanel();
        UpdateSessionBindingStatus("Window session: no pending connection. Click AI on another window to choose how to connect it.");
        PlaceSidecarForTarget(GetBestSidecarTarget(), "Local context reset.");
        QuestionBox.Text = string.Empty;
        OpenWindowsList.SelectedItem = null;
    }
}
