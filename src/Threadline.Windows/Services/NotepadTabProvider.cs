using System.Text.RegularExpressions;

namespace Threadline.Windows.Services;

public sealed class NotepadTabProvider : ITabProvider
{
    private static readonly Regex ControlPrefix = new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled);

    public bool CanInspect(ActiveWindowSnapshot window) =>
        string.Equals(window.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase) ||
        (window.WindowTitle?.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase) ?? false);

    public IReadOnlyList<ThreadlineTarget> GetTargets(ActiveWindowSnapshot window)
    {
        var targets = new List<ThreadlineTarget>();
        var reader = new NativeUiAutomationReader();
        var result = reader.ReadWindow(window.Handle);
        if (!result.Success) return targets;

        var activeTitle = window.WindowTitle ?? string.Empty;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in result.Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = ControlPrefix.Replace(rawLine, string.Empty).Trim();
            var title = NormalizeTabTitle(line);
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (!seen.Add(title)) continue;

            var isActive = !string.IsNullOrWhiteSpace(activeTitle) && activeTitle.Contains(title, StringComparison.OrdinalIgnoreCase);
            targets.Add(new ThreadlineTarget(
                $"notepad-tab:{window.Handle}:{title}",
                ThreadlineTargetKind.Tab,
                window,
                title,
                "notepad-tabs",
                isActive,
                false,
                "title-only",
                "Notepad tab title was detected, but Notepad body text is not safely mapped to this tab yet."));
        }

        return targets;
    }

    private static string? NormalizeTabTitle(string line)
    {
        if (line.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase))
        {
            return line[..^" - Notepad".Length].Trim();
        }

        if (line.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return line;
        if (line.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return line;
        if (line.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return line;
        return null;
    }
}
