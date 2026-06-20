using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly List<WorkThreadDto> _workThreadSessionList = new();

    private async void RefreshWorkThreads_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(async () =>
        {
            OpenSessionManagerPanel();
            await RefreshWorkThreadSessionListAsync(selectActive: true);
        });

    private async void ResumeSelectedWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(ResumeSelectedWorkThreadAsync);

    private async void RenameSelectedWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(RenameSelectedWorkThreadAsync);

    private async void TieCurrentWindowToSelectedThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(TieCurrentWindowToSelectedThreadAsync);

    private async void DetachCurrentWindowFromWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(DetachCurrentWindowFromWorkThreadAsync);

    private void OpenSessionManagerPanel()
    {
        WorkThreadSessionManagerPanel.Visibility = Visibility.Visible;
        WorkThreadListStatusText.Text = "Sessions are shown here so the main sidecar can stay focused on the conversation.";
    }

    private void CloseSessionManagerPanel()
    {
        WorkThreadSessionManagerPanel.Visibility = Visibility.Collapsed;
    }

    private void WorkThreadList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = GetSelectedWorkThreadFromList();
        if (selected is null)
        {
            WorkThreadSelectedText.Text = "Selected: none";
            return;
        }

        WorkThreadSelectedText.Text = $"Selected: {selected.Title} ({selected.Status})";
        SessionManagerTitleBox.Text = selected.Title;
        WorkThreadTitleBox.Text = selected.Title;
    }

    private async Task RefreshWorkThreadSessionListAsync(bool selectActive)
    {
        try
        {
            WorkThreadListStatusText.Text = "Loading Work Threads...";
            var threads = await _workThreadClient.ListWorkThreadsAsync(50);

            _workThreadSessionList.Clear();
            _workThreadSessionList.AddRange(threads.OrderByDescending(thread => thread.UpdatedAt));

            WorkThreadList.ItemsSource = _workThreadSessionList.Select(FormatWorkThreadListRow).ToList();

            if (_workThreadSessionList.Count == 0)
            {
                WorkThreadListStatusText.Text = "No Work Threads found. Click New Thread to start one.";
                WorkThreadSelectedText.Text = "Selected: none";
                return;
            }

            WorkThreadListStatusText.Text = $"Loaded {_workThreadSessionList.Count} Work Thread(s).";

            if (selectActive && _activeWorkThread is not null)
            {
                var activeIndex = _workThreadSessionList.FindIndex(thread => string.Equals(thread.Id, _activeWorkThread.Id, StringComparison.OrdinalIgnoreCase));
                if (activeIndex >= 0)
                {
                    WorkThreadList.SelectedIndex = activeIndex;
                }
            }
        }
        catch (Exception ex)
        {
            WorkThreadListStatusText.Text = "Could not load Work Threads: " + ex.Message;
            AddTimeline("Could not refresh Work Thread list: " + ex.Message);
        }
    }

    private async Task ResumeSelectedWorkThreadAsync()
    {
        var selected = GetSelectedWorkThreadFromList();
        if (selected is null)
        {
            WorkThreadListStatusText.Text = "Select a Work Thread first, then click Resume Selected.";
            OpenSessionManagerPanel();
            return;
        }

        _activeWorkThread = await _workThreadClient.ResumeWorkThreadAsync(selected.Id);
        UpdateWorkThreadUi();
        await LoadWorkThreadMessagesAsync();
        await RefreshWorkThreadSessionListAsync(selectActive: true);
        CloseSessionManagerPanel();
        AddTimeline("Resumed selected Work Thread.");
    }

    private async Task RenameSelectedWorkThreadAsync()
    {
        var selected = GetSelectedWorkThreadFromList() ?? _activeWorkThread;
        if (selected is null)
        {
            WorkThreadListStatusText.Text = "No Work Thread selected or active. Create or resume one first.";
            OpenSessionManagerPanel();
            return;
        }

        var proposedTitle = SessionManagerTitleBox.Text;
        if (string.IsNullOrWhiteSpace(proposedTitle))
        {
            proposedTitle = WorkThreadTitleBox.Text;
        }

        var title = NormalizeThreadTitle(proposedTitle);
        _activeWorkThread = await _workThreadClient.RenameWorkThreadAsync(selected.Id, title, BuildCurrentTargetDescription());
        UpdateWorkThreadUi();
        SessionManagerTitleBox.Text = _activeWorkThread.Title;
        await RefreshWorkThreadSessionListAsync(selectActive: true);
        WorkThreadListStatusText.Text = $"Renamed Work Thread: {_activeWorkThread.Title}";
        AddTimeline("Renamed selected Work Thread.");
    }

    private async Task TieCurrentWindowToSelectedThreadAsync()
    {
        var selected = GetSelectedWorkThreadFromList() ?? _activeWorkThread;
        if (selected is null)
        {
            WorkThreadListStatusText.Text = "Select or create a Work Thread before tying a window.";
            OpenSessionManagerPanel();
            return;
        }

        var target = _pendingConnectionTarget ?? _floatingTriggerTarget ?? _selectedThreadlineTarget ?? _lastFollowTarget;
        if (target is null)
        {
            WorkThreadListStatusText.Text = "No current or pending window target is available to tie.";
            return;
        }

        if (_activeWorkThread is null || !string.Equals(_activeWorkThread.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            _activeWorkThread = await _workThreadClient.ResumeWorkThreadAsync(selected.Id);
            UpdateWorkThreadUi();
        }

        AttachSidecarToWindowTarget(target, clearDraft: false, "Tied window to selected Work Thread.");
        await PersistTargetContextEventAsync(target, "ManualTie");
        PlaceSidecarForTarget(target, "Tied window to selected Work Thread.");
        await RefreshWorkThreadSessionListAsync(selectActive: true);
        WorkThreadListStatusText.Text = "Tied window to selected Work Thread.";
        AddTimeline("Tied window to selected Work Thread.");
    }

    private async Task DetachCurrentWindowFromWorkThreadAsync()
    {
        await DetachPendingOrCurrentTargetAsync();
        WorkThreadListStatusText.Text = _activeWorkThread is null
            ? "Window detached. No active Work Thread."
            : $"Window detached. Active Work Thread remains: {_activeWorkThread.Title}";
    }

    private WorkThreadDto? GetSelectedWorkThreadFromList()
    {
        var index = WorkThreadList.SelectedIndex;
        if (index < 0 || index >= _workThreadSessionList.Count)
        {
            return null;
        }

        return _workThreadSessionList[index];
    }

    private static string FormatWorkThreadListRow(WorkThreadDto thread)
    {
        var updated = thread.UpdatedAt.ToLocalTime().ToString("g");
        return $"{thread.Title} [{thread.Status}]\nUpdated: {updated}";
    }
}