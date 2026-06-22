using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Threadline.Infrastructure.Sqlite;

namespace Threadline.Service;

public sealed class ThreadlineCommercialLifecycleService
{
    public const string ClearLocalDataConfirmation = "CLEAR THREADLINE LOCAL DATA";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ThreadlineServiceOptions _options;
    private readonly SqliteOptions _sqliteOptions;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public ThreadlineCommercialLifecycleService(
        ThreadlineServiceOptions options,
        SqliteOptions sqliteOptions,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _options = options;
        _sqliteOptions = sqliteOptions;
        _configuration = configuration;
        _environment = environment;
    }

    public ThreadlineVersionInfo BuildVersionInfo()
    {
        var serviceAssembly = typeof(ThreadlineCommercialLifecycleService).Assembly;
        var entryAssembly = Assembly.GetEntryAssembly() ?? serviceAssembly;
        var productVersion = serviceAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? serviceAssembly.GetName().Version?.ToString()
            ?? "unknown";

        return new ThreadlineVersionInfo(
            ProductName: "ThreadlineAI",
            ProductVersion: productVersion,
            ServiceName: "ThreadlineAI Service",
            ServiceAssemblyVersion: serviceAssembly.GetName().Version?.ToString() ?? "unknown",
            EntryAssemblyVersion: entryAssembly.GetName().Version?.ToString() ?? "unknown",
            BuildChannel: _configuration["Threadline:BuildChannel"] ?? Environment.GetEnvironmentVariable("THREADLINE_BUILD_CHANNEL") ?? "local",
            ApiCompatibility: "build-21-commercial-lifecycle",
            DatabaseSchemaVersion: "sqlite-auto-initialized",
            ExpectedBrowserExtensionVersion: _configuration["Threadline:ExpectedBrowserExtensionVersion"] ?? "17.x",
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    public ThreadlineLifecycleManifest BuildManifest()
    {
        var version = BuildVersionInfo();
        var root = ResolveThreadlineRoot();
        var targets = BuildLocalDataPlan().Targets;
        var diagnosticsRoot = Path.Combine(root, "diagnostics");

        return new ThreadlineLifecycleManifest(
            Version: version,
            Readiness: "commercial-lifecycle-scaffold",
            LocalOnlyMode: _options.LocalOnlyMode,
            ApiTokenRequired: _options.RequireApiToken,
            ApiTokenPresent: !string.IsNullOrWhiteSpace(LocalApiTokenStore.TryReadToken(_options.ApiTokenPath)),
            CorsAllowedOrigins: _options.CorsAllowedOrigins.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            DatabasePath: _sqliteOptions.DatabasePath,
            DiagnosticsRoot: diagnosticsRoot,
            ContentRootPath: _environment.ContentRootPath,
            EnvironmentName: _environment.EnvironmentName,
            LocalDataTargets: targets,
            Notes:
            [
                "Secrets and local API token values are never included in this manifest.",
                "Use the diagnostics export for support review; use clear-local-data only after explicit user confirmation.",
                "Service install/uninstall/recovery is handled by the Build 21 PowerShell scripts and MSI packaging layer."
            ]);
    }

    public ThreadlineLocalDataPlan BuildLocalDataPlan()
    {
        var root = ResolveThreadlineRoot();
        var secretRoot = ResolveSecretStoreRoot(root);
        var diagnosticsRoot = Path.Combine(root, "diagnostics");
        var logsRoot = Path.Combine(root, "logs");

        var targets = new List<ThreadlineLocalDataTarget>
        {
            FileTarget("sqlite-database", "SQLite database", _sqliteOptions.DatabasePath),
            FileTarget("sqlite-wal", "SQLite write-ahead log", _sqliteOptions.DatabasePath + "-wal"),
            FileTarget("sqlite-shm", "SQLite shared memory file", _sqliteOptions.DatabasePath + "-shm"),
            FileTarget("local-api-token", "Local API/browser-extension token", _options.ApiTokenPath),
            DirectoryTarget("secret-store", "DPAPI-protected provider credential store", secretRoot),
            DirectoryTarget("logs", "Application and service logs", logsRoot),
            DirectoryTarget("diagnostics", "Generated diagnostics packages", diagnosticsRoot),
            FileTarget("first-run-state", "First-run setup completion state", Path.Combine(root, "first-run-complete.json")),
            FileTarget("settings", "Local sidecar settings", Path.Combine(root, "settings.json"))
        };

        return new ThreadlineLocalDataPlan(
            ConfirmationPhrase: ClearLocalDataConfirmation,
            Warning: "This removes local ThreadlineAI state for the current Windows user. Stop the service first for the cleanest database deletion.",
            Targets: targets);
    }

    public async Task<ThreadlineDiagnosticsExport> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var root = ResolveThreadlineRoot();
        var diagnosticsRoot = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);

        var createdAt = DateTimeOffset.UtcNow;
        var exportPath = Path.Combine(diagnosticsRoot, $"threadline-diagnostics-{createdAt:yyyyMMdd-HHmmss}.zip");
        var manifest = BuildManifest();
        var plan = BuildLocalDataPlan();

        await using var file = File.Create(exportPath);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        await WriteJsonEntryAsync(zip, "manifest.json", manifest, cancellationToken);
        await WriteJsonEntryAsync(zip, "local-data-clear-plan.json", plan, cancellationToken);
        await WriteTextEntryAsync(zip, "service-health.txt", BuildServiceHealthText(manifest), cancellationToken);
        await CopyDirectoryIntoZipAsync(zip, Path.Combine(root, "logs"), "logs", cancellationToken);

        return new ThreadlineDiagnosticsExport(exportPath, createdAt, manifest);
    }

    public Task<ThreadlineLocalDataClearResult> ClearLocalDataAsync(ClearLocalDataRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Confirmation, ClearLocalDataConfirmation, StringComparison.Ordinal))
        {
            return Task.FromResult(new ThreadlineLocalDataClearResult(false, "Confirmation phrase did not match. No data was removed.", []));
        }

        var removed = new List<ThreadlineLocalDataRemoval>();
        foreach (var target in BuildLocalDataPlan().Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.IncludeDiagnostics && target.Id.Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                removed.Add(new ThreadlineLocalDataRemoval(target.Id, target.Path, false, "Skipped by request."));
                continue;
            }

            removed.Add(RemoveTarget(target));
        }

        var success = removed.All(item => item.Removed || item.Detail.Contains("not present", StringComparison.OrdinalIgnoreCase) || item.Detail.Contains("Skipped", StringComparison.OrdinalIgnoreCase));
        var message = success
            ? "ThreadlineAI local data targets were removed or were already absent. Restart ThreadlineAI to recreate a clean local profile."
            : "Some ThreadlineAI local data targets could not be removed. Export diagnostics or stop the local service and run eng/clear-local-data.ps1.";
        return Task.FromResult(new ThreadlineLocalDataClearResult(success, message, removed));
    }

    private string ResolveThreadlineRoot()
    {
        var configured = _configuration["Threadline:LocalDataRoot"] ?? Environment.GetEnvironmentVariable("THREADLINE_LOCAL_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var databaseDirectory = Path.GetDirectoryName(_sqliteOptions.DatabasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory)) return databaseDirectory;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreadlineAI");
    }

    private string ResolveSecretStoreRoot(string root) =>
        _configuration["Threadline:SecretStorePath"]
        ?? Environment.GetEnvironmentVariable("THREADLINE_SECRET_STORE_PATH")
        ?? Path.Combine(root, "secrets");

    private static ThreadlineLocalDataTarget FileTarget(string id, string displayName, string path) =>
        new(id, displayName, "file", path, File.Exists(path));

    private static ThreadlineLocalDataTarget DirectoryTarget(string id, string displayName, string path) =>
        new(id, displayName, "directory", path, Directory.Exists(path));

    private static ThreadlineLocalDataRemoval RemoveTarget(ThreadlineLocalDataTarget target)
    {
        try
        {
            if (target.Kind.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(target.Path)) return new ThreadlineLocalDataRemoval(target.Id, target.Path, true, "File was not present.");
                File.Delete(target.Path);
                return new ThreadlineLocalDataRemoval(target.Id, target.Path, !File.Exists(target.Path), File.Exists(target.Path) ? "File still exists after delete attempt." : "File removed.");
            }

            if (!Directory.Exists(target.Path)) return new ThreadlineLocalDataRemoval(target.Id, target.Path, true, "Directory was not present.");
            Directory.Delete(target.Path, recursive: true);
            return new ThreadlineLocalDataRemoval(target.Id, target.Path, !Directory.Exists(target.Path), Directory.Exists(target.Path) ? "Directory still exists after delete attempt." : "Directory removed.");
        }
        catch (Exception ex)
        {
            return new ThreadlineLocalDataRemoval(target.Id, target.Path, false, ex.Message);
        }
    }

    private static string BuildServiceHealthText(ThreadlineLifecycleManifest manifest) =>
        $"ThreadlineAI diagnostics export\nCreated: {manifest.Version.GeneratedAt:O}\nProduct version: {manifest.Version.ProductVersion}\nService version: {manifest.Version.ServiceAssemblyVersion}\nAPI compatibility: {manifest.Version.ApiCompatibility}\nToken required: {manifest.ApiTokenRequired}\nToken present: {manifest.ApiTokenPresent}\nDatabase path: {manifest.DatabasePath}\nDiagnostics root: {manifest.DiagnosticsRoot}\n";

    private static async Task WriteJsonEntryAsync<T>(ZipArchive zip, string name, T value, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static async Task WriteTextEntryAsync(ZipArchive zip, string name, string value, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(value.AsMemory(), cancellationToken);
    }

    private static async Task CopyDirectoryIntoZipAsync(ZipArchive zip, string directory, string zipPrefix, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            var entry = zip.CreateEntry($"{zipPrefix}/{relative}");
            await using var input = File.OpenRead(path);
            await using var output = entry.Open();
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}

public static class ThreadlineCommercialLifecycleEndpointMappings
{
    public static WebApplication MapThreadlineCommercialLifecycleApi(this WebApplication app)
    {
        var api = app.MapGroup(string.Empty).RequireThreadlineLocalAccess();

        api.MapGet("/version", (ThreadlineCommercialLifecycleService lifecycle) =>
            Results.Ok(lifecycle.BuildVersionInfo()));

        api.MapGet("/diagnostics/manifest", (ThreadlineCommercialLifecycleService lifecycle) =>
            Results.Ok(lifecycle.BuildManifest()));

        api.MapPost("/diagnostics/export", async (ThreadlineCommercialLifecycleService lifecycle, CancellationToken ct) =>
            Results.Ok(await lifecycle.ExportDiagnosticsAsync(ct)));

        api.MapGet("/local-data/clear-plan", (ThreadlineCommercialLifecycleService lifecycle) =>
            Results.Ok(lifecycle.BuildLocalDataPlan()));

        api.MapPost("/local-data/clear", async (ClearLocalDataRequest request, ThreadlineCommercialLifecycleService lifecycle, CancellationToken ct) =>
            Results.Ok(await lifecycle.ClearLocalDataAsync(request, ct)));

        return app;
    }
}

public sealed record ThreadlineVersionInfo(
    string ProductName,
    string ProductVersion,
    string ServiceName,
    string ServiceAssemblyVersion,
    string EntryAssemblyVersion,
    string BuildChannel,
    string ApiCompatibility,
    string DatabaseSchemaVersion,
    string ExpectedBrowserExtensionVersion,
    DateTimeOffset GeneratedAt);

public sealed record ThreadlineLifecycleManifest(
    ThreadlineVersionInfo Version,
    string Readiness,
    bool LocalOnlyMode,
    bool ApiTokenRequired,
    bool ApiTokenPresent,
    IReadOnlyList<string> CorsAllowedOrigins,
    string DatabasePath,
    string DiagnosticsRoot,
    string ContentRootPath,
    string EnvironmentName,
    IReadOnlyList<ThreadlineLocalDataTarget> LocalDataTargets,
    IReadOnlyList<string> Notes);

public sealed record ThreadlineDiagnosticsExport(
    string ExportPath,
    DateTimeOffset CreatedAt,
    ThreadlineLifecycleManifest Manifest);

public sealed record ThreadlineLocalDataPlan(
    string ConfirmationPhrase,
    string Warning,
    IReadOnlyList<ThreadlineLocalDataTarget> Targets);

public sealed record ThreadlineLocalDataTarget(
    string Id,
    string DisplayName,
    string Kind,
    string Path,
    bool Exists);

public sealed record ClearLocalDataRequest(
    string Confirmation,
    bool IncludeDiagnostics = true);

public sealed record ThreadlineLocalDataClearResult(
    bool Success,
    string Message,
    IReadOnlyList<ThreadlineLocalDataRemoval> Removals);

public sealed record ThreadlineLocalDataRemoval(
    string Id,
    string Path,
    bool Removed,
    string Detail);
