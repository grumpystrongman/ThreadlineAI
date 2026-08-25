using System.Diagnostics;

namespace Threadline.Windows.Services;

public static class PersonalJarvisRuntimeLauncher
{
    public const string DefaultJarvisUrl = "http://127.0.0.1:47821";

    public static string DefaultRuntimeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI",
        "runtimes",
        "PersonalJarvis");

    public static string DefaultMcpConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI",
        "jarvis",
        "mcp.json");

    public static async Task<PersonalJarvisLaunchResult> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (await IsHealthyAsync(cancellationToken))
        {
            return new PersonalJarvisLaunchResult(true, true, false, null, "Jarvis: running");
        }

        var target = FindInstalledRuntime();
        if (target is null)
        {
            return new PersonalJarvisLaunchResult(
                false,
                false,
                false,
                null,
                "Jarvis: runtime not installed yet. Use the Threadline PersonalJarvis bootstrap once; future launches will start it automatically.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = target.FileName,
            Arguments = target.Arguments,
            WorkingDirectory = target.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.Environment["THREADLINE_LAUNCHED_BY"] = "Threadline.Windows";
        startInfo.Environment["JARVIS_MCP_CONFIG"] = DefaultMcpConfigPath;

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new PersonalJarvisLaunchResult(false, true, false, null, "Jarvis: launch failed.");
            }

            var healthy = await WaitForHealthAsync(process, cancellationToken);
            if (healthy)
            {
                return new PersonalJarvisLaunchResult(
                    true,
                    true,
                    true,
                    process.Id,
                    $"Jarvis: started automatically (PID {process.Id}); MCP config: {DefaultMcpConfigPath}");
            }

            var message = process.HasExited
                ? $"Jarvis: exited during startup with code {process.ExitCode}."
                : "Jarvis: launched but did not become ready yet.";

            return new PersonalJarvisLaunchResult(false, true, true, process.HasExited ? null : process.Id, message);
        }
        catch (Exception ex)
        {
            return new PersonalJarvisLaunchResult(false, true, true, null, "Jarvis: launch failed. " + ex.Message);
        }
    }

    private static RuntimeLaunchTarget? FindInstalledRuntime()
    {
        var runtimeRoot = DefaultRuntimeRoot;
        var executable = Path.Combine(runtimeRoot, ".venv", "Scripts", "jarvis.exe");
        if (File.Exists(executable))
        {
            return new RuntimeLaunchTarget(executable, "serve", runtimeRoot);
        }

        var python = Path.Combine(runtimeRoot, ".venv", "Scripts", "python.exe");
        if (File.Exists(python))
        {
            return new RuntimeLaunchTarget(python, "-m jarvis serve", runtimeRoot);
        }

        return null;
    }

    private static async Task<bool> WaitForHealthAsync(Process process, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) return false;
            if (await IsHealthyAsync(cancellationToken)) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1200));
            using var client = new HttpClient { BaseAddress = new Uri(DefaultJarvisUrl + "/") };
            using var response = await client.GetAsync("api/missions?limit=1", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private sealed record RuntimeLaunchTarget(string FileName, string Arguments, string WorkingDirectory);
}

public sealed record PersonalJarvisLaunchResult(
    bool Success,
    bool Installed,
    bool LaunchAttempted,
    int? ProcessId,
    string Message);
