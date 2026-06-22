using System.Text;
using System.Text.RegularExpressions;

namespace Threadline.Windows.Services;

public enum NativeWindowContextLevel
{
    FullDocument,
    VisibleDocument,
    SelectedText,
    ReadableUiTree,
    TitleOnly,
    NoReadableContext
}

public sealed record NativeWindowContextCapture(
    string ProviderKey,
    string ProviderName,
    NativeWindowContextLevel Level,
    string Content,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<string> Warnings,
    string Guidance)
{
    public string LevelDisplayName => Level switch
    {
        NativeWindowContextLevel.FullDocument => "Full document",
        NativeWindowContextLevel.VisibleDocument => "Visible document",
        NativeWindowContextLevel.SelectedText => "Selected text",
        NativeWindowContextLevel.ReadableUiTree => "Readable UI tree",
        NativeWindowContextLevel.TitleOnly => "Title only",
        _ => "No readable context"
    };

    public bool HasDocumentText => Level is NativeWindowContextLevel.FullDocument or NativeWindowContextLevel.VisibleDocument or NativeWindowContextLevel.SelectedText or NativeWindowContextLevel.ReadableUiTree;

    public IReadOnlyDictionary<string, string> ToWindowMetadata()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nativeContext.providerKey"] = ProviderKey,
            ["nativeContext.providerName"] = ProviderName,
            ["nativeContext.level"] = Level.ToString(),
            ["nativeContext.levelDisplay"] = LevelDisplayName,
            ["nativeContext.hasDocumentText"] = HasDocumentText.ToString(),
            ["nativeContext.guidance"] = Guidance
        };

        if (!string.IsNullOrWhiteSpace(Content))
        {
            result["nativeContext.content"] = Content;
        }

        if (Warnings.Count > 0)
        {
            result["nativeContext.warnings"] = string.Join(" | ", Warnings);
        }

        foreach (var pair in Metadata)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                result[$"nativeContext.{pair.Key}"] = pair.Value;
            }
        }

        return result;
    }
}

public sealed class NativeWindowContextProvider
{
    private const int MaxContextCharacters = 16000;
    private static readonly Regex WindowsPathPattern = new(@"[A-Za-z]:\\[^:*?""<>|\r\n]+", RegexOptions.Compiled);
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();

    public NativeWindowContextCapture Capture(ActiveWindowSnapshot window)
    {
        var process = window.ProcessName ?? string.Empty;
        var title = window.WindowTitle ?? string.Empty;

        if (IsNotepad(process, title)) return CaptureNotepad(window);
        if (IsVsCode(process, title)) return CaptureVsCode(window);
        if (IsTerminal(process, title)) return CaptureTerminal(window);
        if (IsOffice(process, title)) return CaptureOffice(window);
        if (IsPdfReader(process, title)) return CapturePdf(window);
        if (IsDashboard(process, title) || IsBrowser(process)) return CaptureBrowserOrDashboard(window);

        return CaptureGeneric(window);
    }

    private NativeWindowContextCapture CaptureNotepad(ActiveWindowSnapshot window)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appFamily"] = "notepad"
        };

        var filePath = TryExtractExistingPath(window.WindowTitle, ".txt", ".md", ".log", ".json", ".xml", ".csv", ".sql", ".cs", ".ps1", ".py", ".yaml", ".yml");
        if (!string.IsNullOrWhiteSpace(filePath) && TryReadTextFile(filePath, out var fileText, out var fileWarning))
        {
            metadata["filePath"] = filePath;
            metadata["fileName"] = Path.GetFileName(filePath);
            if (!string.IsNullOrWhiteSpace(fileWarning)) metadata["readWarning"] = fileWarning;

            return Capture(
                "notepad-file",
                "Notepad / file-backed text",
                NativeWindowContextLevel.FullDocument,
                fileText,
                metadata,
                fileWarning is null ? [] : [fileWarning],
                "A file path was resolved from the Notepad window title and Threadline read the text file directly.");
        }

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success)
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "notepad-visible",
                "Notepad / visible text",
                NativeWindowContextLevel.VisibleDocument,
                native.Content,
                metadata,
                native.Warnings,
                "Threadline could not prove a backing file path, so it captured visible Notepad text through Windows accessibility.");
        }

        return TitleOnly(
            window,
            "notepad-title",
            "Notepad / title only",
            metadata,
            native.Warnings.Append("Notepad body text was not readable through native accessibility.").ToArray(),
            "Notepad was detected, but only the title was readable. Save the file or expose text selection to improve context.");
    }

    private NativeWindowContextCapture CaptureVsCode(ActiveWindowSnapshot window)
    {
        var metadata = ParseVsCodeTitle(window.WindowTitle);
        metadata["appFamily"] = "ide";

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success)
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "vscode-visible",
                "VS Code / IDE visible context",
                NativeWindowContextLevel.ReadableUiTree,
                native.Content,
                metadata,
                native.Warnings,
                "VS Code active file/workspace were inferred from the title when possible. Editor body text is captured only when Windows accessibility exposes it.");
        }

        return TitleOnly(
            window,
            "vscode-title",
            "VS Code / IDE title",
            metadata,
            native.Warnings,
            "VS Code was detected, but the editor surface was not readable through native accessibility. Title/workspace signals are still attached.");
    }

    private NativeWindowContextCapture CaptureTerminal(ActiveWindowSnapshot window)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appFamily"] = "terminal"
        };

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success)
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "terminal-visible",
                "Terminal / PowerShell visible text",
                NativeWindowContextLevel.VisibleDocument,
                native.Content,
                metadata,
                native.Warnings,
                "Terminal output is treated as visible context, not full shell history. Scrollback may be incomplete.");
        }

        return TitleOnly(
            window,
            "terminal-title",
            "Terminal / PowerShell title",
            metadata,
            native.Warnings,
            "Terminal was detected, but visible output was not readable through native accessibility.");
    }

    private NativeWindowContextCapture CaptureOffice(ActiveWindowSnapshot window)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appFamily"] = "office",
            ["safeOfficeMode"] = "title-uia-no-com"
        };

        var titlePath = TryExtractExistingPath(window.WindowTitle, ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".csv");
        if (!string.IsNullOrWhiteSpace(titlePath))
        {
            metadata["filePath"] = titlePath;
            metadata["fileName"] = Path.GetFileName(titlePath);
        }

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success)
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "office-visible",
                "Office document / safe visible context",
                NativeWindowContextLevel.ReadableUiTree,
                native.Content,
                metadata,
                native.Warnings.Append("Office COM automation was not invoked by native capture.").ToArray(),
                "Office context is captured through safe title/path/UIA signals only. Full COM extraction should remain opt-in and app-specific.");
        }

        return TitleOnly(
            window,
            "office-title",
            "Office document / title only",
            metadata,
            native.Warnings.Append("Office COM automation was not invoked by native capture.").ToArray(),
            "Office was detected, but only the title/path was readable. Native capture intentionally avoids unsafe automatic COM automation.");
    }

    private NativeWindowContextCapture CapturePdf(ActiveWindowSnapshot window)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appFamily"] = "pdf",
            ["ocrFallbackAvailable"] = "false"
        };

        var titlePath = TryExtractExistingPath(window.WindowTitle, ".pdf");
        if (!string.IsNullOrWhiteSpace(titlePath))
        {
            metadata["filePath"] = titlePath;
            metadata["fileName"] = Path.GetFileName(titlePath);
        }

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success)
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "pdf-visible",
                "PDF reader / visible text",
                NativeWindowContextLevel.VisibleDocument,
                native.Content,
                metadata,
                native.Warnings.Append("OCR/vision fallback is not wired into native window capture yet.").ToArray(),
                "PDF title/path and any accessible visible text were captured. OCR/vision fallback should be added later as an explicit, user-approved step.");
        }

        return TitleOnly(
            window,
            "pdf-title",
            "PDF reader / title only",
            metadata,
            native.Warnings.Append("OCR/vision fallback is not wired into native window capture yet.").ToArray(),
            "PDF reader was detected, but only title/path was readable. OCR/vision fallback is intentionally reported as unavailable for this build.");
    }

    private NativeWindowContextCapture CaptureBrowserOrDashboard(ActiveWindowSnapshot window)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appFamily"] = IsDashboard(window.ProcessName ?? string.Empty, window.WindowTitle ?? string.Empty) ? "dashboard" : "browser",
            ["preferredProvider"] = "browser-extension"
        };

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success && IsDashboard(window.ProcessName ?? string.Empty, window.WindowTitle ?? string.Empty))
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "dashboard-native-fallback",
                "Dashboard / browser extension preferred",
                NativeWindowContextLevel.ReadableUiTree,
                native.Content,
                metadata,
                native.Warnings.Append("Browser dashboard capture should prefer the Threadline browser extension.").ToArray(),
                "Power BI, Tableau, and browser dashboards should use extension context first. Native UI is only a fallback and may mostly capture chrome or labels.");
        }

        return TitleOnly(
            window,
            "browser-extension-first",
            "Browser / extension first",
            metadata,
            native.Warnings.Append("Browser page context is intentionally delegated to the Threadline browser extension.").ToArray(),
            "Browser windows should use extension context first. Native capture only supplies title-level fallback here.");
    }

    private NativeWindowContextCapture CaptureGeneric(ActiveWindowSnapshot window)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appFamily"] = "generic-win32"
        };

        var native = _nativeUiAutomationReader.ReadWindow(window.Handle);
        if (native.Success)
        {
            MergeNativeMetadata(metadata, native);
            return Capture(
                "generic-uia",
                "Generic Win32/UIA provider",
                NativeWindowContextLevel.ReadableUiTree,
                native.Content,
                metadata,
                native.Warnings,
                "Generic Windows accessibility captured readable controls. This may be noisy and should not be treated as a full document.");
        }

        return TitleOnly(
            window,
            "generic-title",
            "Generic window / title only",
            metadata,
            native.Warnings,
            "No readable native UI tree was available, so Threadline only attached process/title metadata.");
    }

    private static NativeWindowContextCapture Capture(
        string providerKey,
        string providerName,
        NativeWindowContextLevel level,
        string content,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<string> warnings,
        string guidance) =>
        new(providerKey, providerName, level, Truncate(content), metadata, warnings, guidance);

    private static NativeWindowContextCapture TitleOnly(
        ActiveWindowSnapshot window,
        string providerKey,
        string providerName,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<string> warnings,
        string guidance)
    {
        var title = window.WindowTitle ?? string.Empty;
        var level = string.IsNullOrWhiteSpace(title) ? NativeWindowContextLevel.NoReadableContext : NativeWindowContextLevel.TitleOnly;
        var content = string.IsNullOrWhiteSpace(title) ? string.Empty : $"Window title: {title}";
        return Capture(providerKey, providerName, level, content, metadata, warnings, guidance);
    }

    private static void MergeNativeMetadata(Dictionary<string, string> metadata, NativeUiAutomationResult native)
    {
        metadata["nativeUi.processId"] = native.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["nativeUi.processName"] = native.ProcessName;
        metadata["nativeUi.windowTitle"] = native.WindowTitle;
        metadata["nativeUi.textLength"] = native.Content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> ParseVsCodeTitle(string? title)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(title)) return metadata;

        var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 1 && !parts[^1].Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
        {
            metadata["activeFile"] = parts[0];
        }
        else if (parts.Length >= 2)
        {
            metadata["activeFile"] = parts[0];
        }

        if (parts.Length >= 3)
        {
            metadata["workspace"] = parts[^2];
        }

        return metadata;
    }

    private static bool TryReadTextFile(string filePath, out string content, out string? warning)
    {
        content = string.Empty;
        warning = null;

        try
        {
            var builder = new StringBuilder();
            using var stream = File.OpenRead(filePath);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[4096];
            while (builder.Length < MaxContextCharacters)
            {
                var read = reader.Read(buffer, 0, Math.Min(buffer.Length, MaxContextCharacters - builder.Length));
                if (read <= 0) break;
                builder.Append(buffer, 0, read);
            }

            if (!reader.EndOfStream)
            {
                warning = $"File content was capped at {MaxContextCharacters} characters.";
            }

            content = builder.ToString();
            return !string.IsNullOrWhiteSpace(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warning = $"Could not read file-backed text: {ex.Message}";
            return false;
        }
    }

    private static string? TryExtractExistingPath(string? title, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        foreach (Match match in WindowsPathPattern.Matches(title))
        {
            var candidate = TrimTitleDecorations(match.Value);
            if (File.Exists(candidate) && extensions.Any(ext => candidate.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        var withoutKnownSuffix = title
            .Replace(" - Notepad", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" - Visual Studio Code", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        withoutKnownSuffix = TrimTitleDecorations(withoutKnownSuffix);
        if (Path.IsPathFullyQualified(withoutKnownSuffix) && File.Exists(withoutKnownSuffix) && extensions.Any(ext => withoutKnownSuffix.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return withoutKnownSuffix;
        }

        return null;
    }

    private static string TrimTitleDecorations(string value) => value.Trim().TrimStart('*').Trim().Trim('"');

    private static string Truncate(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var normalized = content.ReplaceLineEndings(Environment.NewLine).Trim();
        return normalized.Length <= MaxContextCharacters ? normalized : normalized[..MaxContextCharacters];
    }

    private static bool IsBrowser(string process) =>
        string.Equals(process, "chrome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "msedge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsNotepad(string process, string title) =>
        string.Equals(process, "notepad", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase);

    private static bool IsVsCode(string process, string title) =>
        string.Equals(process, "Code", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "Code - Insiders", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string process, string title) =>
        string.Equals(process, "WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "powershell", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "pwsh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "cmd", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase);

    private static bool IsOffice(string process, string title) =>
        string.Equals(process, "WINWORD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "EXCEL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "POWERPNT", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "MSACCESS", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" - Word", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" - Excel", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" - PowerPoint", StringComparison.OrdinalIgnoreCase);

    private static bool IsPdfReader(string process, string title) =>
        string.Equals(process, "AcroRd32", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "Acrobat", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(process, "AcroCEF", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsDashboard(string process, string title) =>
        title.Contains("Power BI", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Tableau", StringComparison.OrdinalIgnoreCase) ||
        process.Contains("PBIDesktop", StringComparison.OrdinalIgnoreCase) ||
        process.Contains("Tableau", StringComparison.OrdinalIgnoreCase);
}
