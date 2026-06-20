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

    private void ClearSharedContext_Click(object sender, RoutedEventArgs e)
    {
        ClearLocalWorkingState();
        _transcriptMessages.Clear();
        AppendTranscript("Threadline", "Shared local context cleared. Start New Chat for a clean service session.");
        CurrentWindowText.Text = "No target window.";
        AddTimeline("Cleared local shared context.");
    }

    private void ClearLocalWorkingState()
    {
        _attachment = null;
        _lastAction = null;
        _lastNativeUiResult = null;
        _lastContextSummary = null;
        _selectedTargetWindow = null;
        _selectedThreadlineTarget = null;
        _lastSidecarAttachedTargetId = null;
        ResetCurrentContextPanel();
        QuestionBox.Text = string.Empty;
        OpenWindowsList.SelectedItem = null;
        TryAttachSidecarToBestTarget(force: true);
    }
}
