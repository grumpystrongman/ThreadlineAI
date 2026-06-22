using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Threadline.Service;

public sealed record ThreadlineServiceOptions(
    bool RequireApiToken,
    string? ApiToken,
    string ApiTokenPath,
    int MaxContextCharacters,
    int MaxSessionNameCharacters,
    int RetentionDays,
    bool LocalOnlyMode,
    IReadOnlySet<string> CorsAllowedOrigins)
{
    public static ThreadlineServiceOptions FromConfiguration(IConfiguration configuration)
    {
        var apiTokenPath = FirstNonBlank(
            configuration["Threadline:ApiTokenPath"],
            Environment.GetEnvironmentVariable("THREADLINE_API_TOKEN_PATH")) ?? LocalApiTokenStore.DefaultTokenPath;

        var configuredToken = FirstNonBlank(
            configuration["Threadline:ApiToken"],
            Environment.GetEnvironmentVariable("THREADLINE_API_TOKEN"));

        // Build 20 hardening: the local API is token-protected by default. Development can explicitly opt out,
        // but a missing token no longer means "open local API".
        var requireApiToken = ParseBool(configuration["Threadline:RequireApiToken"])
            ?? ParseBool(Environment.GetEnvironmentVariable("THREADLINE_REQUIRE_API_TOKEN"))
            ?? true;

        var apiToken = requireApiToken
            ? FirstNonBlank(configuredToken, LocalApiTokenStore.GetOrCreateToken(apiTokenPath))
            : configuredToken;

        var maxContextCharacters = ParsePositiveInt(configuration["Threadline:MaxContextCharacters"]) ?? 200_000;
        var maxSessionNameCharacters = ParsePositiveInt(configuration["Threadline:MaxSessionNameCharacters"]) ?? 120;
        var retentionDays = ParsePositiveInt(configuration["Threadline:RetentionDays"]) ?? 30;
        var localOnlyMode = ParseBool(configuration["Threadline:LocalOnlyMode"])
            ?? ParseBool(Environment.GetEnvironmentVariable("THREADLINE_LOCAL_ONLY_MODE"))
            ?? false;
        var corsAllowedOrigins = ParseOrigins(FirstNonBlank(
            configuration["Threadline:CorsAllowedOrigins"],
            Environment.GetEnvironmentVariable("THREADLINE_CORS_ALLOWED_ORIGINS")));

        return new ThreadlineServiceOptions(
            requireApiToken,
            apiToken,
            apiTokenPath,
            maxContextCharacters,
            maxSessionNameCharacters,
            retentionDays,
            localOnlyMode,
            corsAllowedOrigins);
    }

    public bool IsAuthorized(HttpRequest request)
    {
        if (!IsLoopbackRequest(request))
        {
            return false;
        }

        if (!RequireApiToken)
        {
            return true;
        }

        var suppliedToken = request.Headers["X-Threadline-Token"].ToString();
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            var authorization = request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";
            if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                suppliedToken = authorization[bearerPrefix.Length..].Trim();
            }
        }

        return SecureEquals(suppliedToken, ApiToken);
    }

    public bool IsCorsOriginAllowed(string origin) => CorsAllowedOrigins.Contains(origin);

    private static bool IsLoopbackRequest(HttpRequest request)
    {
        var remoteIp = request.HttpContext.Connection.RemoteIpAddress;
        return remoteIp is null || IPAddress.IsLoopback(remoteIp);
    }

    private static bool SecureEquals(string? supplied, string? expected)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static IReadOnlySet<string> ParseOrigins(string? value)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return origins;

        foreach (var origin in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (origin == "*")
            {
                throw new InvalidOperationException("Threadline:CorsAllowedOrigins cannot contain '*'. Build 20 requires explicit origins only.");
            }

            origins.Add(origin.TrimEnd('/'));
        }

        return origins;
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool? ParseBool(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;

    private static int? ParsePositiveInt(string? value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
}
