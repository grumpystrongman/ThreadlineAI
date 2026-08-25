using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _handledJarvisToolApprovalTraceIds = new(StringComparer.OrdinalIgnoreCase);

    private async Task HandlePendingJarvisToolApprovalsAsync(string missionId)
    {
        IReadOnlyList<JarvisToolApprovalDto> approvals;
        try
        {
            approvals = await _agentClient.GetMissionToolApprovalsAsync(missionId);
        }
        catch (Exception ex)
        {
            AddTimeline($"Jarvis tool-approval check failed: {ex.Message}");
            return;
        }

        foreach (var approval in approvals)
        {
            if (string.IsNullOrWhiteSpace(approval.TraceId) || !_handledJarvisToolApprovalTraceIds.Add(approval.TraceId)) continue;

            var argsPreview = string.IsNullOrWhiteSpace(approval.ArgsPreview) ? "No argument preview was supplied." : approval.ArgsPreview.Trim();
            if (argsPreview.Length > 1200) argsPreview = argsPreview[..1200] + "…";
            var protectedHint = approval.ToolName.StartsWith("threadline-privileged/", StringComparison.OrdinalIgnoreCase)
                || approval.ToolName.StartsWith("protected_", StringComparison.OrdinalIgnoreCase)
                || approval.RiskTier.Equals("ask", StringComparison.OrdinalIgnoreCase);

            var content = new StackPanel { Spacing = 8, MaxWidth = 560 };
            content.Children.Add(new TextBlock
            {
                Text = protectedHint
                    ? "Jarvis paused before a protected operation. Approval resumes this exact tool call; Windows may still show a UAC elevation prompt afterward."
                    : "Jarvis paused before this tool call and needs your decision.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock { Text = $"Tool: {approval.ToolName}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = $"Risk: {approval.RiskTier} · Reason: {approval.Reason}", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "Arguments preview:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = argsPreview, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            content.Children.Add(new TextBlock
            {
                Text = "Approval applies only to this paused trace ID. It does not expand Owner Authority or approve future protected actions.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            });

            var dialog = new ContentDialog
            {
                XamlRoot = RootShell.XamlRoot,
                Title = protectedHint ? "Approve protected Jarvis action?" : "Approve Jarvis tool call?",
                Content = content,
                PrimaryButtonText = "Approve this action",
                CloseButtonText = "Deny",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            try
            {
                if (result == ContentDialogResult.Primary)
                {
                    await _agentClient.ApproveMissionToolAsync(missionId, approval.TraceId);
                    AppendTranscript("AIKA / JARVIS", $"Approved tool action: {approval.ToolName}. Jarvis is resuming Mission {missionId}.");
                    AddTimeline($"Approved Jarvis tool {approval.ToolName} ({approval.TraceId}).");
                }
                else
                {
                    await _agentClient.DenyMissionToolAsync(missionId, approval.TraceId, "user_denied_in_aika");
                    AppendTranscript("AIKA / JARVIS", $"Denied tool action: {approval.ToolName}. Jarvis will receive the denial and must re-plan or stop that step.");
                    AddTimeline($"Denied Jarvis tool {approval.ToolName} ({approval.TraceId}).");
                }
            }
            catch (Exception ex)
            {
                AddTimeline($"Jarvis tool approval decision could not be submitted: {ex.Message}");
            }
        }
    }
}
