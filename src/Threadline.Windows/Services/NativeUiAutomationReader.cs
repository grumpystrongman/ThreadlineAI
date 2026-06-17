using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Threadline.Windows.Services;

public sealed record NativeUiAutomationResult(
    bool Success,
    string WindowTitle,
    string ProcessName,
    int ProcessId,
    string Content,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<string> Warnings)
{
    public string ToDisplayText() =>
        Success
            ? $"UI Automation context from {ProcessName} ({ProcessId})\nWindow: {WindowTitle}\n\n{Content}"
            : $"UI Automation capture failed for {ProcessName} ({ProcessId})\nWindow: {WindowTitle}\n\n{string.Join(Environment.NewLine, Warnings)}";
}

public sealed class NativeUiAutomationReader
{
    private const int MaxItems = 350;
    private const int MaxCharacters = 16000;

    public NativeUiAutomationResult ReadForegroundWindow()
    {
        var warnings = new List<string>();
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return Failure("Unknown", "unknown", 0, warnings, "No foreground window handle was available.");
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        var processName = TryGetProcessName(processId);
        var windowTitle = ReadWindowTitle(handle);

        try
        {
            var root = AutomationElement.FromHandle(handle);
            if (root is null)
            {
                return Failure(windowTitle, processName, (int)processId, warnings, "UI Automation could not access this window.");
            }

            var content = ReadAutomationText(root, warnings);
            if (string.IsNullOrWhiteSpace(content))
            {
                return Failure(windowTitle, processName, (int)processId, warnings, "No readable UI Automation text was found in this window.");
            }

            return new NativeUiAutomationResult(
                true,
                windowTitle,
                processName,
                (int)processId,
                content,
                new Dictionary<string, string>
                {
                    ["adapter"] = "Windows UI Automation",
                    ["captureKind"] = "native-ui-automation",
                    ["processId"] = processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["processName"] = processName,
                    ["windowTitle"] = windowTitle
                },
                warnings);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            return Failure(windowTitle, processName, (int)processId, warnings, ex.Message);
        }
    }

    private static string ReadAutomationText(AutomationElement root, List<string> warnings)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var walker = TreeWalker.ControlViewWalker;
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);

        while (queue.Count > 0 && lines.Count < MaxItems && lines.Sum(line => line.Length) < MaxCharacters)
        {
            var element = queue.Dequeue();
            TryAddElementText(element, lines, seen);

            AutomationElement? child = null;
            try
            {
                child = walker.GetFirstChild(element);
            }
            catch (ElementNotAvailableException)
            {
                warnings.Add("A UI Automation element disappeared during capture.");
            }

            while (child is not null && queue.Count < MaxItems)
            {
                queue.Enqueue(child);
                try
                {
                    child = walker.GetNextSibling(child);
                }
                catch (ElementNotAvailableException)
                {
                    warnings.Add("A UI Automation sibling element disappeared during capture.");
                    break;
                }
            }
        }

        if (lines.Count >= MaxItems)
        {
            warnings.Add($"UI Automation capture was capped at {MaxItems} elements.");
        }

        var content = string.Join(Environment.NewLine, lines);
        return content.Length > MaxCharacters ? content[..MaxCharacters] : content;
    }

    private static void TryAddElementText(AutomationElement element, List<string> lines, HashSet<string> seen)
    {
        try
        {
            var name = Normalize(element.Current.Name);
            var controlType = element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty, StringComparison.Ordinal) ?? "Unknown";
            var value = TryGetValue(element);
            var text = string.IsNullOrWhiteSpace(value) || string.Equals(name, value, StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{name}: {value}";

            if (string.IsNullOrWhiteSpace(text)) return;
            var line = $"[{controlType}] {text}";
            if (seen.Add(line)) lines.Add(line);
        }
        catch (ElementNotAvailableException)
        {
            // Ignore volatile UI elements.
        }
    }

    private static string TryGetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
            {
                return Normalize(valuePattern.Current.Value);
            }
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static NativeUiAutomationResult Failure(string windowTitle, string processName, int processId, List<string> warnings, string reason)
    {
        warnings.Add(reason);
        return new NativeUiAutomationResult(false, windowTitle, processName, processId, string.Empty, new Dictionary<string, string>(), warnings);
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.ReplaceLineEndings(" ").Trim();

    private static string ReadWindowTitle(IntPtr handle)
    {
        var builder = new StringBuilder(512);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
}
