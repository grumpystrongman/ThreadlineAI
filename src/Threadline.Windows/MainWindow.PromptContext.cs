using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private async Task<string?> ResolveContextForAskAsync()
    {
        var target = GetBestContextTargetForAsk();
        if (target is not null)
        {
            _selectedThreadlineTarget = target;
            _selectedTargetWindow = target.Window;
            _lastForegroundWindow = target.Window;
            _lastFollowTarget = target;
            PlaceSidecarForTarget(target, "Ask context target attached.");
            _attachment = await _client.AttachWindowAsync(_session!.Id, target.Window);
            var consent = BuildContextCaptureConsent(target);
            _lastContextSummary = await _contentResolver.ResolveAsync(_session!.Id, target, consent);
            ResetScreenshotVisionOneTimeApproval(_lastContextSummary);
            UpdateCurrentContextPanel(_lastContextSummary);

            var contextStatus = BuildContextStatus(_lastContextSummary);
            AddTimeline($"Ask using context: {target.Window.ApplicationName} — {target.Title} ({contextStatus})");
            AppendTranscript("Threadline Context", BuildAskContextReceiptMessage(target, _lastContextSummary, contextStatus));
            return BuildFullPromptContext(_lastContextSummary);
        }

        if (_lastContextSummary is not null)
        {
            UpdateCurrentContextPanel(_lastContextSummary);
            AddTimeline($"Ask using previous context: {_lastContextSummary.Title} ({BuildContextStatus(_lastContextSummary)})");
            return BuildFullPromptContext(_lastContextSummary);
        }

        if (_lastNativeUiResult is { Success: true })
        {
            _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
            UpdateCurrentContextPanel(_lastContextSummary);
            AddTimeline($"Ask using native UI context: {_lastContextSummary.Title} ({BuildContextStatus(_lastContextSummary)})");
            return BuildFullPromptContext(_lastContextSummary);
        }

        return _attachment is not null ? FormatAttachment(_attachment) : _lastForegroundWindow?.ToDisplayText();
    }

    private ThreadlineTarget? GetBestContextTargetForAsk()
    {
        if (_isTargetLocked && _lockedFollowTarget is not null) return _lockedFollowTarget;
        if (_selectedThreadlineTarget is not null) return _selectedThreadlineTarget;
        if (_lastFollowTarget is not null) return _lastFollowTarget;
        return null;
    }

    private static string BuildAskContextReceiptMessage(ThreadlineTarget target, SummarizedContext context, string contextStatus)
    {
        var receipt = context.Receipt;
        if (receipt is null)
        {
            return $"Using context from {target.Window.ApplicationName} — {target.Title}\nStatus: {contextStatus}\nSource: {context.Source}\nConfidence: {context.Confidence}\nReceipt: not available";
        }

        return $"Using context from {target.Window.ApplicationName} — {target.Title}\nStatus: {contextStatus}\nSource: {context.Source}\nConfidence: {context.Confidence}\nReceipt: {receipt.CaptureKind} via {receipt.SourceUsed}\nMissing real content: {(receipt.MissingRealWorkingContent ? "Yes" : "No")}\n{receipt.UserMessage}";
    }

    private static string BuildFullPromptContext(SummarizedContext context)
    {
        var raw = TrimForPrompt(context.RawPreview, 12000);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return context.ToPromptContext();
        }

        return $"{context.ToPromptContext()}\n\nResolved context excerpt:\n{raw}";
    }

    private static string TrimForPrompt(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength) return trimmed;
        return trimmed.Substring(0, maxLength).TrimEnd() + "\n...[truncated by Threadline]";
    }
}
