using System.Collections.ObjectModel;
using System.Linq;
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
    private string? _lastTrustedContextReceiptId;

    public MainWindow()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = _transcriptMessages;
        ConfigureSidecarWindow();
        AppendTranscript("Threadline", "Start or use a session, pick an app/tab, then ask Threadline about that target.");
        RefreshActiveWindow();
        StartAutoFollow();
        _ = RunUiActionAsync(CheckServiceAsync);
        _ = RunUiActionAsync(InitializeWorkThreadAsync);
    }

    private async void CheckService_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(CheckServiceAsync);
    private async void StartSession_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(StartSessionAsync);
    private async void UseActiveSession_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(UseActiveSessionAsync);
    private async void RefreshWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => { RefreshActiveWindow(); return Task.CompletedTask; });
    private async void AttachWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(AttachWindowAsync);
    private async void PreviewWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(PreviewWindowAsync);
    private async void StoreWindow_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(StoreWindowAsync);
    private async void PreviewNativeUi_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(PreviewNativeUiAsync);
    private async void Ask_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(AskAsync);
    private async void TrustedAsk_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(TrustedAskAsync);
    private async void ProposeInsert_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(ProposeInsertActionAsync);
    private async void CompleteLastAction_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(CompleteLastActionAsync);
    private async void CopyConversation_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => { CopyConversationToClipboard(); return Task.CompletedTask; });
    private async void CopyLastAnswer_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => { CopyLastAnswerToClipboard(); return Task.CompletedTask; });
    private async void JumpTranscriptTop_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => { ScrollTranscriptToTop(); return Task.CompletedTask; });
    private async void JumpTranscriptBottom_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => { ScrollTranscriptToBottom(force: true); return Task.CompletedTask; });
    private async void CreateSummaryArtifact_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => CreateArtifactFromConversationAsync("Summary", "Thread Summary"));
    private async void CreateHandoffArtifact_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => CreateArtifactFromConversationAsync("Handoff", "Work Handoff"));
    private async void CreateDecisionsArtifact_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => CreateArtifactFromConversationAsync("Decisions", "Decision Log"));
    private async void CreateRisksArtifact_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => CreateArtifactFromConversationAsync("Risks", "Risks and Watchouts"));
    private async void CreateNextActionsArtifact_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(() => CreateArtifactFromConversationAsync("NextActions", "Next Actions"));

    private async void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() =>
        {
            _transcriptMessages.Clear();
            AppendTranscript("Threadline", "Transcript cleared.");
            return Task.CompletedTask;
        });
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
        CurrentWindowText.Text = _lastForegroundWindow?.ToDisplayText() ?? "No foreground app detected.";
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
        await PersistTranscriptMessageAsync("user", question);

        var pendingMessage = AppendTranscript("Threadline", "Thinking… resolving context and preparing a response.");
        AddTimeline("Resolving context for Ask...");

        var currentWindow = await ResolveContextForAskAsync();
        await PersistTargetContextEventAsync(_selectedThreadlineTarget ?? _lastFollowTarget, _selectedThreadlineTarget is null ? "Inferred" : "Followed");
        var contextReceipt = await PersistContextReceiptForAskAsync(currentWindow);
        AddTimeline("Sending Ask request to provider path...");

        try
        {
            var response = await _client.AskAsync(_session!.Id, question, currentWindow);
            var answer = string.IsNullOrWhiteSpace(response.Answer)
                ? "The provider returned an empty answer."
                : response.Answer;
            var receiptText = BuildContextReceiptText(contextReceipt, currentWindow);
            var answerWithReceipt = answer + "\n\n" + receiptText;
            UpdateTranscript(pendingMessage, answerWithReceipt);
            await PersistTranscriptMessageAsync("assistant", answerWithReceipt, contextReceipt?.Id);
            AddTimeline("Received provider response and saved Work Thread message.");
        }
        catch (ThreadlineEndpointNotFoundException ex)
        {
            await ShowLocalAskFallbackAsync(pendingMessage, question, currentWindow, "Ask endpoint missing", ex.Message);
            await PersistTranscriptMessageAsync("assistant", pendingMessage.Message, contextReceipt?.Id);
        }
        catch (InvalidOperationException ex)
        {
            await ShowLocalAskFallbackAsync(pendingMessage, question, currentWindow, "Ask provider call failed", ex.Message);
            await PersistTranscriptMessageAsync("assistant", pendingMessage.Message, contextReceipt?.Id);
        }
    }

    private async Task TrustedAskAsync()
    {
        EnsureSession();
        var question = QuestionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(question)) return;

        await EnsureDurableWorkThreadAsync();

        QuestionBox.Text = string.Empty;
        AppendTranscript("You", question);
        if (IsWorkThreadMemoryEnabled())
        {
            await PersistTranscriptMessageAsync("user", question);
        }

        var pendingMessage = AppendTranscript("Threadline", "Thinking… checking trust controls, resolving context, and preparing a response.");
        AddTimeline("Checking trust controls for Ask...");

        string? currentWindow = null;
        if (IsProviderContextAllowed())
        {
            currentWindow = await ResolveContextForAskAsync();
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTargetContextEventAsync(_selectedThreadlineTarget ?? _lastFollowTarget, _selectedThreadlineTarget is null ? "Inferred" : "Followed");
            }
        }
        else
        {
            TrustControlStatusText.Text = "Provider context send is off. This Ask sends the question without the resolved app context.";
            AddTimeline("Provider context send blocked by privacy control.");
        }

        ContextReceiptDto? contextReceipt = null;
        if (IsContextReceiptEnabled())
        {
            contextReceipt = await PersistContextReceiptForAskAsync(currentWindow);
            _lastTrustedContextReceiptId = contextReceipt?.Id;
        }

        AddTimeline("Sending trusted Ask request to provider path...");

        try
        {
            var response = await _client.AskAsync(_session!.Id, question, currentWindow, IsProviderContextAllowed() ? 20 : 0);
            var answer = string.IsNullOrWhiteSpace(response.Answer)
                ? "The provider returned an empty answer."
                : response.Answer;

            var answerWithReceipt = IsContextReceiptEnabled()
                ? answer + "\n\n" + BuildContextReceiptText(contextReceipt, currentWindow)
                : answer + "\n\nContext Receipt hidden by privacy/trust controls.";

            UpdateTranscript(pendingMessage, answerWithReceipt);
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTranscriptMessageAsync("assistant", answerWithReceipt, contextReceipt?.Id);
            }

            AddTimeline("Received trusted provider response.");
            TrustControlStatusText.Text = IsProviderContextAllowed()
                ? "Provider answer returned with current trust controls."
                : "Provider answer returned without resolved app context or recent service context.";
        }
        catch (ThreadlineEndpointNotFoundException ex)
        {
            await ShowLocalAskFallbackAsync(pendingMessage, question, currentWindow, "Ask endpoint missing", ex.Message);
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTranscriptMessageAsync("assistant", pendingMessage.Message, contextReceipt?.Id);
            }
        }
        catch (InvalidOperationException ex)
        {
            await ShowLocalAskFallbackAsync(pendingMessage, question, currentWindow, "Ask provider call failed", ex.Message);
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTranscriptMessageAsync("assistant", pendingMessage.Message, contextReceipt?.Id);
            }
        }
    }

    private async Task ShowLocalAskFallbackAsync(TranscriptMessage pendingMessage, string question, string? currentWindow, string reason, string detail)
    {
        try
        {
            var messages = await _client.ComposePromptAsync(_session!.Id, question, currentWindow);
            UpdateTranscript(pendingMessage, BuildLocalAskFallbackMessage(messages.Count, currentWindow, reason, detail));
            AddTimeline($"{reason}; local visibility fallback shown.");
        }
        catch (Exception fallbackException)
        {
            UpdateTranscript(pendingMessage, BuildLocalAskFallbackMessage(0, currentWindow, reason, detail + " Fallback compose also failed: " + fallbackException.Message));
            AddTimeline($"{reason}; local visibility fallback shown without prompt composition.");
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

    private async Task EnsureDurableWorkThreadAsync()
    {
        if (!IsWorkThreadMemoryEnabled() && !IsContextReceiptEnabled()) return;
        if (_activeWorkThread is not null && !_activeWorkThread.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase)) return;

        var active = await _workThreadClient.GetActiveWorkThreadAsync();
        _activeWorkThread = active is null
            ? await _workThreadClient.StartWorkThreadAsync(BuildDefaultWorkThreadTitle(), BuildCurrentTargetDescription())
            : await _workThreadClient.ResumeWorkThreadAsync(active.Id);

        UpdateWorkThreadUi();
        AddTimeline(active is null ? "Started Work Thread memory for Ask." : "Resumed Work Thread memory for Ask.");
    }

    private async Task CreateArtifactFromConversationAsync(string artifactType, string title)
    {
        await EnsureArtifactWorkThreadAsync();
        var content = BuildArtifactContent(artifactType, title);
        if (string.IsNullOrWhiteSpace(content))
        {
            AppendTranscript("Threadline Artifact", "There is not enough conversation content to create that artifact yet.");
            return;
        }

        var saved = await _workThreadClient.SaveArtifactAsync(_activeWorkThread!.Id, artifactType, title, content, _lastTrustedContextReceiptId);
        var message = $"Saved artifact: {saved.Title}\nType: {saved.ArtifactType}\nID: {saved.Id}\n\n{saved.Content}";
        AppendTranscript("Threadline Artifact", message);
        if (IsWorkThreadMemoryEnabled())
        {
            await PersistTranscriptMessageAsync("artifact", message, saved.ContextReceiptId);
        }

        AddTimeline($"Saved artifact action: {saved.ArtifactType}.");
    }

    private async Task EnsureArtifactWorkThreadAsync()
    {
        if (_activeWorkThread is not null && !_activeWorkThread.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase)) return;

        var active = await _workThreadClient.GetActiveWorkThreadAsync();
        _activeWorkThread = active is null
            ? await _workThreadClient.StartWorkThreadAsync(BuildDefaultWorkThreadTitle(), BuildCurrentTargetDescription())
            : await _workThreadClient.ResumeWorkThreadAsync(active.Id);

        UpdateWorkThreadUi();
    }

    private string BuildArtifactContent(string artifactType, string title)
    {
        var latestAnswer = GetLatestThreadlineMessage();
        var transcript = GetRecentTranscriptText(12);
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine($"Created: {DateTimeOffset.Now:g}");
        builder.AppendLine($"Work Thread: {_activeWorkThread?.Title ?? "None"}");
        builder.AppendLine("Source: current sidecar transcript");
        if (!string.IsNullOrWhiteSpace(_lastTrustedContextReceiptId))
        {
            builder.AppendLine($"Context Receipt: {_lastTrustedContextReceiptId}");
        }

        builder.AppendLine();

        switch (artifactType)
        {
            case "Summary":
                builder.AppendLine("## Summary");
                builder.AppendLine(SummarizeBlock(latestAnswer ?? transcript));
                builder.AppendLine();
                builder.AppendLine("## Recent conversation");
                builder.AppendLine(transcript);
                break;
            case "Handoff":
                builder.AppendLine("## Current state");
                builder.AppendLine(SummarizeBlock(latestAnswer ?? transcript));
                builder.AppendLine();
                builder.AppendLine("## Handoff notes");
                builder.AppendLine("- Continue from the active Work Thread and current context receipt.");
                builder.AppendLine("- Validate any inferred detail before acting outside the current app context.");
                builder.AppendLine("- Use the artifact actions again after the next major answer.");
                break;
            case "Decisions":
                builder.AppendLine("## Decisions / commitments");
                AppendFilteredLines(builder, transcript, new[] { "decid", "decision", "choose", "selected", "will ", "agreed", "commit" }, "No explicit decisions detected yet.");
                break;
            case "Risks":
                builder.AppendLine("## Risks / watchouts");
                AppendFilteredLines(builder, transcript, new[] { "risk", "warning", "limitation", "blocked", "error", "failed", "not used", "privacy" }, "No explicit risks detected yet.");
                break;
            case "NextActions":
                builder.AppendLine("## Next actions");
                AppendFilteredLines(builder, transcript, new[] { "next", "action", "todo", "follow up", "should", "need to", "verify", "validate" }, "No explicit next actions detected yet.");
                break;
            default:
                builder.AppendLine(transcript);
                break;
        }

        return builder.ToString().Trim();
    }

    private string? GetLatestThreadlineMessage()
    {
        return _transcriptMessages
            .LastOrDefault(message => message.Speaker.StartsWith("Threadline", StringComparison.OrdinalIgnoreCase)
                                      && !string.IsNullOrWhiteSpace(message.Message))
            ?.Message;
    }

    private string GetRecentTranscriptText(int take)
    {
        var messages = _transcriptMessages.TakeLast(Math.Max(1, take));
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine($"{message.Speaker}: {message.Message}");
        }

        return builder.ToString().Trim();
    }

    private static string SummarizeBlock(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "No content available yet.";
        var normalized = value.Replace("\r", " ").Trim();
        return normalized.Length <= 1200 ? normalized : normalized[..1200].TrimEnd() + "...";
    }

    private static void AppendFilteredLines(StringBuilder builder, string transcript, string[] keywords, string fallback)
    {
        var lines = transcript
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => keywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Take(12)
            .ToList();

        if (lines.Count == 0)
        {
            builder.AppendLine($"- {fallback}");
            return;
        }

        foreach (var line in lines)
        {
            builder.AppendLine($"- {line}");
        }
    }

    private bool IsProviderContextAllowed() => GetToggleValue(AllowProviderContextToggle, defaultValue: true);
    private bool IsWorkThreadMemoryEnabled() => GetToggleValue(SaveWorkThreadMemoryToggle, defaultValue: true);
    private bool IsContextReceiptEnabled() => GetToggleValue(ShowContextReceiptsToggle, defaultValue: true);

    private static bool GetToggleValue(CheckBox toggle, bool defaultValue)
    {
        try
        {
            return toggle.IsChecked ?? defaultValue;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Toggle read failed: {ex.GetType().Name}: {ex.Message}");
            return defaultValue;
        }
    }

    private void RefreshActiveWindow()
    {
        _lastForegroundWindow = _activeWindowMonitor.GetActiveWindowSnapshot();
        var foregroundText = _lastForegroundWindow?.ToDisplayText() ?? "No foreground app detected.";
        CurrentWindowText.Text = _attachment is null
            ? foregroundText
            : FormatAttachment(_attachment) + "\n\nForeground now:\n" + foregroundText;
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            try
            {
                ServiceStatusText.Text = "Service/action error";
            }
            catch (Exception uiEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Threadline] Secondary UI update failed: {uiEx.GetType().Name}: {uiEx.Message}");
            }

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
            return item.Content.ToString() ?? "OpenAI";
        }

        return "OpenAI";
    }

    private static string FormatSession(ThreadlineSessionDto session) =>
        $"{session.Name}\nID: {session.Id}\nProvider: {session.ActiveProvider ?? "None"}\nStatus: {session.Status}";

    private static string FormatAttachment(WindowAttachmentDto attachment) =>
        $"Attached: {attachment.Snapshot.ApplicationName}\nProcess: {attachment.Snapshot.ProcessName}\nWindow: {attachment.Snapshot.WindowTitle}\nStatus: {attachment.Status}\nAttachment: {attachment.Id}";

    private void AddTimeline(string message)
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Timeline update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private TranscriptMessage AppendTranscript(string speaker, string message)
    {
        var transcriptMessage = new TranscriptMessage(
            speaker,
            TrimForTranscript(message),
            DateTimeOffset.Now);

        try
        {
            _transcriptMessages.Add(transcriptMessage);
            while (_transcriptMessages.Count > MaxTranscriptItems)
            {
                _transcriptMessages.RemoveAt(0);
            }

            ScrollTranscriptToBottom(force: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Transcript append failed: {ex.GetType().Name}: {ex.Message}");
        }

        return transcriptMessage;
    }

    private void UpdateTranscript(TranscriptMessage transcriptMessage, string message)
    {
        try
        {
            transcriptMessage.Message = TrimForTranscript(message);
            transcriptMessage.Timestamp = DateTimeOffset.Now;
            ScrollTranscriptToBottom(force: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Transcript update failed: {ex.GetType().Name}: {ex.Message}");
        }
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

    private string BuildLocalAskFallbackMessage(int composedMessageCount, string? currentWindow, string reason, string detail)
    {
        var builder = new StringBuilder();
        builder.AppendLine("I could not get a provider-written answer, but Threadline did resolve local context.");
        builder.AppendLine($"Reason: {reason}.");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.AppendLine($"Detail: {SummarizeServiceError(detail)}");
        }

        if (_lastContextSummary is not null)
        {
            builder.AppendLine();
            builder.AppendLine("What I can actually see:");
            builder.AppendLine($"- Target: {_lastContextSummary.Title}");
            builder.AppendLine($"- Status: {BuildContextStatus(_lastContextSummary)}");
            builder.AppendLine($"- Source: {_lastContextSummary.Source}");
            builder.AppendLine($"- Confidence: {_lastContextSummary.Confidence}");

            if (_lastContextSummary.Process is not null)
            {
                builder.AppendLine($"- App: {_lastContextSummary.Process.ProcessName} ({_lastContextSummary.Process.AppType})");
                builder.AppendLine($"- Window: {_lastContextSummary.Process.WindowTitle}");
            }

            builder.AppendLine();
            builder.AppendLine("Summary:");
            builder.AppendLine(_lastContextSummary.Summary);

            if (_lastContextSummary.KeyDetails.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Key details I can use:");
                foreach (var detailItem in _lastContextSummary.KeyDetails.Take(8))
                {
                    builder.AppendLine($"- {detailItem}");
                }
            }

            if (_lastContextSummary.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Warnings:");
                foreach (var warning in _lastContextSummary.Warnings.Take(5))
                {
                    builder.AppendLine($"- {warning}");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(currentWindow))
        {
            builder.AppendLine();
            builder.AppendLine("What I can actually see:");
            builder.AppendLine(currentWindow);
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("I do not have readable target context yet. Select an app/tab and click Use, or let Follow mode identify the last non-Threadline app before asking again.");
        }

        builder.AppendLine();
        builder.AppendLine($"Prompt composition produced {composedMessageCount} message(s). Configure the active provider to get a full provider-written answer.");
        return builder.ToString();
    }

    private static string SummarizeServiceError(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= 600) return normalized;
        return normalized[..600].TrimEnd() + "...";
    }

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void ScrollTranscriptToTop()
    {
        try
        {
            TranscriptScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Scroll to top failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ScrollTranscriptToBottom(bool force)
    {
        if (!force) return;

        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    TranscriptList.UpdateLayout();
                    TranscriptScrollViewer.UpdateLayout();
                    TranscriptScrollViewer.ChangeView(null, TranscriptScrollViewer.ScrollableHeight, null, disableAnimation: false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Threadline] Scroll layout update failed: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] Scroll dispatch failed: {ex.GetType().Name}: {ex.Message}");
        }
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
