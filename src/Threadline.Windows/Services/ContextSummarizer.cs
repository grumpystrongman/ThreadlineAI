using System.Text.RegularExpressions;

namespace Threadline.Windows.Services;

public sealed record SummarizedContext(
    string Title,
    string Source,
    string Summary,
    IReadOnlyList<string> KeyDetails,
    IReadOnlyList<string> Warnings,
    string RawPreview)
{
    public string ToPromptContext()
    {
        var details = KeyDetails.Count == 0 ? "None detected." : string.Join(Environment.NewLine, KeyDetails.Select(detail => $"- {detail}"));
        var warnings = Warnings.Count == 0 ? "None." : string.Join(Environment.NewLine, Warnings.Select(warning => $"- {warning}"));
        return $"Context summary: {Title}\nSource: {Source}\n\nSummary:\n{Summary}\n\nKey details:\n{details}\n\nWarnings:\n{warnings}";
    }
}

public sealed class ContextSummarizer
{
    private static readonly Regex ControlPrefix = new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled);
    private static readonly Regex TabLine = new(@"\b(tab|document|page)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] NoiseTerms =
    [
        "minimize", "maximize", "close", "system", "application", "non client", "input sink", "vertical", "horizontal",
        "context help", "title bar", "menu bar", "scroll bar", "restore", "ribbon", "status bar", "toolbar"
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
                result.ToDisplayText());
        }

        var cleanedLines = CleanLines(result.Content).ToList();
        var title = string.IsNullOrWhiteSpace(result.WindowTitle) ? result.ProcessName : result.WindowTitle;
        var warnings = result.Warnings.ToList();
        var ambiguous = IsAmbiguousTabbedCapture(result, cleanedLines);
        if (ambiguous)
        {
            warnings.Add("This native capture appears to include multiple tabs or document surfaces. Threadline can identify the active window/tab title, but the body text may belong to another tab. Use selected text, clipboard capture, or an app-specific adapter for reliable tab content.");
        }

        var keyDetails = ambiguous
            ? cleanedLines.Where(line => LooksLikeTitleOrTab(line, title)).Take(8).ToList()
            : cleanedLines.Take(12).ToList();
        var summary = BuildSummary(result, cleanedLines, ambiguous);
        if (cleanedLines.Count == 0)
        {
            warnings.Add("Native UI capture contained only window chrome or low-value controls after cleanup.");
        }

        return new SummarizedContext(
            title,
            $"{result.ProcessName} native UI",
            summary,
            keyDetails,
            warnings,
            result.ToDisplayText());
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
        if (!string.Equals(result.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase) &&
            !cleanedLines.Any(line => TabLine.IsMatch(line)))
        {
            return false;
        }

        var tabLikeLines = cleanedLines.Count(line => TabLine.IsMatch(line) || line.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (tabLikeLines >= 2) return true;
        if (cleanedLines.Count > 20 && string.Equals(result.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool LooksLikeTitleOrTab(string line, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title) && title.Contains(line, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(title) && line.Contains(title, StringComparison.OrdinalIgnoreCase)) return true;
        return TabLine.IsMatch(line) || line.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
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
            return $"Threadline identified the active window/tab as '{title}', but the native UI capture appears to include multiple tabs or document bodies. To avoid summarizing the wrong tab, Threadline is not treating the captured body text as authoritative.";
        }

        var joined = string.Join(" ", cleanedLines.Take(8));
        if (joined.Length > 900)
        {
            joined = joined[..900].TrimEnd() + "...";
        }

        return $"Threadline captured readable content from '{title}'. The useful visible content appears to focus on: {joined}";
    }
}
