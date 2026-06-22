using System.Text.RegularExpressions;

namespace Threadline.Windows.Services;

public sealed record SummarizedContext(
    string Title,
    string Source,
    string Summary,
    IReadOnlyList<string> KeyDetails,
    IReadOnlyList<string> Warnings,
    string RawPreview,
    ContextConfidence Confidence = ContextConfidence.None,
    ProcessIntelligence? Process = null,
    CaptureDiagnostics? Diagnostics = null,
    ContextReceipt? Receipt = null)
{
    public string ToPromptContext()
    {
        var details = KeyDetails.Count == 0 ? "None detected." : string.Join(Environment.NewLine, KeyDetails.Select(detail => $"- {detail}"));
        var warnings = Warnings.Count == 0 ? "None." : string.Join(Environment.NewLine, Warnings.Select(warning => $"- {warning}"));
        var process = Process is null ? "Process intelligence: not available." : Process.ToDisplayText();
        var receipt = Receipt is null ? "Context receipt: not available." : Receipt.ToPromptText();
        return $"Context summary: {Title}\nSource: {Source}\nConfidence: {Confidence}\n\nWhat Threadline can see:\n{process}\n\nSummary:\n{Summary}\n\nKey details:\n{details}\n\nWarnings:\n{warnings}\n\n{receipt}";
    }
}

public sealed class ContextSummarizer
{
    private static readonly Regex ControlPrefix = new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled);
    private static readonly Regex TabLine = new(@"\b(tab|document|page)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] NoiseTerms =
    [
        "minimize", "maximize", "close", "system", "application", "non client", "input sink", "vertical", "horizontal",
        "context help", "title bar", "menu bar", "scroll bar", "restore", "ribbon", "status bar", "toolbar", "ime"
    ];

    public SummarizedContext SummarizeNativeUi(NativeUiAutomationResult result)
    {
        if (!result.Success)
        {
            return new SummarizedContext(
                result.WindowTitle,
                $"{result.ProcessName} native UI",
                "Threadline could not read useful native UI content from the target window.",
                [],
                result.Warnings,
                result.ToDisplayText(),
                ContextConfidence.Low);
        }

        var cleanedLines = CleanLines(result.Content).ToList();
        var title = string.IsNullOrWhiteSpace(result.WindowTitle) ? result.ProcessName : result.WindowTitle;
        var warnings = result.Warnings.ToList();
        var ambiguous = IsAmbiguousTabbedCapture(result, cleanedLines);
        if (ambiguous)
        {
            warnings.Add("This native capture is tab-ambiguous. Threadline can identify the active window or tab title, but Windows exposed body text that may belong to another tab.");
        }

        var keyDetails = ambiguous
            ? [title]
            : cleanedLines.Take(12).ToList();
        var summary = BuildSummary(result, cleanedLines, ambiguous);
        if (cleanedLines.Count == 0)
        {
            warnings.Add("Native UI capture contained only window chrome or low-value controls after cleanup.");
        }

        var confidence = cleanedLines.Count == 0 || ambiguous ? ContextConfidence.Low : ContextConfidence.Medium;

        return new SummarizedContext(
            title,
            $"{result.ProcessName} native UI",
            summary,
            keyDetails,
            warnings,
            result.ToDisplayText(),
            confidence);
    }

    public SummarizedContext SummarizePlainText(string title, string source, string text)
    {
        var cleanedLines = text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keyDetails = cleanedLines.Take(12).ToList();
        var summaryText = string.Join(" ", cleanedLines.Take(10));
        if (summaryText.Length > 1200)
        {
            summaryText = summaryText.Substring(0, 1200).TrimEnd() + "...";
        }

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            summaryText = "The approved text source was empty.";
        }
        else
        {
            summaryText = $"Threadline captured approved text from {source}. The content appears to focus on: {summaryText}";
        }

        return new SummarizedContext(
            title,
            source,
            summaryText,
            keyDetails,
            [],
            text,
            string.IsNullOrWhiteSpace(text) ? ContextConfidence.None : ContextConfidence.High);
    }

    private static IEnumerable<string> CleanLines(string content)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = ControlPrefix.Replace(rawLine, string.Empty).Trim();
            if (line.Length < 3) continue;
            if (line.All(char.IsDigit)) continue;
            if (NoiseTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            if (!seen.Add(line)) continue;
            yield return line;
        }
    }

    private static bool IsAmbiguousTabbedCapture(NativeUiAutomationResult result, IReadOnlyList<string> cleanedLines)
    {
        if (string.Equals(result.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase)) return true;
        if (result.WindowTitle.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase)) return true;
        if (cleanedLines.Any(line => TabLine.IsMatch(line))) return true;
        return false;
    }

    private static string BuildSummary(NativeUiAutomationResult result, IReadOnlyList<string> cleanedLines, bool ambiguous)
    {
        var title = string.IsNullOrWhiteSpace(result.WindowTitle) ? "the target window" : result.WindowTitle;
        if (cleanedLines.Count == 0)
        {
            return $"Threadline captured the window '{title}', but the native UI source did not expose clear document text. This may be an app limitation or a low-confidence accessibility capture.";
        }

        if (ambiguous)
        {
            return $"Threadline identified the active window or tab as '{title}', but this app's native UI capture may contain content from other tabs or document bodies. Threadline is not summarizing the body text because it may be from the wrong tab.";
        }

        var joined = string.Join(" ", cleanedLines.Take(8));
        if (joined.Length > 900)
        {
            joined = joined.Substring(0, 900).TrimEnd() + "...";
        }

        return $"Threadline captured readable content from '{title}'. The useful visible content appears to focus on: {joined}";
    }
}
