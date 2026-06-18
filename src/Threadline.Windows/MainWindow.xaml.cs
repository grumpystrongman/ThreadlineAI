using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow : Window
{
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
        RefreshActiveWindow();
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
    private void ClearTranscript_Click(object sender, RoutedEventArgs e) => ChatTranscript.Text = "Transcript cleared.";

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
        SessionText.Text = $"{_session.Name}\nID: {_session.Id}\nProvider: {_session.ActiveProvider ?? provider}\nStatus: {_session.Status}";
        AddTimeline($"Started session {_session.Id}");
    }

    private async Task UseActiveSessionAsync()
    {
        _session = await _client.GetActiveSessionAsync();
        if (_session is null)
        {
            SessionText.Text = "No active Threadline session found.";
            AddTimeline("No active session found.");
            return;
        }

        SessionText.Text = $"{_session.Name}\nID: {_session.Id}\nProvider: {_session.ActiveProvider ?? "None"}\nStatus: {_session.Status}";
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

        var currentWindow = _lastContextSummary is not null
            ? _lastContextSummary.ToPromptContext()
            : _lastNativeUiResult is { Success: true }
                ? _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult).ToPromptContext()
                : _attachment is not null ? FormatAttachment(_attachment) : _lastForegroundWindow?.ToDisplayText();
        AppendTranscript("You", question);
        var messages = await _client.ComposePromptAsync(_session!.Id, question, currentWindow);
        AppendTranscript("Threadline Prompt", string.Join("\n\n---\n\n", messages.Select(message => $"{message.Role}:\n{message.Content}")));
        QuestionBox.Text = string.Empty;
        AddTimeline("Composed session prompt with summarized context.");
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

    private static string FormatAttachment(WindowAttachmentDto attachment) =>
        $"Attached: {attachment.Snapshot.ApplicationName}\nProcess: {attachment.Snapshot.ProcessName}\nWindow: {attachment.Snapshot.WindowTitle}\nStatus: {attachment.Status}\nAttachment: {attachment.Id}";

    private void AddTimeline(string message) => TimelineList.Items.Add($"{DateTimeOffset.Now:t} {message}");

    private void AppendTranscript(string speaker, string message)
    {
        ChatTranscript.Text += $"\n\n{speaker}:\n{message}";
    }
}
