namespace Threadline.Windows.Services;

public sealed record FileBackedTextResult(bool Success, string? Path, string Text, IReadOnlyList<string> Warnings);

public sealed class FileBackedTextResolver
{
    private const int MaxBytes = 512_000;
    private static readonly string[] KnownTitleSuffixes =
    [
        " - Notepad",
        " - Visual Studio Code",
        " - Word",
        " - Microsoft Word"
    ];

    public FileBackedTextResult TryResolve(string fileName)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new FileBackedTextResult(false, null, string.Empty, ["No file name was available."]);
        }

        var candidates = CandidateFileNames(fileName).ToList();
        var matches = candidates
            .SelectMany(CandidatePaths)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            return new FileBackedTextResult(false, null, string.Empty, [$"No exact saved-file match was found for: {string.Join(", ", candidates)}. Searched current folder, Documents, Desktop, and Downloads."]);
        }

        if (matches.Count > 1)
        {
            warnings.Add("Multiple exact saved-file matches were found; Threadline will not guess.");
            warnings.AddRange(matches.Select(path => $"Candidate: {path}"));
            return new FileBackedTextResult(false, null, string.Empty, warnings);
        }

        var path = matches[0];
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
            {
                return new FileBackedTextResult(false, path, string.Empty, [$"File is too large for direct preview: {info.Length} bytes."]);
            }

            return new FileBackedTextResult(true, path, File.ReadAllText(path), warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileBackedTextResult(false, path, string.Empty, [$"Could not read file: {ex.Message}"]);
        }
    }

    private static IEnumerable<string> CandidateFileNames(string titleOrFileName)
    {
        var trimmed = titleOrFileName.Trim();
        yield return trimmed;

        foreach (var suffix in KnownTitleSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stripped = trimmed[..^suffix.Length].Trim();
                if (!string.IsNullOrWhiteSpace(stripped)) yield return stripped;
            }
        }

        var separatorIndex = trimmed.LastIndexOf(" - ", StringComparison.OrdinalIgnoreCase);
        if (separatorIndex > 0)
        {
            var beforeSeparator = trimmed[..separatorIndex].Trim();
            if (!string.IsNullOrWhiteSpace(beforeSeparator)) yield return beforeSeparator;
        }
    }

    private static IEnumerable<string> CandidatePaths(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            yield return fileName;
            yield break;
        }

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var downloads = Path.Combine(user, "Downloads");

        yield return Path.Combine(Environment.CurrentDirectory, fileName);
        yield return Path.Combine(documents, fileName);
        yield return Path.Combine(desktop, fileName);
        yield return Path.Combine(downloads, fileName);
    }
}
