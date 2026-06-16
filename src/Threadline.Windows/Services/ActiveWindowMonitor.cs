using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Threadline.Windows.Services;

public sealed record ActiveWindowSnapshot(nint Handle, string? WindowTitle, string? ProcessName, DateTimeOffset CapturedAt)
{
    public string ToDisplayText() => $"Window: {WindowTitle ?? "Unknown"}\nProcess: {ProcessName ?? "Unknown"}\nCaptured: {CapturedAt:t}";
}

public sealed class ActiveWindowMonitor
{
    public ActiveWindowSnapshot GetActiveWindowSnapshot()
    {
        var handle = GetForegroundWindow();
        var title = GetWindowTitle(handle);
        var processName = GetProcessName(handle);
        return new ActiveWindowSnapshot(handle, title, processName, DateTimeOffset.Now);
    }

    private static string? GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0) return null;
        var builder = new StringBuilder(length + 1);
        return GetWindowText(handle, builder, builder.Capacity) > 0 ? builder.ToString() : null;
    }

    private static string? GetProcessName(nint handle)
    {
        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return null;
        try { using var process = Process.GetProcessById((int)processId); return process.ProcessName; } catch { return null; }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
