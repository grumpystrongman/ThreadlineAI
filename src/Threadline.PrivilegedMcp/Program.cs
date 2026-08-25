using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Threadline.McpCommon;

await new Threadline.PrivilegedMcp.PrivilegedMcpServer().RunAsync();

namespace Threadline.PrivilegedMcp
{
internal sealed class PrivilegedMcpServer : McpStdioServer
{
    private readonly string _brokerExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI", "runtimes", "PrivilegedBroker", "Threadline.PrivilegedBroker.exe");

    protected override string ServerName => "threadline-privileged";
    protected override string ServerVersion => "0.1.0";
    protected override string ToolFailurePrefix => "Protected Windows operation failed";
    protected override string Instructions =>
        "Every tool in this server is protected and destructiveHint=true. Jarvis must obtain user approval through its normal tool-approval workflow before invocation. Execution then crosses Windows' normal UAC elevation boundary; never bypass UAC or credentials. Only allowlisted broker operations exist; there is no arbitrary elevated shell.";

    protected override async Task<JsonObject> ExecuteToolAsync(JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new ArgumentException("Tool name is required.");
        var args = parameters?["arguments"] as JsonObject ?? new JsonObject();
        var (operation, values) = name switch
        {
            "protected_install_package" => ("winget_install", Dict(("packageId", RequiredString(args, "package_id")))),
            "protected_service_start" => ("service_start", Dict(("serviceName", RequiredString(args, "service_name")))),
            "protected_service_stop" => ("service_stop", Dict(("serviceName", RequiredString(args, "service_name")))),
            "protected_service_restart" => ("service_restart", Dict(("serviceName", RequiredString(args, "service_name")))),
            "protected_registry_set_hklm_software" => (
                "registry_set_hklm_software",
                Dict(
                    ("subKey", RequiredString(args, "subkey")),
                    ("name", RequiredString(args, "name")),
                    ("value", OptionalStringAllowEmpty(args, "value") ?? string.Empty),
                    ("kind", OptionalString(args, "kind") ?? "string"))),
            _ => throw new ArgumentException($"Unknown protected tool '{name}'.")
        };

        var result = await InvokeBrokerAsync(operation, values, cancellationToken);
        return ToolResult(JsonSerializer.Serialize(result, JsonOptions), !result.Success);
    }

    private async Task<BrokerResult> InvokeBrokerAsync(string operation, Dictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(_brokerExe))
            return new BrokerResult(false, null, $"Privileged broker is not installed at {_brokerExe}.", false);

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreadlineAI", "broker-requests");
        Directory.CreateDirectory(root);
        var requestId = $"priv-{Guid.NewGuid():N}";
        var requestPath = Path.Combine(root, requestId + ".request.json");
        var responsePath = Path.Combine(root, requestId + ".response.json");
        var request = new { requestId, operation, arguments, createdAt = DateTimeOffset.UtcNow };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), new UTF8Encoding(false), cancellationToken);

        try
        {
            var brokerDirectory = Path.GetDirectoryName(_brokerExe)
                ?? throw new InvalidOperationException("Privileged broker path has no parent directory.");
            var psi = new ProcessStartInfo
            {
                FileName = _brokerExe,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = brokerDirectory,
                Arguments = $"--request \"{requestPath}\" --response \"{responsePath}\""
            };

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new BrokerResult(false, null, "The owner cancelled the Windows elevation prompt.", true);
            }

            if (process is null)
                return new BrokerResult(false, null, "Windows did not start the privileged broker.", false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new BrokerResult(false, null, "Privileged broker did not finish within 15 minutes.", false);
            }

            if (!File.Exists(responsePath))
                return new BrokerResult(false, null, $"Privileged broker exited with code {process.ExitCode} without a response.", false);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath, cancellationToken));
            var rootElement = document.RootElement;
            var success = rootElement.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
            var error = rootElement.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            Dictionary<string, string>? details = null;
            if (rootElement.TryGetProperty("details", out var detailsElement) && detailsElement.ValueKind == JsonValueKind.Object)
            {
                details = detailsElement.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            return new BrokerResult(success, details, error, false);
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    protected override JsonObject BuildToolList() => new()
    {
        ["tools"] = new JsonArray
        {
            ProtectedTool("protected_install_package", "Install one exact winget package through the elevated allowlisted broker. Requires Jarvis user approval and then Windows UAC.", Schema([Prop("package_id", "string", "Exact winget package id, e.g. Microsoft.PowerToys.")], ["package_id"])),
            ProtectedTool("protected_service_start", "Start one non-security-critical Windows service through the elevated allowlisted broker.", Schema([Prop("service_name", "string", "Windows service name, not display name.")], ["service_name"])),
            ProtectedTool("protected_service_stop", "Stop one non-security-critical Windows service through the elevated allowlisted broker.", Schema([Prop("service_name", "string", "Windows service name, not display name.")], ["service_name"])),
            ProtectedTool("protected_service_restart", "Restart one non-security-critical Windows service through the elevated allowlisted broker.", Schema([Prop("service_name", "string", "Windows service name, not display name.")], ["service_name"])),
            ProtectedTool("protected_registry_set_hklm_software", "Set a string or DWORD under HKLM\\SOFTWARE only. Other registry hives/paths are refused by the broker.", Schema([
                Prop("subkey", "string", "Subkey under HKLM, beginning SOFTWARE\\..."),
                Prop("name", "string", "Registry value name."),
                Prop("value", "string", "Registry value data."),
                Prop("kind", "string", "string or dword; default string.")], ["subkey", "name", "value"]))
        }
    };

    private static JsonObject ProtectedTool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JsonObject
        {
            ["destructiveHint"] = true,
            ["readOnlyHint"] = false,
            ["idempotentHint"] = false,
            ["openWorldHint"] = false
        }
    };

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    private static string RequiredString(JsonObject args, string name) =>
        OptionalString(args, name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"'{name}' is required.");

    private static string? OptionalString(JsonObject args, string name) =>
        args[name] is null ? null : args[name]!.GetValue<string>().Trim();

    private static string? OptionalStringAllowEmpty(JsonObject args, string name) =>
        args[name] is null ? null : args[name]!.GetValue<string>();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup; stale broker requests cannot execute without a fresh UAC launch.
        }
    }

    private sealed record BrokerResult(
        bool Success,
        IReadOnlyDictionary<string, string>? Details,
        string? Error,
        bool UserCancelled);
}
}
