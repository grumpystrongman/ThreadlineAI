using System.Text;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ThreadlineUiActionRegistry _uiActions = new();

    private void RegisterUiActions()
    {
        _uiActions.Register("artifact.summary", "Summary", () => CreateArtifactFromConversationAsync("Summary", "Thread Summary"), "artifact.work-artifact");
        _uiActions.Register("artifact.handoff", "Handoff", () => CreateArtifactFromConversationAsync("Handoff", "Work Handoff"), "artifact.work-artifact");
        _uiActions.Register("artifact.decisions", "Decisions", () => CreateArtifactFromConversationAsync("Decisions", "Decision Log"), "artifact.work-artifact");
        _uiActions.Register("artifact.risks", "Risks", () => CreateArtifactFromConversationAsync("Risks", "Risks and Watchouts"), "artifact.work-artifact");
        _uiActions.Register("artifact.next-actions", "Next actions", () => CreateArtifactFromConversationAsync("NextActions", "Next Actions"), "artifact.work-artifact");
        _uiActions.Register("provider.test", "Provider test", RunProviderTestActionAsync, "provider.configured");
        _uiActions.Register("work.resume", "Resume work", ResumeWorkThreadAsync, "memory.work-thread");
        _uiActions.Register("context.clear", "Clear context", ClearSharedContextActionAsync);
        _uiActions.Register("transcript.clear", "Clear transcript", ClearTranscriptActionAsync);
    }

    private Task RunRegisteredUiActionAsync(string actionId) =>
        _uiActions.ExecuteAsync(actionId);

    private Task ClearTranscriptActionAsync()
    {
        _transcriptMessages.Clear();
        AppendTranscript("Threadline", "Transcript cleared.");
        AddTimeline("Transcript cleared through registered action.");
        return Task.CompletedTask;
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

        ServiceStatusText.Text = $"Service: Doctor {report.Readiness} / {passing} of {checks.Count} checks passing";
        TrustControlStatusText.Text = report.Readiness;

        if (setupIssues.Length == 0)
        {
            AddTimeline("Doctor readiness: " + report.Readiness);
            return;
        }

        AddTimeline("Doctor readiness: " + report.Readiness + " — " + string.Join("; ", setupIssues.Select(issue => issue.DisplayName)));
    }
}
