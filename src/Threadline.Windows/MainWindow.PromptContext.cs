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
            _attachment = await _client.AttachWindowAsync(_session!.Id, target.Window);
            _lastContextSummary = await _contentResolver.ResolveAsync(_session!.Id, target);
            UpdateCurrentContextPanel(_lastContextSummary);

            AddTimeline($"Ask using context: {target.Window.ApplicationName} — {target.Title}");
            AppendTranscript("Threadline Context", $"Using context from {target.Window.ApplicationName} — {target.Title}\nSource: {_lastContextSummary.Source}\nConfidence: {_lastContextSummary.Confidence.ToString().ToUpperInvariant()}");
            return BuildFullPromptContext(_lastContextSummary);
        }

        if (_lastContextSummary is not null)
        {
            AddTimeline($"Ask using previous context: {_lastContextSummary.Title}");
            UpdateCurrentContextPanel(_lastContextSummary);
            return BuildFullPromptContext(_lastContextSummary);
        }

        if (_lastNativeUiResult is { Success: true })
        {
            _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
            UpdateCurrentContextPanel(_lastContextSummary);
            AddTimeline($"Ask using native UI context: {_lastContextSummary.Title}");
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
        return trimmed[..maxLength].TrimEnd() + "\n...[truncated by Threadline]";
    }
}
