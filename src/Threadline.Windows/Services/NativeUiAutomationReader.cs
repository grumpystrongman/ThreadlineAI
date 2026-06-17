using System.Runtime.InteropServices;
using System.Text;

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
            ? $"Native UI context from {ProcessName} ({ProcessId})\nWindow: {WindowTitle}\n\n{Content}"
            : $"Native UI capture failed for {ProcessName} ({ProcessId})\nWindow: {WindowTitle}\n\n{string.Join(Environment.NewLine, Warnings)}";
}

public sealed class NativeUiAutomationReader
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const int MaxItems = 350;
    private const int MaxCharacters = 16000;
    private static readonly Guid AccessibleInterfaceId = new("618736e0-3c3d-11cf-810c-00aa00389b71");

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
            var interfaceId = AccessibleInterfaceId;
            var hr = AccessibleObjectFromWindow(handle, ObjIdClient, ref interfaceId, out var accessibleObject);
            if (hr != 0 || accessibleObject is null)
            {
                return Failure(windowTitle, processName, (int)processId, warnings, $"Windows accessibility object was unavailable. HRESULT: 0x{hr:X8}");
            }

            var content = ReadAccessibleText(accessibleObject, warnings);
            if (string.IsNullOrWhiteSpace(content))
            {
                return Failure(windowTitle, processName, (int)processId, warnings, "No readable native accessibility text was found in this window.");
            }

            return new NativeUiAutomationResult(
                true,
                windowTitle,
                processName,
                (int)processId,
                content,
                new Dictionary<string, string>
                {
                    ["adapter"] = "Windows Native Accessibility",
                    ["captureKind"] = "native-ui-accessibility",
                    ["processId"] = processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["processName"] = processName,
                    ["windowTitle"] = windowTitle
                },
                warnings);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or RuntimeBinderException)
        {
            return Failure(windowTitle, processName, (int)processId, warnings, ex.Message);
        }
    }

    private static string ReadAccessibleText(object root, List<string> warnings)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<object>();
        queue.Enqueue(root);

        while (queue.Count > 0 && lines.Count < MaxItems && lines.Sum(line => line.Length) < MaxCharacters)
        {
            var item = queue.Dequeue();
            AddAccessibleText(item, 0, lines, seen);
            EnqueueChildren(item, queue, warnings);
        }

        if (lines.Count >= MaxItems)
        {
            warnings.Add($"Native UI capture was capped at {MaxItems} elements.");
        }

        var content = string.Join(Environment.NewLine, lines);
        return content.Length > MaxCharacters ? content[..MaxCharacters] : content;
    }

    private static void EnqueueChildren(object accessibleObject, Queue<object> queue, List<string> warnings)
    {
        try
        {
            dynamic accessible = accessibleObject;
            int childCount = accessible.accChildCount;
            if (childCount <= 0) return;

            var children = new object[childCount];
            var obtained = 0;
            var hr = AccessibleChildren(accessibleObject, 0, childCount, children, ref obtained);
            if (hr != 0)
            {
                warnings.Add($"Could not enumerate all native UI children. HRESULT: 0x{hr:X8}");
                return;
            }

            for (var index = 0; index < obtained && queue.Count < MaxItems; index++)
            {
                var child = children[index];
                if (child is null) continue;

                if (child is int childId)
                {
                    AddAccessibleText(accessibleObject, childId, queue: null, warnings: null);
                    continue;
                }

                queue.Enqueue(child);
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException)
        {
            warnings.Add("A native UI element could not be enumerated.");
        }
    }

    private static void AddAccessibleText(object accessibleObject, int childId, List<string>? queue, HashSet<string>? warnings)
    {
        // This overload exists only to keep child-id enumeration resilient. It intentionally no-ops.
    }

    private static void AddAccessibleText(object accessibleObject, int childId, List<string> lines, HashSet<string> seen)
    {
        try
        {
            dynamic accessible = accessibleObject;
            object child = childId == 0 ? 0 : childId;
            var name = Normalize((string?)accessible.accName[child]);
            var value = Normalize((string?)accessible.accValue[child]);
            var role = Normalize(Convert.ToString(accessible.accRole[child], System.Globalization.CultureInfo.InvariantCulture));
            var text = string.IsNullOrWhiteSpace(value) || string.Equals(name, value, StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{name}: {value}";

            if (string.IsNullOrWhiteSpace(text)) return;
            var line = string.IsNullOrWhiteSpace(role) ? text : $"[{role}] {text}";
            if (seen.Add(line)) lines.Add(line);
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException)
        {
            // Ignore volatile or inaccessible child elements.
        }
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

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid interfaceId, [MarshalAs(UnmanagedType.IDispatch)] out object? accessibleObject);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren([MarshalAs(UnmanagedType.IDispatch)] object accessibleObject, int childStart, int childCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, ref int obtainedCount);
}
