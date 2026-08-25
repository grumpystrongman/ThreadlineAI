using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Threadline.Core;

namespace Threadline.Windows.Services;

public sealed class WindowsDeviceAgent
{
    private readonly ActiveWindowMonitor _windowMonitor;
    private readonly NativeUiAutomationReader _reader;
    private readonly NativeUiAutomationController _uiaController;
    private readonly DeviceAuthorityEvaluator _authorityEvaluator;
    private DeviceAuthorityGrant _authorityGrant;

    public WindowsDeviceAgent(
        ActiveWindowMonitor? windowMonitor = null,
        NativeUiAutomationReader? reader = null,
        NativeUiAutomationController? uiaController = null,
        DeviceAuthorityGrant? authorityGrant = null)
    {
        _windowMonitor = windowMonitor ?? new ActiveWindowMonitor();
        _reader = reader ?? new NativeUiAutomationReader();
        _uiaController = uiaController ?? new NativeUiAutomationController();
        _authorityEvaluator = new DeviceAuthorityEvaluator();
        _authorityGrant = authorityGrant ?? DeviceAuthorityGrant.OwnerDefault;
    }

    public DeviceAuthorityGrant AuthorityGrant => _authorityGrant;

    public void SetAuthorityGrant(DeviceAuthorityGrant grant) =>
        _authorityGrant = grant ?? throw new ArgumentNullException(nameof(grant));

    public IReadOnlyList<DeviceCapability> GetCapabilities() =>
        new DeviceCapability[]
        {
            new(
                "windows.process-window",
                "Windows process and window control",
                new HashSet<DeviceOperationKind>
                {
                    DeviceOperationKind.ObserveDesktop,
                    DeviceOperationKind.ListApplications,
                    DeviceOperationKind.LaunchApplication,
                    DeviceOperationKind.FocusWindow
                },
                Priority: 100,
                RequiresInteractiveDesktop: true,
                "Native Win32 process/window discovery, launch, focus, and state verification."),
            new(
                "windows.uia",
                "Windows UI Automation",
                new HashSet<DeviceOperationKind>
                {
                    DeviceOperationKind.InvokeControl,
                    DeviceOperationKind.SetText
                },
                Priority: 90,
                RequiresInteractiveDesktop: true,
                "Native Windows UI Automation control lookup and Invoke/Value patterns."),
            new(
                "windows.input",
                "Windows input fallback",
                new HashSet<DeviceOperationKind>
                {
                    DeviceOperationKind.KeyboardInput,
                    DeviceOperationKind.MouseInput
                },
                Priority: 20,
                RequiresInteractiveDesktop: true,
                "SendInput fallback for applications that do not expose usable native automation patterns.")
        };

    public DeviceObservation ObserveDesktop() => CaptureObservation();

    public async Task<DeviceExecutionResult> ExecuteAsync(
        DeviceCommand command,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var authority = _authorityEvaluator.Evaluate(_authorityGrant, command, confirmed);
        var before = CaptureObservation();

        if (!authority.Allowed)
        {
            return new DeviceExecutionResult(
                command.Id,
                DeviceExecutionStatus.Blocked,
                authority.Reason,
                before,
                before,
                new DeviceVerificationResult(false, command.Verification.Kind, authority.Reason, DateTimeOffset.Now),
                Metadata: new Dictionary<string, string>
                {
                    ["requiresConfirmation"] = authority.RequiresConfirmation.ToString(CultureInfo.InvariantCulture),
                    ["riskTier"] = command.RiskTier.ToString()
                });
        }

        try
        {
            var capability = await ExecuteOperationAsync(command, cancellationToken);
            var verification = await VerifyAsync(command, cancellationToken);
            var after = CaptureObservation();
            var status = verification.Satisfied ? DeviceExecutionStatus.Completed : DeviceExecutionStatus.Partial;
            var message = verification.Satisfied
                ? $"{command.Operation} completed and the expected state was verified."
                : $"{command.Operation} executed, but the expected state could not be verified: {verification.Detail}";

            return new DeviceExecutionResult(
                command.Id,
                status,
                message,
                before,
                after,
                verification,
                capability,
                new Dictionary<string, string>
                {
                    ["authority"] = authority.Reason,
                    ["riskTier"] = command.RiskTier.ToString()
                });
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            var after = CaptureObservation();
            return new DeviceExecutionResult(
                command.Id,
                DeviceExecutionStatus.Failed,
                ex.Message,
                before,
                after,
                new DeviceVerificationResult(false, command.Verification.Kind, ex.Message, DateTimeOffset.Now));
        }
    }

    private async Task<string> ExecuteOperationAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        switch (command.Operation)
        {
            case DeviceOperationKind.ObserveDesktop:
            case DeviceOperationKind.ListApplications:
                return "windows.process-window";

            case DeviceOperationKind.LaunchApplication:
                await LaunchApplicationAsync(command.Target, cancellationToken);
                return "windows.process-window";

            case DeviceOperationKind.FocusWindow:
                FocusWindow(command.Target);
                return "windows.process-window";

            case DeviceOperationKind.InvokeControl:
                FocusWindowIfSpecified(command.Target);
                _uiaController.Invoke(command.Target);
                return "windows.uia";

            case DeviceOperationKind.SetText:
                FocusWindowIfSpecified(command.Target);
                var text = RequiredArgument(command, "text");
                _uiaController.SetValue(command.Target, text);
                return "windows.uia";

            case DeviceOperationKind.KeyboardInput:
                FocusWindowIfSpecified(command.Target);
                if (TryArgument(command, "hotkey", out var hotkey) && !string.IsNullOrWhiteSpace(hotkey))
                {
                    WindowsInput.SendHotkey(hotkey);
                }
                else
                {
                    WindowsInput.SendUnicodeText(RequiredArgument(command, "text"));
                }
                return "windows.input";

            case DeviceOperationKind.MouseInput:
                FocusWindowIfSpecified(command.Target);
                var x = RequiredIntArgument(command, "x");
                var y = RequiredIntArgument(command, "y");
                var button = TryArgument(command, "button", out var buttonValue) ? buttonValue : "left";
                WindowsInput.Click(x, y, button ?? "left");
                return "windows.input";

            default:
                throw new InvalidOperationException($"Operation {command.Operation} is not implemented by the interactive Windows Device Agent yet.");
        }
    }

    private static async Task LaunchApplicationAsync(DeviceTarget target, CancellationToken cancellationToken)
    {
        var requested = FirstNonEmpty(target.ExecutablePath, target.Application);
        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new ArgumentException("Application or executablePath is required for LaunchApplication.");
        }

        var launchTarget = ResolveLaunchTarget(requested);
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = launchTarget,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Could not launch '{requested}': {ex.Message}", ex);
        }

        if (process is null)
        {
            throw new InvalidOperationException($"Windows did not return a process for '{requested}'.");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                process.Refresh();
                if (!process.HasExited)
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            await Task.Delay(150, cancellationToken);
        }
    }

    private void FocusWindowIfSpecified(DeviceTarget target)
    {
        if (target.WindowHandle is not null || target.ProcessId is not null || !string.IsNullOrWhiteSpace(target.WindowTitle) || !string.IsNullOrWhiteSpace(target.Application))
        {
            FocusWindow(target);
        }
    }

    private static void FocusWindow(DeviceTarget target)
    {
        var handle = FindWindow(target);
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("No matching top-level window was found.");
        }

        _ = ShowWindow(handle, 9); // SW_RESTORE
        if (!SetForegroundWindow(handle))
        {
            throw new InvalidOperationException("Windows refused to move the requested window to the foreground.");
        }
    }

    private async Task<DeviceVerificationResult> VerifyAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        var expected = command.Verification;
        if (expected.Kind == DeviceVerificationKind.None)
        {
            return new DeviceVerificationResult(true, expected.Kind, "No explicit postcondition was requested.", DateTimeOffset.Now);
        }

        var timeout = Math.Clamp(expected.TimeoutMilliseconds, 100, 60000);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeout);
        DeviceVerificationResult last = new(false, expected.Kind, "Expected state has not been observed yet.", DateTimeOffset.Now);

        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = VerifyOnce(command.Target, expected);
            if (last.Satisfied)
            {
                return last;
            }

            await Task.Delay(200, cancellationToken);
        }

        return last with { Detail = $"Timed out after {timeout} ms. {last.Detail}", CheckedAt = DateTimeOffset.Now };
    }

    private DeviceVerificationResult VerifyOnce(DeviceTarget target, DeviceExpectedState expected)
    {
        switch (expected.Kind)
        {
            case DeviceVerificationKind.ProcessRunning:
            {
                var match = EnumerateWindows().FirstOrDefault(window => TargetMatches(window, target));
                return Verification(expected, match is not null, match is null ? "Matching process/window is not running." : $"Observed {match.ProcessName} ({match.ProcessId}).");
            }
            case DeviceVerificationKind.WindowExists:
            {
                var match = EnumerateWindows().FirstOrDefault(window => TargetMatches(window, target));
                return Verification(expected, match is not null, match is null ? "Matching window was not found." : $"Observed window '{match.Title}'.");
            }
            case DeviceVerificationKind.ForegroundWindowMatches:
            {
                var foreground = CaptureObservation().ForegroundWindow;
                var satisfied = foreground is not null && TargetMatches(foreground, target);
                return Verification(expected, satisfied, satisfied ? $"Foreground is '{foreground!.Title}'." : "Foreground window does not match the requested target.");
            }
            case DeviceVerificationKind.AccessibleTextContains:
            {
                var handle = FindWindow(target);
                if (handle == nint.Zero)
                {
                    return Verification(expected, false, "Target window was not found for accessibility verification.");
                }

                var result = _reader.ReadWindow(handle);
                var needle = expected.Value ?? string.Empty;
                var satisfied = result.Success && result.Content.Contains(needle, StringComparison.OrdinalIgnoreCase);
                return Verification(expected, satisfied, satisfied ? $"Accessible text contains '{needle}'." : $"Accessible text does not contain '{needle}'.");
            }
            case DeviceVerificationKind.FileExists:
            {
                var path = expected.Value ?? string.Empty;
                var satisfied = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                return Verification(expected, satisfied, satisfied ? $"File exists: {path}" : $"File does not exist: {path}");
            }
            default:
                return Verification(expected, true, "No verification required.");
        }
    }

    private DeviceObservation CaptureObservation()
    {
        var windows = EnumerateWindows();
        var foregroundHandle = GetForegroundWindow();
        var foreground = windows.FirstOrDefault(window => window.Handle == foregroundHandle.ToInt64());
        string? accessibleText = null;
        if (foregroundHandle != nint.Zero)
        {
            var native = _reader.ReadWindow(foregroundHandle);
            if (native.Success)
            {
                accessibleText = native.Content;
            }
        }

        return new DeviceObservation(
            DateTimeOffset.Now,
            foreground,
            windows,
            accessibleText,
            new Dictionary<string, string>
            {
                ["interactiveSession"] = Environment.UserInteractive.ToString(CultureInfo.InvariantCulture),
                ["sessionId"] = Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture),
                ["windowCount"] = windows.Count.ToString(CultureInfo.InvariantCulture)
            });
    }

    private static IReadOnlyList<DeviceWindowObservation> EnumerateWindows()
    {
        var foreground = GetForegroundWindow();
        var windows = new List<DeviceWindowObservation>();
        _ = EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            var title = ReadWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title)) return true;
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0) return true;

            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new DeviceWindowObservation(
                    handle.ToInt64(),
                    title,
                    process.ProcessName,
                    process.Id,
                    TryGetExecutablePath(process),
                    handle == foreground));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Window disappeared while enumerating.
            }

            return true;
        }, nint.Zero);
        return windows;
    }

    private static nint FindWindow(DeviceTarget target)
    {
        if (target.WindowHandle is long raw && raw != 0)
        {
            return new nint(raw);
        }

        nint match = nint.Zero;
        _ = EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            var title = ReadWindowTitle(handle);
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0) return true;

            try
            {
                using var process = Process.GetProcessById((int)processId);
                var observation = new DeviceWindowObservation(
                    handle.ToInt64(),
                    title,
                    process.ProcessName,
                    process.Id,
                    TryGetExecutablePath(process),
                    false);
                if (!TargetMatches(observation, target)) return true;
                match = handle;
                return false;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return true;
            }
        }, nint.Zero);
        return match;
    }

    private static bool TargetMatches(DeviceWindowObservation window, DeviceTarget target)
    {
        if (target.ProcessId is int processId && window.ProcessId != processId) return false;
        if (!string.IsNullOrWhiteSpace(target.WindowTitle) && !window.Title.Contains(target.WindowTitle, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(target.Application))
        {
            var app = NormalizeAppName(target.Application);
            var process = NormalizeAppName(window.ProcessName);
            var title = NormalizeAppName(window.Title);
            if (!process.Contains(app, StringComparison.OrdinalIgnoreCase) && !title.Contains(app, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(target.ExecutablePath) && !string.Equals(window.ExecutablePath, target.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
        return target.ProcessId is not null || target.WindowHandle is not null || !string.IsNullOrWhiteSpace(target.WindowTitle) || !string.IsNullOrWhiteSpace(target.Application) || !string.IsNullOrWhiteSpace(target.ExecutablePath);
    }

    private static string ResolveLaunchTarget(string requested)
    {
        var expanded = Environment.ExpandEnvironmentVariables(requested.Trim().Trim('"'));
        if (File.Exists(expanded)) return expanded;

        var startMenuRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        };
        var normalized = NormalizeAppName(Path.GetFileNameWithoutExtension(expanded));
        var candidates = new List<string>();
        foreach (var root in startMenuRoots.Where(Directory.Exists))
        {
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories));
            }
            catch (UnauthorizedAccessException)
            {
                // Skip protected Start Menu folders and continue with shell resolution.
            }
        }

        var shortcut = candidates
            .Select(path => new { Path = path, Name = NormalizeAppName(Path.GetFileNameWithoutExtension(path)) })
            .OrderBy(item => string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Name.Length)
            .FirstOrDefault(item => item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) || normalized.Contains(item.Name, StringComparison.OrdinalIgnoreCase));

        return shortcut?.Path ?? expanded;
    }

    private static string NormalizeAppName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? TryGetExecutablePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { return null; }
    }

    private static string ReadWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0) return string.Empty;
        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static DeviceVerificationResult Verification(DeviceExpectedState expected, bool satisfied, string detail) =>
        new(satisfied, expected.Kind, detail, DateTimeOffset.Now);

    private static string RequiredArgument(DeviceCommand command, string key) =>
        TryArgument(command, key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value!
            : throw new ArgumentException($"Argument '{key}' is required for {command.Operation}.");

    private static int RequiredIntArgument(DeviceCommand command, string key) =>
        int.TryParse(RequiredArgument(command, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"Argument '{key}' must be an integer.");

    private static bool TryArgument(DeviceCommand command, string key, out string? value)
    {
        value = null;
        return command.Arguments is not null && command.Arguments.TryGetValue(key, out value);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}

public sealed class NativeUiAutomationController
{
    private static readonly Guid CUiAutomationClassId = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private const int TreeScopeDescendants = 4;
    private const int UiaNamePropertyId = 30005;
    private const int UiaAutomationIdPropertyId = 30011;
    private const int UiaInvokePatternId = 10000;
    private const int UiaValuePatternId = 10002;

    public void Invoke(DeviceTarget target)
    {
        dynamic element = FindElement(target);
        dynamic pattern = element.GetCurrentPattern(UiaInvokePatternId);
        pattern.Invoke();
    }

    public void SetValue(DeviceTarget target, string value)
    {
        dynamic element = FindElement(target);
        dynamic pattern = element.GetCurrentPattern(UiaValuePatternId);
        pattern.SetValue(value);
    }

    private static dynamic FindElement(DeviceTarget target)
    {
        var handle = FindTargetWindow(target);
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("No matching target window was found for UI Automation.");
        }

        var automationType = Type.GetTypeFromCLSID(CUiAutomationClassId, throwOnError: true)!;
        dynamic automation = Activator.CreateInstance(automationType) ?? throw new InvalidOperationException("Windows UI Automation could not be created.");
        dynamic root = automation.ElementFromHandle(handle);

        if (!string.IsNullOrWhiteSpace(target.AutomationId))
        {
            dynamic condition = automation.CreatePropertyCondition(UiaAutomationIdPropertyId, target.AutomationId);
            dynamic element = root.FindFirst(TreeScopeDescendants, condition);
            if (element is not null) return element;
        }

        if (!string.IsNullOrWhiteSpace(target.ControlName))
        {
            dynamic condition = automation.CreatePropertyCondition(UiaNamePropertyId, target.ControlName);
            dynamic element = root.FindFirst(TreeScopeDescendants, condition);
            if (element is not null) return element;
        }

        throw new InvalidOperationException("UI Automation target requires a matching AutomationId or ControlName.");
    }

    private static nint FindTargetWindow(DeviceTarget target)
    {
        if (target.WindowHandle is long raw && raw != 0) return new nint(raw);
        nint found = nint.Zero;
        _ = EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            var title = ReadTitle(handle);
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (target.ProcessId is int expectedProcess && processId != expectedProcess) return true;
            if (!string.IsNullOrWhiteSpace(target.WindowTitle) && !title.Contains(target.WindowTitle, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(target.Application))
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    if (!process.ProcessName.Contains(target.Application, StringComparison.OrdinalIgnoreCase) && !title.Contains(target.Application, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { return true; }
            }
            found = handle;
            return false;
        }, nint.Zero);
        return found;
    }

    private static string ReadTitle(nint handle)
    {
        var builder = new StringBuilder(512);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);
}

internal static class WindowsInput
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;

    public static void SendUnicodeText(string text)
    {
        foreach (var character in text)
        {
            var inputs = new[]
            {
                KeyboardUnicode(character, keyUp: false),
                KeyboardUnicode(character, keyUp: true)
            };
            Send(inputs);
        }
    }

    public static void SendHotkey(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException("Hotkey cannot be empty.", nameof(hotkey));
        var keys = parts.Select(ParseVirtualKey).ToArray();
        var inputs = new List<INPUT>();
        inputs.AddRange(keys.Select(key => KeyboardVirtualKey(key, keyUp: false)));
        inputs.AddRange(keys.Reverse().Select(key => KeyboardVirtualKey(key, keyUp: true)));
        Send(inputs.ToArray());
    }

    public static void Click(int x, int y, string button)
    {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Windows refused to move the mouse pointer.");
        var right = string.Equals(button, "right", StringComparison.OrdinalIgnoreCase);
        var down = right ? MouseeventfRightdown : MouseeventfLeftdown;
        var up = right ? MouseeventfRightup : MouseeventfLeftup;
        Send(new[] { Mouse(down), Mouse(up) });
    }

    private static ushort ParseVirtualKey(string value) => value.Trim().ToLowerInvariant() switch
    {
        "ctrl" or "control" => 0x11,
        "shift" => 0x10,
        "alt" => 0x12,
        "win" or "windows" => 0x5B,
        "enter" => 0x0D,
        "tab" => 0x09,
        "esc" or "escape" => 0x1B,
        "delete" or "del" => 0x2E,
        "backspace" => 0x08,
        "space" => 0x20,
        var key when key.Length == 1 => (ushort)char.ToUpperInvariant(key[0]),
        _ => throw new ArgumentException($"Unsupported hotkey component '{value}'.")
    };

    private static INPUT KeyboardUnicode(char character, bool keyUp) => new()
    {
        type = InputKeyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = character,
                dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0)
            }
        }
    };

    private static INPUT KeyboardVirtualKey(ushort key, bool keyUp) => new()
    {
        type = InputKeyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                dwFlags = keyUp ? KeyeventfKeyup : 0
            }
        }
    };

    private static INPUT Mouse(uint flags) => new()
    {
        type = InputMouse,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } }
    };

    private static void Send(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) throw new InvalidOperationException($"SendInput accepted {sent} of {inputs.Length} events.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
