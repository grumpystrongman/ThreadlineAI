using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Threadline.Core;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class LocalServiceApiContractTests : IClassFixture<LocalServiceApiContractTests.ThreadlineApiFactory>
{
    private readonly ThreadlineApiFactory _factory;

    public LocalServiceApiContractTests(ThreadlineApiFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthDoctorCapabilitiesAndActionsExposeStableContracts()
    {
        using var client = _factory.CreateClient();

        var health = await client.GetAsync("/health");
        var doctor = await client.GetAsync("/doctor");
        var capabilities = await client.GetAsync("/capabilities");
        var actions = await client.GetAsync("/actions");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, doctor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, actions.StatusCode);

        using var healthJson = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.Equal("ok", GetString(healthJson.RootElement, "status"));
        Assert.Equal("Threadline.Service", GetString(healthJson.RootElement, "service"));

        using var doctorJson = JsonDocument.Parse(await doctor.Content.ReadAsStringAsync());
        Assert.True(doctorJson.RootElement.TryGetProperty("readiness", out _));
        Assert.True(doctorJson.RootElement.TryGetProperty("checks", out _));
        Assert.True(doctorJson.RootElement.TryGetProperty("capabilities", out _));
        Assert.True(doctorJson.RootElement.TryGetProperty("actions", out _));

        using var actionJson = JsonDocument.Parse(await actions.Content.ReadAsStringAsync());
        Assert.Contains(actionJson.RootElement.EnumerateArray(), item => GetString(item, "id") == "provider.test");
    }

    [Fact]
    public async Task SessionBootstrapPreviewAndPromptContractsWorkTogether()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/sessions", new StartSessionRequest("Build 23 contract session", "OpenAI"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = GetString(createJson.RootElement, "id");
        Assert.StartsWith("ses_", sessionId);

        var active = await client.GetAsync("/sessions/active");
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);

        var preview = await client.PostAsJsonAsync($"/sessions/{sessionId}/events/preview", new AppendContextEventRequest(
            ContextSource.Manual,
            "release-note",
            "Build 23 verifies service contracts before release.",
            UserApproved: true));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        var append = await client.PostAsJsonAsync($"/sessions/{sessionId}/events", new AppendContextEventRequest(
            ContextSource.Manual,
            "release-note",
            "Build 23 stores approved context before prompt composition.",
            UserApproved: true));
        Assert.Equal(HttpStatusCode.Accepted, append.StatusCode);

        var prompt = await client.PostAsJsonAsync($"/sessions/{sessionId}/prompt", new ComposePromptRequest("What changed in Build 23?"));
        Assert.Equal(HttpStatusCode.OK, prompt.StatusCode);
        using var promptJson = JsonDocument.Parse(await prompt.Content.ReadAsStringAsync());
        var messages = promptJson.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Contains(messages, item => GetString(item, "role") == "user" && GetString(item, "content").Contains("Build 23 stores approved context", StringComparison.Ordinal));
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value)) return value.GetString() ?? string.Empty;
        var pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return element.TryGetProperty(pascal, out value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    public sealed class ThreadlineApiFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "threadline-build23-api", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_root);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Threadline:RequireApiToken"] = "false",
                    ["Threadline:DatabasePath"] = Path.Combine(_root, "threadline.db"),
                    ["Threadline:LocalDataRoot"] = _root,
                    ["Threadline:ApiTokenPath"] = Path.Combine(_root, "service-token.txt"),
                    ["Threadline:SecretStorePath"] = Path.Combine(_root, "secrets"),
                    ["Threadline:BuildChannel"] = "build-23-test"
                });
            });
        }

        public new void Dispose()
        {
            base.Dispose();
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Test host shutdown can lag SQLite pool cleanup on Windows; leaving a temp folder is safer than hiding contract failures.
            }
        }
    }
}