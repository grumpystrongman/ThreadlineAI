using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Threadline.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Threadline.Windows;

public sealed partial class MainWindow : Window
{
    private const int MaxTranscriptItems = 80;
    private const int MaxTranscriptMessageCharacters = 3000;

    private readonly ObservableCollection<TranscriptMessage> _transcriptMessages = new();
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
        TranscriptList.ItemsSource = _transcriptMessages;
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
    private void CopyConversation_Click(object sender, RoutedEventArgs e) => CopyConversationToClipboard();
    private void CopyLastAnswer_Click(object sender, RoutedEventArgs e) => CopyLastAnswerToClipboard();
    private void JumpTranscriptTop_Click(object sender, RoutedEventArgs e) => ScrollTranscriptToTop();
    private void JumpTranscriptBottom_Click(object sender, RoutedEventArgs e) => ScrollTranscriptToBottom(force: true);

    private void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        _transcriptMessages.Clear();
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

        QuestionBox.Text = string.Empty;
        AppendTranscript("You", question);
        var pendingMessage = AppendTranscript("Threadline", "Thinking… resolving context and preparing a response.");
        AddTimeline("Resolving context for Ask...");

        var currentWindow = await ResolveContextForAskAsync();
        AddTimeline("Sending Ask request to provider path...");

        try
        {
            var response = await _client.AskAsync(_session!.Id, question, currentWindow);
            UpdateTranscript(pendingMessage, string.IsNullOrWhiteSpace(response.Answer)
                ? "The provider returned an empty answer."
                : response.Answer);
            AddTimeline("Received provider response.");
        }
        catch (ThreadlineEndpointNotFoundException)
        {
            var messages = await _client.ComposePromptAsync(_session!.Id, question, currentWindow);
            UpdateTranscript(pendingMessage, $"The local service does not expose /ask yet. Prompt composition still works and produced {messages.Count} message(s), but provider response execution is not available from this service build.");
            AddTimeline("Ask endpoint missing; composed prompt fallback completed.");
        }
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
            AppendTranscript("Threadline", "I hit a service/action error: " + ex.Message);
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

    private TranscriptMessage AppendTranscript(string speaker, string message)
    {
        var transcriptMessage = new TranscriptMessage(
            speaker,
            TrimForTranscript(message),
            DateTimeOffset.Now);

        _transcriptMessages.Add(transcriptMessage);
        while (_transcriptMessages.Count > MaxTranscriptItems)
        {
            _transcriptMessages.RemoveAt(0);
        }

        ScrollTranscriptToBottom(force: true);
        return transcriptMessage;
    }

    private void UpdateTranscript(TranscriptMessage transcriptMessage, string message)
    {
        transcriptMessage.Message = TrimForTranscript(message);
        transcriptMessage.Timestamp = DateTimeOffset.Now;
        ScrollTranscriptToBottom(force: true);
    }

    private void CopyConversationToClipboard()
    {
        var text = BuildTranscriptText(_transcriptMessages);
        if (string.IsNullOrWhiteSpace(text)) return;

        SetClipboardText(text);
        AddTimeline("Copied conversation transcript.");
    }

    private void CopyLastAnswerToClipboard()
    {
        var lastAnswer = _transcriptMessages
            .LastOrDefault(message => message.Speaker.StartsWith("Threadline", StringComparison.OrdinalIgnoreCase));

        if (lastAnswer is null || string.IsNullOrWhiteSpace(lastAnswer.Message)) return;

        SetClipboardText(lastAnswer.Message);
        AddTimeline("Copied last Threadline answer.");
    }

    private static string BuildTranscriptText(IEnumerable<TranscriptMessage> messages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine($"{message.Speaker} ({message.TimestampDisplay})");
            builder.Append(message.Message);
        }

        return builder.ToString();
    }

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void ScrollTranscriptToTop()
    {
        TranscriptScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
    }

    private void ScrollTranscriptToBottom(bool force)
    {
        if (!force) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            TranscriptList.UpdateLayout();
            TranscriptScrollViewer.UpdateLayout();
            TranscriptScrollViewer.ChangeView(null, TranscriptScrollViewer.ScrollableHeight, null, disableAnimation: false);
        });
    }

    private static string TrimForTranscript(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length <= MaxTranscriptMessageCharacters) return trimmed;
        return trimmed[..MaxTranscriptMessageCharacters].TrimEnd() + "\n...[trimmed in transcript]";
    }
}

public sealed class TranscriptMessage : System.ComponentModel.INotifyPropertyChanged
{
    private string _message;
    private DateTimeOffset _timestamp;

    public TranscriptMessage(string speaker, string message, DateTimeOffset timestamp)
    {
        Speaker = speaker;
        _message = message;
        _timestamp = timestamp;
    }

    public string Speaker { get; }

    public string Message
    {
        get => _message;
        set
        {
            if (_message == value) return;
            _message = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Message)));
        }
    }

    public DateTimeOffset Timestamp
    {
        get => _timestamp;
        set
        {
            if (_timestamp == value) return;
            _timestamp = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Timestamp)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(TimestampDisplay)));
        }
    }

    public string TimestampDisplay => Timestamp.ToString("t");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
