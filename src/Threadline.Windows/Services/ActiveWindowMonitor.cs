using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Threadline.Windows.Services;

public sealed record ActiveWindowSnapshot(
    nint Handle,
    string? WindowTitle,
    string? ProcessName,
    int? ProcessId,
    string? ExecutablePath,
    DateTimeOffset CapturedAt)
{
    public string ApplicationName => string.IsNullOrWhiteSpace(ProcessName) ? "Unknown" : ProcessName;

    public string ToDisplayText() =>
        $"Window: {WindowTitle ?? "Unknown"}\nProcess: {ProcessName ?? "Unknown"}\nPID: {ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}\nCaptured: {CapturedAt:t}";

    public override string ToString() => $"{ApplicationName} — {WindowTitle ?? "Untitled"}";
}

public sealed class ActiveWindowMonitor
{
    public ActiveWindowSnapshot GetActiveWindowSnapshot()
    {
        var handle = GetForegroundWindow();
        var currentProcessId = Environment.ProcessId;
        var process = GetProcess(handle);
        if (process?.Id == currentProcessId)
        {
            handle = FindNextVisibleAppWindow(currentProcessId) ?? handle;
            process = GetProcess(handle);
        }

        var title = GetWindowTitle(handle);
        return new ActiveWindowSnapshot(
            handle,
            title,
            process?.ProcessName,
            process?.Id,
            TryGetExecutablePath(process),
            DateTimeOffset.Now);
    }

    private static nint? FindNextVisibleAppWindow(int currentProcessId)
    {
        nint? candidate = null;
        EnumWindows((handle, _) =>
        {
            if (candidate is not null) return false;
            if (!IsWindowVisible(handle)) return true;
            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title)) return true;
            var process = GetProcess(handle);
            if (process is null || process.Id == currentProcessId) return true;
            candidate = handle;
            return false;
        }, nint.Zero);

        return candidate;
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

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
}
