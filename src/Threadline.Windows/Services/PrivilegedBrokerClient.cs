using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Threadline.Windows.Services;

public sealed record PrivilegedBrokerRequest(string Operation, IReadOnlyDictionary<string, string> Arguments);
public sealed record PrivilegedBrokerResult(bool Success, IReadOnlyDictionary<string, string>? Details, string? Error, bool UserCancelled = false);

/// <summary>
/// Launches the allowlisted privileged helper through Windows UAC for genuinely elevated work.
/// This is intentionally separate from Owner Authority: routine/consequential work can be pre-authorized,
/// but protected work must cross the OS elevation boundary rather than bypass it.
/// </summary>
public sealed class PrivilegedBrokerClient
{
    public static string DefaultBrokerExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI", "runtimes", "PrivilegedBroker", "Threadline.PrivilegedBroker.exe");

    private readonly string _brokerExecutable;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PrivilegedBrokerClient(string? brokerExecutable = null) => _brokerExecutable = brokerExecutable ?? DefaultBrokerExecutable;

    public async Task<PrivilegedBrokerResult> ExecuteAsync(PrivilegedBrokerRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_brokerExecutable))
            return new(false, null, $"Privileged broker is not installed at {_brokerExecutable}.");
        if (!AllowedOperations.Contains(request.Operation))
            return new(false, null, $"Protected operation '{request.Operation}' is not allowlisted by the broker client.");

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreadlineAI", "broker-requests");
        Directory.CreateDirectory(root);
        var requestId = $"priv-{Guid.NewGuid():N}";
        var requestPath = Path.Combine(root, requestId + ".request.json");
        var responsePath = Path.Combine(root, requestId + ".response.json");
        var envelope = new { requestId, operation = request.Operation, arguments = request.Arguments, createdAt = DateTimeOffset.UtcNow };
        File.WriteAllText(requestPath, JsonSerializer.Serialize(envelope, _json), new UTF8Encoding(false));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _brokerExecutable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(_brokerExecutable)!,
                Arguments = $"--request \"{requestPath}\" --response \"{responsePath}\""
            };
            Process? process;
            try { process = Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new(false, null, "Protected operation was cancelled at the Windows elevation prompt.", UserCancelled: true);
            }
            if (process is null) return new(false, null, "Windows did not start the privileged broker.");
            await process.WaitForExitAsync(cancellationToken);
            if (!File.Exists(responsePath)) return new(false, null, $"Privileged broker exited with code {process.ExitCode} without a response.");

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(responsePath, cancellationToken));
            var rootElement = document.RootElement;
            var success = rootElement.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
            var error = rootElement.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : null;
            Dictionary<string, string>? details = null;
            if (rootElement.TryGetProperty("details", out var detailsElement) && detailsElement.ValueKind == JsonValueKind.Object)
            {
                details = detailsElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            return new(success, details, error);
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    private static readonly HashSet<string> AllowedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "winget_install", "service_start", "service_stop", "service_restart", "registry_set_hklm_software"
    };

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
}
