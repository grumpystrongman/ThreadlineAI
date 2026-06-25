using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Threadline.Service;

namespace Threadline.Service.Tests;

public sealed class ThreadlineServiceOptionsTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void FromConfiguration_ParsesBoolValues(string input, bool expected)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:RequireApiToken"] = input,
        });

        var options = ThreadlineServiceOptions.FromConfiguration(config);
        Assert.Equal(expected, options.RequireApiToken);
    }

    [Fact]
    public void FromConfiguration_DefaultsRequireApiTokenToTrue()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var options = ThreadlineServiceOptions.FromConfiguration(config);
        Assert.True(options.RequireApiToken);
    }

    [Fact]
    public void FromConfiguration_ParsesPositiveIntegers()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:RequireApiToken"] = "false",
            ["Threadline:MaxContextCharacters"] = "500",
            ["Threadline:MaxSessionNameCharacters"] = "50",
            ["Threadline:RetentionDays"] = "7",
        });

        var options = ThreadlineServiceOptions.FromConfiguration(config);
        Assert.Equal(500, options.MaxContextCharacters);
        Assert.Equal(50, options.MaxSessionNameCharacters);
        Assert.Equal(7, options.RetentionDays);
    }

    [Fact]
    public void FromConfiguration_FallsBackToDefaultsForInvalidIntegers()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:RequireApiToken"] = "false",
            ["Threadline:MaxContextCharacters"] = "not-a-number",
            ["Threadline:RetentionDays"] = "-5",
        });

        var options = ThreadlineServiceOptions.FromConfiguration(config);
        Assert.Equal(200_000, options.MaxContextCharacters);
        Assert.Equal(30, options.RetentionDays);
    }

    [Fact]
    public void FromConfiguration_ParsesCorsOrigins()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:RequireApiToken"] = "false",
            ["Threadline:CorsAllowedOrigins"] = "https://example.com, https://other.com/",
        });

        var options = ThreadlineServiceOptions.FromConfiguration(config);
        Assert.Contains("https://example.com", options.CorsAllowedOrigins);
        Assert.Contains("https://other.com", options.CorsAllowedOrigins);
        Assert.DoesNotContain("https://other.com/", options.CorsAllowedOrigins);
    }

    [Fact]
    public void FromConfiguration_RejectsWildcardCorsOrigin()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:RequireApiToken"] = "false",
            ["Threadline:CorsAllowedOrigins"] = "*",
        });

        Assert.Throws<InvalidOperationException>(() => ThreadlineServiceOptions.FromConfiguration(config));
    }

    [Fact]
    public void IsCorsOriginAllowed_ReturnsTrueForRegisteredOrigin()
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https://example.com" };
        var options = new ThreadlineServiceOptions(false, null, "/tmp/t", 200_000, 120, 30, false, origins);

        Assert.True(options.IsCorsOriginAllowed("https://example.com"));
        Assert.True(options.IsCorsOriginAllowed("HTTPS://EXAMPLE.COM"));
        Assert.False(options.IsCorsOriginAllowed("https://other.com"));
    }

    [Fact]
    public void IsAuthorized_AllowsLoopbackWithoutTokenWhenNotRequired()
    {
        var options = new ThreadlineServiceOptions(false, null, "/tmp/t", 200_000, 120, 30, false, new HashSet<string>());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        Assert.True(options.IsAuthorized(context.Request));
    }

    [Fact]
    public void IsAuthorized_RejectsNonLoopbackEvenWhenTokenNotRequired()
    {
        var options = new ThreadlineServiceOptions(false, null, "/tmp/t", 200_000, 120, 30, false, new HashSet<string>());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");

        Assert.False(options.IsAuthorized(context.Request));
    }

    [Fact]
    public void IsAuthorized_AcceptsBearerTokenFromAuthorizationHeader()
    {
        var token = "a-valid-token-that-is-long-enough";
        var options = new ThreadlineServiceOptions(true, token, "/tmp/t", 200_000, 120, 30, false, new HashSet<string>());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["Authorization"] = "Bearer " + token;

        Assert.True(options.IsAuthorized(context.Request));
    }

    [Fact]
    public void IsAuthorized_RejectsEmptySuppliedToken()
    {
        var token = "a-valid-token-that-is-long-enough";
        var options = new ThreadlineServiceOptions(true, token, "/tmp/t", 200_000, 120, 30, false, new HashSet<string>());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        Assert.False(options.IsAuthorized(context.Request));
    }

    [Fact]
    public void FromConfiguration_ParsesLocalOnlyMode()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Threadline:RequireApiToken"] = "false",
            ["Threadline:LocalOnlyMode"] = "true",
        });

        var options = ThreadlineServiceOptions.FromConfiguration(config);
        Assert.True(options.LocalOnlyMode);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
