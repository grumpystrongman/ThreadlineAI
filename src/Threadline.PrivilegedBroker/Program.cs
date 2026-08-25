using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

var exitCode = await PrivilegedBroker.RunAsync(args);
Environment.ExitCode = exitCode;

internal static partial class PrivilegedBroker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string RequestRoot = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI", "broker-requests"));
    private static readonly HashSet<string> BlockedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "WdNisSvc", "SecurityHealthService", "EventLog", "MpsSvc", "Sense", "CryptSvc"
    };

    public static async Task<int> RunAsync(string[] args)
    {
        string? requestPath = null;
        string? responsePath = null;
        try
        {
            requestPath = ValueAfter(args, "--request") ?? throw new ArgumentException("--request is required.");
            responsePath = ValueAfter(args, "--response") ?? throw new ArgumentException("--response is required.");
            requestPath = ValidateBrokerPath(requestPath);
            responsePath = ValidateBrokerPath(responsePath);
            Directory.CreateDirectory(RequestRoot);

            if (!IsAdministrator()) throw new UnauthorizedAccessException("Privileged broker must run elevated.");
            if (!File.Exists(requestPath)) throw new FileNotFoundException("Broker request file was not found.", requestPath);

            var request = JsonSerializer.Deserialize<BrokerRequest>(await File.ReadAllTextAsync(requestPath), Json)
                ?? throw new InvalidOperationException("Broker request was empty.");
            if (string.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("requestId is required.");
            if (DateTimeOffset.UtcNow - request.CreatedAt > TimeSpan.FromMinutes(2)) throw new InvalidOperationException("Broker request expired before execution.");

            var result = await ExecuteAsync(request);
            await WriteResponseAsync(responsePath, result);
            TryDelete(requestPath);
            return result.Success ? 0 : 2;
        }
        catch (Exception ex)
        {
            if (responsePath is not null)
            {
                try { await WriteResponseAsync(responsePath, new BrokerResponse("unknown", false, null, ex.Message, DateTimeOffset.UtcNow)); }
                catch { }
            }
            if (requestPath is not null) TryDelete(requestPath);
            return 1;
        }
    }

    private static async Task<BrokerResponse> ExecuteAsync(BrokerRequest request)
    {
        var operation = request.Operation.Trim().ToLowerInvariant();
        var details = operation switch
        {
            "winget_install" => await WingetInstallAsync(request.Arguments),
            "service_start" => await ServiceActionAsync(request.Arguments, "start"),
            "service_stop" => await ServiceActionAsync(request.Arguments, "stop"),
            "service_restart" => await ServiceRestartAsync(request.Arguments),
            "registry_set_hklm_software" => RegistrySet(request.Arguments),
            _ => throw new InvalidOperationException($"Protected operation '{request.Operation}' is not allowlisted by this broker.")
        };
        return new BrokerResponse(request.RequestId, true, details, null, DateTimeOffset.UtcNow);
    }

    private static async Task<Dictionary<string, string>> WingetInstallAsync(Dictionary<string, string> args)
    {
        var packageId = Required(args, "packageId");
        if (!SafeIdentifier().IsMatch(packageId)) throw new ArgumentException("winget packageId contains unsupported characters.");
        return await RunCheckedAsync("winget.exe", ["install", "--id", packageId, "--exact", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], 900);
    }

    private static async Task<Dictionary<string, string>> ServiceActionAsync(Dictionary<string, string> args, string action)
    {
        var service = ValidateServiceName(Required(args, "serviceName"));
        return await RunCheckedAsync("sc.exe", [action, service], 120);
    }

    private static async Task<Dictionary<string, string>> ServiceRestartAsync(Dictionary<string, string> args)
    {
        var service = ValidateServiceName(Required(args, "serviceName"));
        var stop = await RunCheckedAsync("sc.exe", ["stop", service], 120, allowServiceAlreadyState: true);
        await Task.Delay(750);
        var start = await RunCheckedAsync("sc.exe", ["start", service], 120, allowServiceAlreadyState: true);
        return new Dictionary<string, string>
        {
            ["service"] = service,
            ["stop"] = stop.GetValueOrDefault("stdout", string.Empty),
            ["start"] = start.GetValueOrDefault("stdout", string.Empty)
        };
    }

    private static Dictionary<string, string> RegistrySet(Dictionary<string, string> args)
    {
        var subKey = Required(args, "subKey").Trim().TrimStart('\\');
        if (!subKey.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase) && !string.Equals(subKey, "SOFTWARE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Broker registry writes are restricted to HKLM\\SOFTWARE.");
        var name = Required(args, "name");
        if (name.Length > 256) throw new ArgumentException("Registry value name is too long.");
        var value = args.GetValueOrDefault("value") ?? string.Empty;
        var kind = args.GetValueOrDefault("kind")?.Trim().ToLowerInvariant() ?? "string";
        using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true) ?? throw new UnauthorizedAccessException($"Cannot open HKLM\\{subKey} for writing.");
        switch (kind)
        {
            case "string": key.SetValue(name, value, RegistryValueKind.String); break;
            case "dword":
                if (!int.TryParse(value, out var number)) throw new ArgumentException("DWORD value must be an integer.");
                key.SetValue(name, number, RegistryValueKind.DWord); break;
            default: throw new ArgumentException("Registry kind must be string or dword.");
        }
        return new Dictionary<string, string> { ["key"] = $"HKLM\\{subKey}", ["name"] = name, ["kind"] = kind };
    }

    private static string ValidateServiceName(string value)
    {
        if (!SafeIdentifier().IsMatch(value)) throw new ArgumentException("Service name contains unsupported characters.");
        if (BlockedServices.Contains(value)) throw new UnauthorizedAccessException($"Service '{value}' is security-critical and cannot be controlled by the Threadline broker.");
        return value;
    }

    private static async Task<Dictionary<string, string>> RunCheckedAsync(string file, IEnumerable<string> arguments, int timeoutSeconds, bool allowServiceAlreadyState = false)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {file}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{file} timed out after {timeoutSeconds} seconds.");
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var tolerated = allowServiceAlreadyState && (stdout.Contains("already", StringComparison.OrdinalIgnoreCase) || stderr.Contains("already", StringComparison.OrdinalIgnoreCase));
        if (process.ExitCode != 0 && !tolerated) throw new InvalidOperationException($"{file} exited with {process.ExitCode}: {Trim(stderr)} {Trim(stdout)}".Trim());
        return new Dictionary<string, string> { ["exitCode"] = process.ExitCode.ToString(), ["stdout"] = Trim(stdout), ["stderr"] = Trim(stderr) };
    }

    private static string ValidateBrokerPath(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var root = RequestRoot.EndsWith(Path.DirectorySeparatorChar) ? RequestRoot : RequestRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Broker request/response files must stay inside the Threadline broker-requests directory.");
        return full;
    }

    private static async Task WriteResponseAsync(string path, BrokerResponse response)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(response, Json));
        File.Move(temp, path, overwrite: true);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    private static string Required(Dictionary<string, string> args, string name) => args.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"'{name}' is required.");
    private static string? ValueAfter(string[] args, string flag) { var index = Array.FindIndex(args, item => string.Equals(item, flag, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static string Trim(string value) => value.Length <= 12000 ? value.Trim() : value[..12000].Trim() + "...[truncated]";
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,200}$", RegexOptions.CultureInvariant)] private static partial Regex SafeIdentifier();
    private sealed record BrokerRequest(string RequestId, string Operation, Dictionary<string, string> Arguments, DateTimeOffset CreatedAt);
    private sealed record BrokerResponse(string RequestId, bool Success, Dictionary<string, string>? Details, string? Error, DateTimeOffset CompletedAt);
}
