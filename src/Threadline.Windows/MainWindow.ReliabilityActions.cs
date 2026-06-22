using System.Text;
using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ThreadlineUiActionRegistry _uiActions = new();
    private bool _uiActionsRegistered;
    private WorkArtifactDto? _lastArtifact;

    private void RegisterUiActions()
    {
        _uiActions.Register("artifact.summary", "Summary", () => RunArtifactActionAsync("artifact.summary"), "artifact.work-artifact");
        _uiActions.Register("artifact.handoff", "Handoff", () => RunArtifactActionAsync("artifact.handoff"), "artifact.work-artifact");
        _uiActions.Register("artifact.decisions", "Decisions", () => RunArtifactActionAsync("artifact.decisions"), "artifact.work-artifact");
        _uiActions.Register("artifact.risks", "Risks", () => RunArtifactActionAsync("artifact.risks"), "artifact.work-artifact");
        _uiActions.Register("artifact.next-actions", "Next actions", () => RunArtifactActionAsync("artifact.next-actions"), "artifact.work-artifact");
        _uiActions.Register("artifact.copy", "Copy artifact", CopyLastArtifactActionAsync, "artifact.work-artifact");
        _uiActions.Register("artifact.export", "Export artifact", ExportLastArtifactActionAsync, "artifact.work-artifact");
        _uiActions.Register("artifact.regenerate", "Regenerate artifact", RegenerateLastArtifactActionAsync, "artifact.work-artifact");
        _uiActions.Register("provider.test", "Provider test", RunProviderTestActionAsync, "provider.configured");
        _uiActions.Register("work.resume", "Resume work", ResumeWorkThreadAsync, "memory.work-thread");
        _uiActions.Register("context.clear", "Clear context", ClearSharedContextActionAsync, "memory.work-thread");
        _uiActions.Register("conversation.clear", "Clear conversation", ClearConversationActionAsync, "memory.work-thread");
        _uiActions.Register("memory.clear", "Clear memory", ClearMemoryActionAsync, "memory.work-thread");
        _uiActions.Register("transcript.clear", "Clear transcript", ClearTranscriptActionAsync);
    }

    private void EnsureUiActionsRegistered()
    {
        if (_uiActionsRegistered)
        {
            return;
        }

        RegisterUiActions();
        _uiActionsRegistered = true;
    }

    private Task RunRegisteredUiActionAsync(string actionId)
    {
        EnsureUiActionsRegistered();
        return _uiActions.ExecuteAsync(actionId);
    }

    private async void ProviderTest_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("provider.test"));

    private async void SummaryArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.summary"));

    private async void HandoffArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.handoff"));

    private async void DecisionsArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.decisions"));

    private async void RisksArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.risks"));

    private async void NextActionsArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.next-actions"));

    private async void CopyArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.copy"));

    private async void ExportArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.export"));

    private async void RegenerateArtifactAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("artifact.regenerate"));

    private async void ClearConversationAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("conversation.clear"));

    private async void ClearMemoryAction_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => RunRegisteredUiActionAsync("memory.clear"));

    private Task ClearTranscriptActionAsync()
    {
        _transcriptMessages.Clear();
        AppendTranscript("Threadline", "Transcript cleared. Work Thread memory was not deleted.");
        AddTimeline("Transcript cleared through registered action.");
        return Task.CompletedTask;
    }

    private async Task RunArtifactActionAsync(string actionId)
    {
        await EnsureArtifactWorkThreadAsync();
        var result = await _workThreadClient.RunActionAsync(
            actionId,
            _activeWorkThread?.Id,
            BuildTranscriptText(_transcriptMessages),
            BuildCurrentActionContextSummary());
        ApplyActionExecutionResult(result, persistAsArtifactMessage: true);
        await RefreshDoctorReadinessAsync();
    }

    private async Task CopyLastArtifactActionAsync()
    {
        await EnsureArtifactWorkThreadAsync();
        var result = await _workThreadClient.RunActionAsync(
            "artifact.copy",
            _activeWorkThread?.Id,
            artifactId: _lastArtifact?.Id);
        if (result.Artifact is not null)
        {
            _lastArtifact = result.Artifact;
        }

        if (result.Metadata is not null && result.Metadata.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content))
        {
            SetClipboardText(content);
            AddTimeline("Copied artifact content through registered action.");
        }

        ApplyActionExecutionResult(result, persistAsArtifactMessage: false);
    }

    private async Task ExportLastArtifactActionAsync()
    {
        await EnsureArtifactWorkThreadAsync();
        var result = await _workThreadClient.RunActionAsync(
            "artifact.export",
            _activeWorkThread?.Id,
            artifactId: _lastArtifact?.Id);
        if (result.Artifact is not null)
        {
            _lastArtifact = result.Artifact;
        }

        if (result.Metadata is not null && result.Metadata.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content))
        {
            SetClipboardText(content);
            var fileName = result.Metadata.TryGetValue("fileName", out var proposedFileName) ? proposedFileName : "ThreadlineArtifact.md";
            AddTimeline($"Prepared artifact export and copied Markdown: {fileName}.");
        }

        ApplyActionExecutionResult(result, persistAsArtifactMessage: false);
    }

    private async Task RegenerateLastArtifactActionAsync()
    {
        await EnsureArtifactWorkThreadAsync();
        if (_lastArtifact is null)
        {
            var artifacts = await _workThreadClient.GetArtifactsAsync(_activeWorkThread!.Id, 1);
            _lastArtifact = artifacts.FirstOrDefault();
        }

        var result = await _workThreadClient.RunActionAsync(
            "artifact.regenerate",
            _activeWorkThread?.Id,
            BuildTranscriptText(_transcriptMessages),
            BuildCurrentActionContextSummary(),
            _lastArtifact?.Id);
        ApplyActionExecutionResult(result, persistAsArtifactMessage: true);
    }

    private async Task ClearConversationActionAsync()
    {
        if (_activeWorkThread is null)
        {
            _transcriptMessages.Clear();
            AppendTranscript("Threadline", "Conversation cleared locally. No active Work Thread memory was selected.");
            AddTimeline("Conversation cleared locally; no active Work Thread.");
            return;
        }

        var result = await _workThreadClient.RunActionAsync("conversation.clear", _activeWorkThread.Id);
        _transcriptMessages.Clear();
        AppendTranscript("Threadline", result.Message);
        AddTimeline(result.Message);
    }

    private async Task ClearMemoryActionAsync()
    {
        if (_activeWorkThread is null)
        {
            ClearLocalWorkingState();
            _transcriptMessages.Clear();
            AppendTranscript("Threadline", "Local memory state cleared. No active Work Thread was selected for durable deletion.");
            return;
        }

        var result = await _workThreadClient.RunActionAsync("memory.clear", _activeWorkThread.Id);
        ClearLocalWorkingState();
        _activeWorkThread = null;
        _lastArtifact = null;
        _transcriptMessages.Clear();
        UpdateWorkThreadUi();
        AppendTranscript("Threadline", result.Message);
        AddTimeline(result.Message);
    }

    private void ApplyActionExecutionResult(ThreadlineActionExecutionResultDto result, bool persistAsArtifactMessage)
    {
        if (result.Artifact is not null)
        {
            _lastArtifact = result.Artifact;
        }

        var builder = new StringBuilder();
        builder.AppendLine(result.Message);
        builder.AppendLine($"Action: {result.ActionId}");
        builder.AppendLine($"Status: {result.Status}");
        if (result.Artifact is not null)
        {
            builder.AppendLine($"Artifact: {result.Artifact.Title} ({result.Artifact.ArtifactType})");
            builder.AppendLine($"ID: {result.Artifact.Id}");
            builder.AppendLine();
            builder.AppendLine(result.Artifact.Content);
        }

        var message = builder.ToString().Trim();
        AppendTranscript("Threadline Action", message);
        AddTimeline($"Registered action {result.ActionId}: {result.Status}.");
        ServiceStatusText.Text = $"Action {result.ActionId}: {result.Status}";

        if (persistAsArtifactMessage && IsWorkThreadMemoryEnabled() && _activeWorkThread is not null)
        {
            _ = PersistTranscriptMessageAsync("artifact", message, result.Artifact?.ContextReceiptId);
        }
    }

    private string BuildCurrentActionContextSummary()
    {
        var builder = new StringBuilder();
        var target = _selectedThreadlineTarget ?? _lastFollowTarget;
        if (target is not null)
        {
            builder.AppendLine($"Target: {target.Title}");
            builder.AppendLine($"App: {target.Window.ApplicationName}");
            builder.AppendLine($"Window: {target.Window.WindowTitle}");
            builder.AppendLine($"Confidence: {target.Confidence}");
        }

        if (_lastContextSummary is not null)
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine(_lastContextSummary.ToPromptContext());
        }

        if (_lastForegroundWindow is not null)
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine(_lastForegroundWindow.ToDisplayText());
        }

        return builder.ToString().Trim();
    }

    private async Task RunProviderTestActionAsync()
    {
        var provider = GetSelectedSettingsProvider();
        ServiceStatusText.Text = $"Testing provider: {provider}";
        AddTimeline($"Running provider test for {provider}...");

        try
        {
            var result = await _client.TestProviderAsync(provider);
            var builder = new StringBuilder();
            builder.AppendLine($"Provider: {result.ProviderName}");
            builder.AppendLine($"Status: {result.Status}");
            builder.AppendLine($"Success: {result.Success}");
            builder.AppendLine($"Detail: {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.Model))
            {
                builder.AppendLine($"Model: {result.Model}");
            }

            if (result.DurationMs > 0)
            {
                builder.AppendLine($"Duration: {result.DurationMs} ms");
            }

            AppendTranscript("Threadline Provider Test", builder.ToString());
            ServiceStatusText.Text = result.Success
                ? $"Provider test passed: {result.ProviderName}"
                : $"Provider test needs attention: {result.ProviderName}";
            AddTimeline(result.Success ? "Provider test passed." : "Provider test returned a setup issue.");
            await RefreshDoctorReadinessAsync();
        }
        catch (Exception ex)
        {
            var localValidation = await TestProviderSettingsAsync();
            ServiceStatusText.Text = "Provider test failed";
            AppendTranscript(
                "Threadline Provider Test",
                $"Provider test failed before or during service execution.\n\nService error: {ex.Message}\n\nLocal settings check: {localValidation}");
            AddTimeline("Provider test failed: " + ex.Message);
        }
    }

    private async Task RefreshDoctorReadinessAsync()
    {
        try
        {
            var report = await _client.GetDoctorAsync();
            ApplyDoctorReport(report);
        }
        catch (Exception ex)
        {
            ServiceStatusText.Text = "Service: connected / Doctor unavailable";
            AddTimeline("Doctor endpoint unavailable: " + ex.Message);
        }
    }

    private void ApplyDoctorReport(ThreadlineDoctorReportDto report)
    {
        var checks = report.Checks ?? Array.Empty<ThreadlineDoctorCheckDto>();
        var passing = checks.Count(check => check.Status.Equals("Pass", StringComparison.OrdinalIgnoreCase));
        var setupIssues = checks
            .Where(check => check.Status.Equals("Fail", StringComparison.OrdinalIgnoreCase) || check.Status.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        ServiceStatusText.Text = $"Service: Doctor {report.Readiness} / {passing} of {checks.Count} checks passing / {report.Actions?.Count ?? 0} actions";
        TrustControlStatusText.Text = report.Readiness;

        if (setupIssues.Length == 0)
        {
            AddTimeline($"Doctor readiness: {report.Readiness}; action catalog: {report.Actions?.Count ?? 0} action(s).");
            return;
        }

        AddTimeline("Doctor readiness: " + report.Readiness + " — " + string.Join("; ", setupIssues.Select(issue => issue.DisplayName)) + $"; action catalog: {report.Actions?.Count ?? 0} action(s).");
    }
}
