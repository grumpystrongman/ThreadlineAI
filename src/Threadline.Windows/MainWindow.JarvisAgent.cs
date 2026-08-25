using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Threadline.Windows.Services;
using Windows.System;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ThreadlineAgentClient _agentClient = new();
    private bool _jarvisComposerWired;

    private void WireJarvisComposer()
    {
        if (_jarvisComposerWired) return;

        QuestionBox.KeyDown -= QuestionBox_KeyDown;
        QuestionBox.KeyDown += JarvisQuestionBox_KeyDown;
        RewireAskButtons(RootShell);
        _jarvisComposerWired = true;
    }

    private void RewireAskButtons(DependencyObject root)
    {
        if (root is Button button && string.Equals(button.Content?.ToString(), "Ask", StringComparison.Ordinal))
        {
            button.Click -= TrustedAsk_Click;
            button.Click += JarvisAsk_Click;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            RewireAskButtons(VisualTreeHelper.GetChild(root, index));
        }
    }

    private async void JarvisAsk_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(JarvisAgentAskAsync);

    private void JarvisQuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter
            && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            _ = RunUiActionAsync(JarvisAgentAskAsync);
        }
    }

    private async Task JarvisAgentAskAsync()
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

        var pendingMessage = AppendTranscript("AIKA / JARVIS", "Understanding the request and deciding whether to answer or act…");
        AddTimeline("AIKA / JARVIS routing request between Direct and Mission paths...");

        string? currentWindow = null;
        if (IsProviderContextAllowed())
        {
            currentWindow = await ResolveContextForAskAsync();
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTargetContextEventAsync(
                    _selectedThreadlineTarget ?? _lastFollowTarget,
                    _selectedThreadlineTarget is null ? "Inferred" : "Followed");
            }
        }
        else
        {
            TrustControlStatusText.Text = "Context sharing is off. AIKA / JARVIS receives the user request without resolved app context.";
            AddTimeline("Agent context send blocked by privacy control.");
        }

        ContextReceiptDto? contextReceipt = null;
        if (IsContextReceiptEnabled())
        {
            contextReceipt = await PersistContextReceiptForAskAsync(currentWindow);
            _lastTrustedContextReceiptId = contextReceipt?.Id;
        }

        var takeRecentEvents = IsProviderContextAllowed() ? 20 : 0;
        AgentAskResponseDto response;
        try
        {
            response = await _agentClient.AskAsync(
                _session!.Id,
                question,
                currentWindow,
                takeRecentEvents,
                route: "Auto",
                confirmed: false);
        }
        catch (Exception ex)
        {
            await ShowLocalAskFallbackAsync(
                pendingMessage,
                question,
                currentWindow,
                "AIKA / JARVIS agent route failed",
                ex.Message);
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTranscriptMessageAsync("assistant", pendingMessage.Message, contextReceipt?.Id);
            }
            return;
        }

        if (response.RequiresConfirmation)
        {
            var approved = await ConfirmJarvisMissionAsync(response);
            if (!approved)
            {
                UpdateTranscript(pendingMessage, "Mission cancelled before execution. No destructive action was approved.");
                if (IsWorkThreadMemoryEnabled())
                {
                    await PersistTranscriptMessageAsync("assistant", pendingMessage.Message, contextReceipt?.Id);
                }
                AddTimeline("Jarvis destructive mission confirmation declined.");
                return;
            }

            response = await _agentClient.AskAsync(
                _session!.Id,
                question,
                currentWindow,
                takeRecentEvents,
                route: "Mission",
                confirmed: true);
        }

        if (response.Route.StartsWith("Direct", StringComparison.OrdinalIgnoreCase))
        {
            var answer = string.IsNullOrWhiteSpace(response.DirectResponse?.Answer)
                ? "The provider returned an empty answer."
                : response.DirectResponse!.Answer;
            var routeNote = response.Route.Equals("DirectFallback", StringComparison.OrdinalIgnoreCase)
                ? "\n\nAgent note: Jarvis was unavailable, so Threadline used the direct provider path."
                : string.Empty;
            var receiptText = IsContextReceiptEnabled()
                ? "\n\n" + BuildContextReceiptText(contextReceipt, currentWindow)
                : "\n\nContext Receipt hidden by privacy/trust controls.";
            var answerWithReceipt = answer + routeNote + receiptText;

            UpdateTranscript(pendingMessage, answerWithReceipt);
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTranscriptMessageAsync("assistant", answerWithReceipt, contextReceipt?.Id);
            }
            AddTimeline($"AIKA / JARVIS completed on {response.Route} path: {response.Reason}");
            return;
        }

        if (string.IsNullOrWhiteSpace(response.MissionId))
        {
            var failure = $"Jarvis selected the Mission path but did not return a mission id. Upstream status: {response.UpstreamStatusCode?.ToString() ?? "unknown"}.";
            UpdateTranscript(pendingMessage, failure);
            if (IsWorkThreadMemoryEnabled())
            {
                await PersistTranscriptMessageAsync("assistant", failure, contextReceipt?.Id);
            }
            AddTimeline(failure);
            return;
        }

        var missionId = response.MissionId;
        UpdateTranscript(
            pendingMessage,
            $"Mission started: {missionId}\n\n{response.Reason}\n\nI’ll keep this conversation entry updated as the worker and critic finish the job.");
        AddTimeline($"Jarvis mission {missionId} started.");

        _ = MonitorJarvisMissionAsync(missionId, pendingMessage, contextReceipt);
    }

    private async Task<bool> ConfirmJarvisMissionAsync(AgentAskResponseDto response)
    {
        var details = response.MissionPayload?.ToString() ?? "Jarvis classified this request as potentially destructive.";
        if (details.Length > 800) details = details[..800] + "…";

        var dialog = new ContentDialog
        {
            XamlRoot = RootShell.XamlRoot,
            Title = "Approve Jarvis action?",
            Content = $"Jarvis requires explicit confirmation before starting this Mission.\n\n{details}",
            PrimaryButtonText = "Approve mission",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task MonitorJarvisMissionAsync(
        string missionId,
        TranscriptMessage pendingMessage,
        ContextReceiptDto? contextReceipt)
    {
        try
        {
            string? lastState = null;
            for (var attempt = 0; attempt < 1800; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                var detail = await _agentClient.GetMissionAsync(missionId);
                var state = detail.Mission.State ?? "UNKNOWN";

                if (!string.Equals(state, lastState, StringComparison.OrdinalIgnoreCase))
                {
                    lastState = state;
                    UpdateTranscript(
                        pendingMessage,
                        $"Mission {missionId}\nState: {state}\nIteration: {detail.Mission.Iteration}\nCost: ${detail.Mission.CostUsd:0.0000}\n\nJarvis workers are executing and the critic will review the result before completion.");
                    AddTimeline($"Jarvis mission {missionId}: {state}.");
                }

                if (!IsTerminalMissionState(state)) continue;

                var result = await _agentClient.GetMissionResultAsync(missionId);
                var completion = BuildMissionCompletion(result);
                UpdateTranscript(pendingMessage, completion);
                if (IsWorkThreadMemoryEnabled())
                {
                    await PersistTranscriptMessageAsync("assistant", completion, contextReceipt?.Id);
                }
                AddTimeline($"Jarvis mission {missionId} completed with state {state}.");
                return;
            }

            var timeoutText = $"Mission {missionId} is still running after the local monitoring window. It remains available through Jarvis Mission history.";
            UpdateTranscript(pendingMessage, timeoutText);
            AddTimeline(timeoutText);
        }
        catch (Exception ex)
        {
            var errorText = $"Mission {missionId} monitoring stopped: {ex.Message}\n\nThe Mission may still be running in Jarvis and can be recovered from Mission history.";
            UpdateTranscript(pendingMessage, errorText);
            AddTimeline($"Jarvis mission monitoring error: {ex.Message}");
        }
    }

    private static bool IsTerminalMissionState(string state) =>
        state.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)
        || state.Equals("FAILED", StringComparison.OrdinalIgnoreCase)
        || state.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)
        || state.Equals("TIMED_OUT", StringComparison.OrdinalIgnoreCase);

    private static string BuildMissionCompletion(JarvisMissionResultDto result)
    {
        var builder = new StringBuilder();
        builder.Append("Mission ").Append(result.MissionId).AppendLine();
        builder.Append("State: ").AppendLine(result.State);

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            builder.AppendLine().AppendLine(result.Summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.Reason))
        {
            builder.AppendLine().Append("Reason: ").AppendLine(result.Reason.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.ResultUri))
        {
            builder.Append("Result: ").AppendLine(result.ResultUri.Trim());
        }

        if (result.ArtifactCount > 0)
        {
            builder.Append("Artifacts: ").AppendLine(result.ArtifactCount.ToString());
        }

        if (result.Truncated)
        {
            builder.AppendLine("Artifact preview was truncated; the complete output remains in Jarvis Artifacts.");
        }

        return builder.ToString().Trim();
    }
}
