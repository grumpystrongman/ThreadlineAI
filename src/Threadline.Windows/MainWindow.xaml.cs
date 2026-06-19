using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow : Window
{
    private const int MaxTranscriptItems = 80;
    private const int MaxTranscriptMessageCharacters = 3000;

    private readonly ActiveWindowMonitor _activeWindowMonitor = new();
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly ContextSummarizer _contextSummarizer = new();
    private readonly ThreadlineLocalClient _client = new();
    private ActiveWindowSnapshot? _lastForegroundWindow;
    private ThreadlineSessionDto? _session;
    private WindowAttachmentDto? _attachment;
    private WindowActionDto? _lastAction;
    private NativeUiAutomationResult? _lastNativeUiResult;
    private SummarizedContext? _lastContextSummary;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureSidecarWindow();
        AppendTranscript("Threadline", "Start or use a session, pick an app/tab, then ask Threadline about that target.");
        RefreshActiveWindow();
        StartAutoFollow();
        _ = CheckServiceAsync();
    }

    private async void CheckService_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(CheckServiceAsync);
    private async void StartSession_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(StartSessionAsync);
    private async void UseActiveSession_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(UseActiveSessionAsync);
    private void RefreshWindow_Click(object sender, RoutedEventArgs e) => RefreshActiveWindow();
    private async void AttachWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(AttachWindowAsync);
    private async void PreviewWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(PreviewWindowAsync);
    private async void StoreWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(StoreWindowAsync);
    private async void PreviewNativeUi_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(PreviewNativeUiAsync);
    private async void Ask_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(AskAsync);
    private async void ProposeInsert_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(ProposeInsertActionAsync);
    private async void CompleteLastAction_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(CompleteLastActionAsync);
    private void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        TranscriptList.Items.Clear();
        AppendTranscript("Threadline", "Transcript cleared.");
    }

    private async Task CheckServiceAsync()
    {
        var health = await _client.GetHealthAsync();
        ServiceStatusText.Text = $"Service: {health.Status} / {health.Storage}";
        AddTimeline($"Service connected: {health.Service}");
    }

    private async Task StartSessionAsync()
    {
        var provider = GetSelectedProvider();
        _session = await _client.StartSessionAsync($"Windows companion session {DateTimeOffset.Now:g}", provider);
        SessionText.Text = $"Session: {_session.Status} / {_session.ActiveProvider ?? "None"}";
        AppendTranscript("Threadline Session", FormatSession(_session));
        AddTimeline($"Started session {_session.Id}");
    }

    private async Task UseActiveSessionAsync()
    {
        _session = await _client.GetActiveSessionAsync();
        if (_session is null)
        {
            SessionText.Text = "No active Threadline session.";
            AppendTranscript("Threadline Session", "No active Threadline session found.");
            AddTimeline("No active session found.");
            return;
        }

        SessionText.Text = $"Session: {_session.Status} / {_session.ActiveProvider ?? "None"}";
        AppendTranscript("Threadline Session", FormatSession(_session));
        AddTimeline($"Using active session {_session.Id}");
    }

    private async Task AttachWindowAsync()
    {
        EnsureSession();
        AddTimeline("Target attach armed. Focus the target window now.");
        AppendTranscript("Attach Target", "Focus the target app within 3 seconds.");
        await Task.Delay(TimeSpan.FromSeconds(3));
        _lastForegroundWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        if (_lastForegroundWindow is null)
        {
            throw new InvalidOperationException("No target window snapshot is available.");
        }

        _attachment = await _client.AttachWindowAsync(_session!.Id, _lastForegroundWindow);
        CurrentWindowText.Text = FormatAttachment(_attachment);
        AddTimeline($"Attached target window {_attachment.Snapshot.ApplicationName}: {_attachment.Snapshot.WindowTitle}");
    }

    private async Task PreviewWindowAsync()
    {
        EnsureSession();
        var preview = await _client.PreviewCurrentWindowAsync(_session!.Id, userApproved: true);
        AppendTranscript("Threadline Preview", $"Will store: {preview.WillBeStored}\nConsent: {preview.ConsentState}\nContent:\n{preview.RedactedContent}\nWarnings: {string.Join("; ", preview.Warnings)}");
        AddTimeline("Previewed attached-window context.");
    }

    private async Task StoreWindowAsync()
    {
        EnsureSession();
        var stored = await _client.StoreCurrentWindowAsync(_session!.Id, userApproved: true);
        AddTimeline($"Stored attached-window context {stored.Id}");
        AppendTranscript("Threadline", $"Stored current-window context:\n{stored.Content}");
    }

    private async Task PreviewNativeUiAsync()
    {
        EnsureSession();
        AddTimeline("Native UI capture armed. Focus the target window now.");
        AppendTranscript("Native UI Capture", "Focus the target app within 3 seconds.");
        await Task.Delay(TimeSpan.FromSeconds(3));
        _lastNativeUiResult = _nativeUiAutomationReader.ReadForegroundWindow();
        _lastForegroundWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        CurrentWindowText.Text = _lastForegroundWindow.ToDisplayText();
        _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
        AppendTranscript("Native UI Summary", _lastContextSummary.ToPromptContext());
        AddTimeline(_lastNativeUiResult.Success ? "Summarized target native UI context." : "Target native UI preview found no readable context.");
    }

    private async Task AskAsync()
    {
        EnsureSession();
        var question = QuestionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(question)) return;

        AddTimeline("Resolving context for Ask...");
        var currentWindow = await ResolveContextForAskAsync();
        AppendTranscript("You", question);
        var messages = await _client.ComposePromptAsync(_session!.Id, question, currentWindow);
        AppendTranscript("Threadline", $"Prompt composed with {messages.Count} message(s). Full context was included internally but not dumped into this transcript.");
        QuestionBox.Text = string.Empty;
        AddTimeline("Composed session prompt with full resolved context.");
    }

    private async Task ProposeInsertActionAsync()
    {
        EnsureSession();
        var payload = QuestionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            payload = "Threadline generated text placeholder.";
        }

        _lastAction = await _client.ProposeInsertActionAsync(_session!.Id, payload, userApproved: true);
        AddTimeline($"Proposed and approved action {_lastAction.Id}: {_lastAction.Kind}");
        AppendTranscript("Threadline Action", $"Action {_lastAction.Id}\nStatus: {_lastAction.Status}\nPayload:\n{_lastAction.Payload}");
    }

    private async Task CompleteLastActionAsync()
    {
        if (_lastAction is null)
        {
            AddTimeline("No action to complete.");
            return;
        }

        _lastAction = await _client.CompleteActionAsync(_lastAction.Id, "Completed from Windows companion UI.");
        AddTimeline($"Completed action {_lastAction.Id}");
        AppendTranscript("Threadline Action", $"Action {_lastAction.Id} marked {_lastAction.Status}.");
    }

    private void RefreshActiveWindow()
    {
        _lastForegroundWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        CurrentWindowText.Text = _attachment is null
            ? _lastForegroundWindow.ToDisplayText()
            : FormatAttachment(_attachment) + "\n\nForeground now:\n" + _lastForegroundWindow.ToDisplayText();
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ServiceStatusText.Text = "Service/action error";
            AddTimeline("Error: " + ex.Message);
            AppendTranscript("Error", ex.Message);
        }
    }

    private void EnsureSession()
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Start or load an active session first.");
        }
    }

    private string GetSelectedProvider()
    {
        if (ProviderBox.SelectedItem is ComboBoxItem item && item.Content is not null)
        {
            return item.Content.ToString() ?? "Local";
        }

        return "Local";
    }

    private static string FormatSession(ThreadlineSessionDto session) =>
        $"{session.Name}\nID: {session.Id}\nProvider: {session.ActiveProvider ?? "None"}\nStatus: {session.Status}";

    private static string FormatAttachment(WindowAttachmentDto attachment) =>
        $"Attached: {attachment.Snapshot.ApplicationName}\nProcess: {attachment.Snapshot.ProcessName}\nWindow: {attachment.Snapshot.WindowTitle}\nStatus: {attachment.Status}\nAttachment: {attachment.Id}";

    private void AddTimeline(string message)
    {
        TimelineList.Items.Add($"{DateTimeOffset.Now:t} {message}");
        while (TimelineList.Items.Count > 40)
        {
            TimelineList.Items.RemoveAt(0);
        }

        if (TimelineList.Items.Count > 0)
        {
            TimelineList.ScrollIntoView(TimelineList.Items[^1]);
        }
    }

    private void AppendTranscript(string speaker, string message)
    {
        var safeMessage = TrimForTranscript(message);
        var item = CreateTranscriptItem(speaker, safeMessage);

        TranscriptList.Items.Add(item);
        while (TranscriptList.Items.Count > MaxTranscriptItems)
        {
            TranscriptList.Items.RemoveAt(0);
        }

        TranscriptList.UpdateLayout();
        TranscriptList.ScrollIntoView(item);
    }

    private static Border CreateTranscriptItem(string speaker, string message)
    {
        var speakerBlock = new TextBlock
        {
            Text = speaker,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.9
        };

        var messageBox = new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var timeBlock = new TextBlock
        {
            Text = DateTimeOffset.Now.ToString("t"),
            FontSize = 11,
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var panel = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(speakerBlock);
        panel.Children.Add(messageBox);
        panel.Children.Add(timeBlock);

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 4),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static string TrimForTranscript(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length <= MaxTranscriptMessageCharacters) return trimmed;
        return trimmed[..MaxTranscriptMessageCharacters].TrimEnd() + "\n...[trimmed in transcript]";
    }
}
