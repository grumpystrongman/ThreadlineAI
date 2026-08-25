using Microsoft.Extensions.Configuration;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class JarvisRuntimeOptionsTests
{
    [Fact]
    public void FromConfiguration_DefaultsToEnabledLoopbackRuntime()
    {
        var options = JarvisRuntimeOptions.FromConfiguration(BuildConfig(new Dictionary<string, string?>()));

        Assert.True(options.Enabled);
        Assert.Equal("127.0.0.1", options.BaseAddress.Host);
        Assert.False(options.AllowRemoteRuntime);
    }

    [Fact]
    public void FromConfiguration_RejectsRemoteRuntimeByDefault()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:Jarvis:BaseAddress"] = "https://jarvis.example.com:47821/"
        });

        var error = Assert.Throws<InvalidOperationException>(() => JarvisRuntimeOptions.FromConfiguration(config));
        Assert.Contains("AllowRemoteRuntime", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_AllowsRemoteRuntimeOnlyWhenExplicitlyEnabled()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:Jarvis:BaseAddress"] = "https://jarvis.example.com:47821/",
            ["Threadline:Jarvis:AllowRemoteRuntime"] = "true"
        });

        var options = JarvisRuntimeOptions.FromConfiguration(config);
        Assert.True(options.AllowRemoteRuntime);
        Assert.Equal("jarvis.example.com", options.BaseAddress.Host);
    }

    [Fact]
    public void FromConfiguration_RejectsUnsupportedUriScheme()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:Jarvis:BaseAddress"] = "file:///c:/temp/jarvis"
        });

        Assert.Throws<InvalidOperationException>(() => JarvisRuntimeOptions.FromConfiguration(config));
    }

    [Fact]
    public void FromConfiguration_ClampsTimeout()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:Jarvis:RequestTimeoutSeconds"] = "9999"
        });

        var options = JarvisRuntimeOptions.FromConfiguration(config);
        Assert.Equal(TimeSpan.FromSeconds(300), options.RequestTimeout);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
