using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Threadline.Infrastructure.Sqlite;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class CommercialLifecycleSecurityTests
{
    [Fact]
    public void LocalApiRejectsMissingMalformedAndRemoteTokens()
    {
        var token = "build-21-token-that-is-long-enough-for-tests";
        var options = new ThreadlineServiceOptions(
            RequireApiToken: true,
            ApiToken: token,
            ApiTokenPath: Path.GetTempFileName(),
            MaxContextCharacters: 200_000,
            MaxSessionNameCharacters: 120,
            RetentionDays: 30,
            LocalOnlyMode: false,
            CorsAllowedOrigins: new HashSet<string>());

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        Assert.False(options.IsAuthorized(context.Request));

        context.Request.Headers["X-Threadline-Token"] = "bad-token";
        Assert.False(options.IsAuthorized(context.Request));

        context.Request.Headers["X-Threadline-Token"] = token;
        Assert.True(options.IsAuthorized(context.Request));

        var remoteContext = new DefaultHttpContext();
        remoteContext.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        remoteContext.Request.Headers["X-Threadline-Token"] = token;
        Assert.False(options.IsAuthorized(remoteContext.Request));
    }

    [Fact]
    public void BrowserExtensionTokenSetupCreatesPersistentTokenThatAuthorizesLocalRequests()
    {
        var root = CreateTempDirectory();
        var tokenPath = Path.Combine(root, "service-token.txt");

        var first = LocalApiTokenStore.GetOrCreateToken(tokenPath);
        var second = LocalApiTokenStore.GetOrCreateToken(tokenPath);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.True(first.Length >= 32);
        Assert.Equal(first, second);

        var options = new ThreadlineServiceOptions(
            RequireApiToken: true,
            ApiToken: first,
            ApiTokenPath: tokenPath,
            MaxContextCharacters: 200_000,
            MaxSessionNameCharacters: 120,
            RetentionDays: 30,
            LocalOnlyMode: false,
            CorsAllowedOrigins: new HashSet<string> { "chrome-extension://abc" });

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["Authorization"] = "Bearer " + second;

        Assert.True(options.IsAuthorized(context.Request));
    }

    [Fact]
    public void RetentionDaysRejectsZeroAndFallsBackToThirtyDays()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Threadline:RequireApiToken"] = "false",
                ["Threadline:RetentionDays"] = "0"
            })
            .Build();

        var options = ThreadlineServiceOptions.FromConfiguration(configuration);

        Assert.Equal(30, options.RetentionDays);
    }

    [Fact]
    public void DiagnosticsManifestDoesNotLeakTokenMaterial()
    {
        var root = CreateTempDirectory();
        var token = "super-secret-token-material-that-must-not-leak";
        var tokenPath = Path.Combine(root, "service-token.txt");
        File.WriteAllText(tokenPath, token);

        var service = CreateLifecycleService(root, token, tokenPath);
        var manifest = service.BuildManifest();
        var serialized = JsonSerializer.Serialize(manifest);

        Assert.True(manifest.ApiTokenRequired);
        Assert.True(manifest.ApiTokenPresent);
        Assert.DoesNotContain(token, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearLocalDataRequiresExactConfirmationAndVerifiesRemoval()
    {
        var root = CreateTempDirectory();
        var token = "clear-data-token-material-that-is-long-enough";
        var tokenPath = Path.Combine(root, "service-token.txt");
        File.WriteAllText(tokenPath, token);
        File.WriteAllText(Path.Combine(root, "threadline.db"), "db");
        File.WriteAllText(Path.Combine(root, "threadline.db-wal"), "wal");
        File.WriteAllText(Path.Combine(root, "threadline.db-shm"), "shm");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(root, "first-run-complete.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "secrets"));
        File.WriteAllText(Path.Combine(root, "secrets", "credential.json"), "secret");
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        File.WriteAllText(Path.Combine(root, "logs", "service.log"), "log");
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        File.WriteAllText(Path.Combine(root, "diagnostics", "old.zip"), "zip");

        var service = CreateLifecycleService(root, token, tokenPath);

        var rejected = await service.ClearLocalDataAsync(new ClearLocalDataRequest("wrong"));
        Assert.False(rejected.Success);
        Assert.True(File.Exists(tokenPath));

        var result = await service.ClearLocalDataAsync(new ClearLocalDataRequest(ThreadlineCommercialLifecycleService.ClearLocalDataConfirmation));

        Assert.True(result.Success);
        Assert.All(result.Removals, removal => Assert.True(removal.Removed, removal.Detail));
        Assert.False(File.Exists(Path.Combine(root, "threadline.db")));
        Assert.False(File.Exists(tokenPath));
        Assert.False(Directory.Exists(Path.Combine(root, "secrets")));
        Assert.False(Directory.Exists(Path.Combine(root, "logs")));
        Assert.False(Directory.Exists(Path.Combine(root, "diagnostics")));
        Assert.False(File.Exists(Path.Combine(root, "settings.json")));
        Assert.False(File.Exists(Path.Combine(root, "first-run-complete.json")));
    }

    private static ThreadlineCommercialLifecycleService CreateLifecycleService(string root, string token, string tokenPath)
    {
        var databasePath = Path.Combine(root, "threadline.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Threadline:LocalDataRoot"] = root,
                ["Threadline:SecretStorePath"] = Path.Combine(root, "secrets"),
                ["Threadline:BuildChannel"] = "test",
                ["Threadline:ExpectedBrowserExtensionVersion"] = "17.x"
            })
            .Build();

        var options = new ThreadlineServiceOptions(
            RequireApiToken: true,
            ApiToken: token,
            ApiTokenPath: tokenPath,
            MaxContextCharacters: 200_000,
            MaxSessionNameCharacters: 120,
            RetentionDays: 30,
            LocalOnlyMode: false,
            CorsAllowedOrigins: new HashSet<string>());

        return new ThreadlineCommercialLifecycleService(
            options,
            new SqliteOptions($"Data Source={databasePath}", databasePath),
            configuration,
            new FakeWebHostEnvironment(root));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "threadline-build21-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "Threadline.Service.Tests";
        public IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }
}
