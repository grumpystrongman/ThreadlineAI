using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Threadline.Windows.Services;

public sealed class OpenWindowCatalog
{
    private static readonly Regex ControlPrefix = new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled);
    private static readonly string[] PreferredProcesses =
    [
        "notepad", "chrome", "msedge", "onenote", "code", "windowsterminal", "powershell", "pwsh", "cmd", "winword", "excel", "powerpnt"
    ];

    public IReadOnlyList<ActiveWindowSnapshot> ListOpenWindows()
    {
        var currentProcessId = Environment.ProcessId;
        var windows = new List<ActiveWindowSnapshot>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title)) return true;
            var process = GetProcess(handle);
            if (process is null || process.Id == currentProcessId) return true;
            var snapshot = new ActiveWindowSnapshot(handle, title, process.ProcessName, process.Id, TryGetExecutablePath(process), DateTimeOffset.Now);
            windows.Add(snapshot);
            if (string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase))
            {
                windows.AddRange(FindNotepadTabs(snapshot));
            }
            return true;
        }, nint.Zero);

        return windows
            .GroupBy(window => $"{window.Handle}:{window.WindowTitle}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(window => GetPriority(window.ProcessName))
            .ThenBy(window => window.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<ActiveWindowSnapshot> FindNotepadTabs(ActiveWindowSnapshot snapshot)
    {
        var reader = new NativeUiAutomationReader();
        var result = reader.ReadWindow(snapshot.Handle);
        if (!result.Success) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in result.Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = ControlPrefix.Replace(rawLine, string.Empty).Trim();
            var tabTitle = NormalizeNotepadTabTitle(line);
            if (string.IsNullOrWhiteSpace(tabTitle)) continue;
            if (!seen.Add(tabTitle)) continue;

            yield return new ActiveWindowSnapshot(
                snapshot.Handle,
                tabTitle,
                snapshot.ProcessName,
                snapshot.ProcessId,
                snapshot.ExecutablePath,
                DateTimeOffset.Now);
        }
    }

    private static string? NormalizeNotepadTabTitle(string line)
    {
        if (line.EndsWith(" - Notepad", StringComparison.OrdinalIgnoreCase)) return line;
        if (line.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return line + " - Notepad";
        return null;
    }

    private static int GetPriority(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return 100;
        var index = Array.FindIndex(PreferredProcesses, process => string.Equals(process, processName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 100 : index;
    }

    private static string? GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0) return null;
        var builder = new StringBuilder(length + 1);
        return GetWindowText(handle, builder, builder.Capacity) > 0 ? builder.ToString() : null;
    }

    private static Process? GetProcess(nint handle)
    {
        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return null;
        try { return Process.GetProcessById((int)processId); } catch { return null; }
    }

    private static string? TryGetExecutablePath(Process? process)
    {
        if (process is null) return null;
        try { return process.MainModule?.FileName; } catch { return null; }
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
