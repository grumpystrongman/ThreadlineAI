using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Threadline.Windows.Services;

public sealed class OpenWindowCatalog
{
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
            windows.Add(new ActiveWindowSnapshot(handle, title, process.ProcessName, process.Id, TryGetExecutablePath(process), DateTimeOffset.Now));
            return true;
        }, nint.Zero);

        return windows
            .GroupBy(window => window.Handle)
            .Select(group => group.First())
            .OrderBy(window => GetPriority(window.ProcessName))
            .ThenBy(window => window.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
