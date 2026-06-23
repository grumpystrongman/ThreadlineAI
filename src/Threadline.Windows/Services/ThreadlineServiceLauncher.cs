using System.Diagnostics;

namespace Threadline.Windows.Services;

public static class ThreadlineServiceLauncher
{
    public const string DefaultServiceUrl = "http://localhost:5057";

    public static async Task<ThreadlineServiceLaunchResult> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (await IsHealthyAsync(cancellationToken))
        {
            return new ThreadlineServiceLaunchResult(true, false, null, "Service: running");
        }

        var target = FindServiceLaunchTarget();
        if (target is null)
        {
            return new ThreadlineServiceLaunchResult(
                false,
                false,
                null,
                "Service: not found. Build Threadline.Service or install the Windows service package.");
        }

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThreadlineAI",
            "logs");

        Directory.CreateDirectory(logDirectory);

        var processInfo = new ProcessStartInfo
        {
            FileName = target.FileName,
            Arguments = target.Arguments,
            WorkingDirectory = target.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        processInfo.Environment["ASPNETCORE_URLS"] = DefaultServiceUrl;
        processInfo.Environment["THREADLINE_LAUNCHED_BY"] = "Threadline.Windows";

        try
        {
            var process = Process.Start(processInfo);
            if (process is null)
            {
                return new ThreadlineServiceLaunchResult(false, true, null, "Service: launch failed.");
            }

            var healthy = await WaitForHealthAsync(process, cancellationToken);
            if (healthy)
            {
                return new ThreadlineServiceLaunchResult(true, true, process.Id, $"Service: started on launch (PID {process.Id})");
            }

            var status = process.HasExited
                ? $"Service: exited during startup with code {process.ExitCode}."
                : "Service: launched but did not become healthy yet.";

            return new ThreadlineServiceLaunchResult(false, true, process.HasExited ? null : process.Id, status);
        }
        catch (Exception ex)
        {
            return new ThreadlineServiceLaunchResult(false, true, null, "Service: launch failed. " + ex.Message);
        }
    }

    private static async Task<bool> WaitForHealthAsync(Process process, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                return false;
            }

            if (await IsHealthyAsync(cancellationToken))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));

            using var client = new HttpClient
            {
                BaseAddress = new Uri(DefaultServiceUrl)
            };

            using var response = await client.GetAsync("health", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static ServiceLaunchTarget? FindServiceLaunchTarget()
    {
        var appBase = AppContext.BaseDirectory;
        var directCandidates = new[]
        {
            Path.Combine(appBase, "Threadline.Service.exe"),
            Path.Combine(appBase, "Threadline.Service.dll"),
            Path.Combine(appBase, "service", "Threadline.Service.exe"),
            Path.Combine(appBase, "service", "Threadline.Service.dll")
        };

        foreach (var candidate in directCandidates)
        {
            var target = BuildTarget(candidate);
            if (target is not null)
            {
                return target;
            }
        }

        var repoRoot = FindRepositoryRoot(appBase);
        if (repoRoot is not null)
        {
            var knownBuildCandidates = new[]
            {
                Path.Combine(repoRoot, "src", "Threadline.Service", "bin", "Release", "net8.0", "Threadline.Service.exe"),
                Path.Combine(repoRoot, "src", "Threadline.Service", "bin", "Release", "net8.0", "Threadline.Service.dll"),
                Path.Combine(repoRoot, "src", "Threadline.Service", "bin", "Debug", "net8.0", "Threadline.Service.exe"),
                Path.Combine(repoRoot, "src", "Threadline.Service", "bin", "Debug", "net8.0", "Threadline.Service.dll")
            };

            foreach (var candidate in knownBuildCandidates)
            {
                var target = BuildTarget(candidate);
                if (target is not null)
                {
                    return target;
                }
            }

            var serviceBin = Path.Combine(repoRoot, "src", "Threadline.Service", "bin");
            if (Directory.Exists(serviceBin))
            {
                var recursiveCandidate = Directory.EnumerateFiles(serviceBin, "Threadline.Service.exe", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(serviceBin, "Threadline.Service.dll", SearchOption.AllDirectories))
                    .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                && !path.Contains($"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .Select(info => info.FullName)
                    .FirstOrDefault();

                var target = BuildTarget(recursiveCandidate);
                if (target is not null)
                {
                    return target;
                }
            }
        }

        return null;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var serviceProjectDirectory = Path.Combine(directory.FullName, "src", "Threadline.Service");
            if (Directory.Exists(serviceProjectDirectory))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static ServiceLaunchTarget? BuildTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceLaunchTarget(path, string.Empty, Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        }

        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceLaunchTarget("dotnet", $"\"{path}\"", Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        }

        return null;
    }

    private sealed record ServiceLaunchTarget(string FileName, string Arguments, string WorkingDirectory);
}

public sealed record ThreadlineServiceLaunchResult(bool Success, bool LaunchAttempted, int? ProcessId, string Message);
