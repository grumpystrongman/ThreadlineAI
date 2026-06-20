using System.Text.Json;
using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ThreadlineWorkThreadClient _workThreadClient = new();
    private WorkThreadDto? _activeWorkThread;

    private async void NewWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(NewWorkThreadAsync);

    private async void ResumeWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(ResumeWorkThreadAsync);

    private async void RenameWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(RenameWorkThreadAsync);

    private async void CloseWorkThread_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(CloseWorkThreadAsync);

    private async Task InitializeWorkThreadAsync()
    {
        try
        {
            _activeWorkThread = await _workThreadClient.GetActiveWorkThreadAsync();
            if (_activeWorkThread is null)
            {
                _activeWorkThread = await _workThreadClient.StartWorkThreadAsync(BuildDefaultWorkThreadTitle(), "Automatically created by Threadline Windows sidecar.");
                AddTimeline("Created default active work thread.");
            }
            else
            {
                AddTimeline("Loaded active work thread.");
            }

            UpdateWorkThreadUi();
            await LoadWorkThreadMessagesAsync();
        }
        catch (Exception ex)
        {
            WorkThreadStatusText.Text = "Work Thread: unavailable";
            AddTimeline("Work Thread unavailable: " + ex.Message);
        }
    }

    private async Task NewWorkThreadAsync()
    {
        var title = NormalizeThreadTitle(WorkThreadTitleBox.Text);
        _activeWorkThread = await _workThreadClient.StartWorkThreadAsync(title, BuildCurrentTargetDescription());
        UpdateWorkThreadUi();
        _transcriptMessages.Clear();
        AppendTranscript("Threadline", $"Started Work Thread: {_activeWorkThread.Title}");
        AddTimeline("Started new Work Thread.");
        await PersistTargetContextEventAsync(_selectedThreadlineTarget, "Manual");
    }

    private async Task ResumeWorkThreadAsync()
    {
        var active = _activeWorkThread ?? await _workThreadClient.GetActiveWorkThreadAsync();
        if (active is null)
        {
            _activeWorkThread = await _workThreadClient.StartWorkThreadAsync(BuildDefaultWorkThreadTitle(), BuildCurrentTargetDescription());
        }
        else
        {
            _activeWorkThread = await _workThreadClient.ResumeWorkThreadAsync(active.Id);
        }

        UpdateWorkThreadUi();
        await LoadWorkThreadMessagesAsync();
        AddTimeline("Resumed active Work Thread.");
    }

    private async Task RenameWorkThreadAsync()
    {
        if (_activeWorkThread is null)
        {
            await NewWorkThreadAsync();
            return;
        }

        var title = NormalizeThreadTitle(WorkThreadTitleBox.Text);
        _activeWorkThread = await _workThreadClient.RenameWorkThreadAsync(_activeWorkThread.Id, title, BuildCurrentTargetDescription());
        UpdateWorkThreadUi();
        AddTimeline("Renamed active Work Thread.");
        AppendTranscript("Threadline", $"Renamed Work Thread: {_activeWorkThread.Title}");
    }

    private async Task CloseWorkThreadAsync()
    {
        if (_activeWorkThread is null)
        {
            UpdateWorkThreadUi();
            return;
        }

        var closedTitle = _activeWorkThread.Title;
        _activeWorkThread = await _workThreadClient.CloseWorkThreadAsync(_activeWorkThread.Id);
        UpdateWorkThreadUi();
        AddTimeline("Closed active Work Thread.");
        AppendTranscript("Threadline", $"Closed Work Thread: {closedTitle}. Click New Thread to start the next workstream.");
    }

    private async Task LoadWorkThreadMessagesAsync()
    {
        if (_activeWorkThread is null) return;

        try
        {
            var messages = await _workThreadClient.GetConversationMessagesAsync(_activeWorkThread.Id);
            if (messages.Count == 0) return;

            _transcriptMessages.Clear();
            foreach (var message in messages.OrderBy(message => message.CreatedAt).TakeLast(MaxTranscriptItems))
            {
                AppendTranscript(FormatStoredRole(message.Role), message.Content);
            }

            AddTimeline($"Loaded {messages.Count} stored message(s).");
        }
        catch (Exception ex)
        {
            AddTimeline("Could not load Work Thread messages: " + ex.Message);
        }
    }

    private async Task PersistTranscriptMessageAsync(string role, string content, string? contextReceiptId = null)
    {
        if (_activeWorkThread is null || string.IsNullOrWhiteSpace(content)) return;

        try
        {
            await _workThreadClient.SaveConversationMessageAsync(_activeWorkThread.Id, role, content, contextReceiptId);
        }
        catch (Exception ex)
        {
            AddTimeline("Could not save message to Work Thread: " + ex.Message);
        }
    }

    private async Task PersistTargetContextEventAsync(ThreadlineTarget? target, string captureMode)
    {
        if (_activeWorkThread is null || target is null) return;

        try
        {
            await _workThreadClient.SaveWorkContextEventAsync(
                _activeWorkThread.Id,
                target.Kind.ToString(),
                target.Title,
                target.Window.ApplicationName,
                target.Window.WindowTitle,
                url: null,
                contentSummary: BuildTargetStatus(target, captureMode),
                captureMode: captureMode);
            AddTimeline($"Saved {captureMode} context event for Work Thread.");
        }
        catch (Exception ex)
        {
            AddTimeline("Could not save context event: " + ex.Message);
        }
    }

    private async Task<ContextReceiptDto?> PersistContextReceiptForAskAsync(string? currentWindow)
    {
        if (_activeWorkThread is null) return null;

        try
        {
            var source = BuildContextReceiptSource(currentWindow);
            var notUsed = new[]
            {
                "Email",
                "Teams",
                "Local files outside active/followed context",
                "Patient-level data"
            };
            var limitations = BuildContextReceiptLimitations();
            return await _workThreadClient.SaveContextReceiptAsync(
                _activeWorkThread.Id,
                JsonSerializer.Serialize(source),
                JsonSerializer.Serialize(notUsed),
                limitations);
        }
        catch (Exception ex)
        {
            AddTimeline("Could not save Context Receipt: " + ex.Message);
            return null;
        }
    }

    private string BuildContextReceiptText(ContextReceiptDto? receipt, string? currentWindow)
    {
        var target = _selectedThreadlineTarget ?? _lastFollowTarget;
        var sourceName = target?.Title
            ?? _lastContextSummary?.Title
            ?? _lastForegroundWindow?.WindowTitle
            ?? currentWindow
            ?? "No resolved source";
        var appName = target?.Window.ApplicationName
            ?? _lastContextSummary?.Process?.ProcessName
            ?? _lastForegroundWindow?.ApplicationName
            ?? "Unknown app";
        var captureMode = target is not null ? "Attached/Followed" : "Inferred from current window";

        return "Context Receipt\n" +
               $"- Work Thread: {_activeWorkThread?.Title ?? "None"}\n" +
               $"- Used: {appName} — {sourceName}\n" +
               $"- Capture mode: {captureMode}\n" +
               $"- Snapshot: {DateTimeOffset.Now:g}\n" +
               "- Not used: Email, Teams, local files outside active context, patient-level data\n" +
               $"- Limitations: {BuildContextReceiptLimitations()}\n" +
               (receipt is null ? "- Receipt storage: not saved" : $"- Receipt ID: {receipt.Id}");
    }

    private object BuildContextReceiptSource(string? currentWindow)
    {
        var target = _selectedThreadlineTarget ?? _lastFollowTarget;
        if (target is not null)
        {
            return new[]
            {
                new
                {
                    sourceType = target.Kind.ToString(),
                    sourceName = target.Title,
                    appName = target.Window.ApplicationName,
                    windowTitle = target.Window.WindowTitle,
                    captureMode = "AttachedOrFollowed",
                    provider = target.ProviderKey,
                    confidence = target.Confidence,
                    capturedAt = DateTimeOffset.Now
                }
            };
        }

        return new[]
        {
            new
            {
                sourceType = "Window",
                sourceName = _lastContextSummary?.Title ?? _lastForegroundWindow?.WindowTitle ?? currentWindow ?? "Unknown",
                appName = _lastContextSummary?.Process?.ProcessName ?? _lastForegroundWindow?.ApplicationName ?? "Unknown",
                windowTitle = _lastForegroundWindow?.WindowTitle,
                captureMode = "Inferred",
                provider = _lastContextSummary?.Source ?? "native-ui",
                confidence = _lastContextSummary?.Confidence ?? "unknown",
                capturedAt = DateTimeOffset.Now
            }
        };
    }

    private string BuildContextReceiptLimitations()
    {
        if (_lastContextSummary is null)
        {
            return "Threadline used the selected or foreground window metadata available locally. Deep document, email, Teams, and unrelated local file context were not used.";
        }

        if (_lastContextSummary.Warnings.Count == 0)
        {
            return "Threadline used the resolved active context only. Email, Teams, unrelated local files, and patient-level data were not used.";
        }

        return string.Join(" ", _lastContextSummary.Warnings.Take(3));
    }

    private void UpdateWorkThreadUi()
    {
        if (_activeWorkThread is null)
        {
            WorkThreadStatusText.Text = "Work Thread: none active";
            return;
        }

        WorkThreadStatusText.Text = $"Work Thread: {_activeWorkThread.Title} ({_activeWorkThread.Status})";
        if (string.IsNullOrWhiteSpace(WorkThreadTitleBox.Text) || WorkThreadTitleBox.Text.StartsWith("Work Thread", StringComparison.OrdinalIgnoreCase))
        {
            WorkThreadTitleBox.Text = _activeWorkThread.Title;
        }
    }

    private string NormalizeThreadTitle(string? proposedTitle)
    {
        if (!string.IsNullOrWhiteSpace(proposedTitle)) return proposedTitle.Trim();
        return BuildDefaultWorkThreadTitle();
    }

    private string BuildDefaultWorkThreadTitle()
    {
        var target = _selectedThreadlineTarget ?? _lastFollowTarget;
        if (target is not null)
        {
            return $"{target.Window.ApplicationName} — {target.Title}";
        }

        if (_lastForegroundWindow is not null)
        {
            return $"{_lastForegroundWindow.ApplicationName} — {_lastForegroundWindow.WindowTitle ?? "Untitled"}";
        }

        return $"Work Thread — {DateTimeOffset.Now:g}";
    }

    private string? BuildCurrentTargetDescription()
    {
        var target = _selectedThreadlineTarget ?? _lastFollowTarget;
        if (target is not null)
        {
            return BuildTargetStatus(target, "Work Thread target");
        }

        return _lastForegroundWindow?.ToDisplayText();
    }

    private static string FormatStoredRole(string role)
    {
        if (role.Equals("user", StringComparison.OrdinalIgnoreCase)) return "You";
        if (role.Equals("assistant", StringComparison.OrdinalIgnoreCase)) return "Threadline";
        return role;
    }
}
