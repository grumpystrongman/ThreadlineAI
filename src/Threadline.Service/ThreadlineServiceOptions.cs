using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Threadline.Service;

public sealed record ThreadlineServiceOptions(
    bool RequireApiToken,
    string? ApiToken,
    int MaxContextCharacters,
    int MaxSessionNameCharacters)
{
    public static ThreadlineServiceOptions FromConfiguration(IConfiguration configuration)
    {
        var apiToken = FirstNonBlank(
            configuration["Threadline:ApiToken"],
            Environment.GetEnvironmentVariable("THREADLINE_API_TOKEN"));

        var requireApiToken = ParseBool(configuration["Threadline:RequireApiToken"]) ?? !string.IsNullOrWhiteSpace(apiToken);
        var maxContextCharacters = ParsePositiveInt(configuration["Threadline:MaxContextCharacters"]) ?? 200_000;
        var maxSessionNameCharacters = ParsePositiveInt(configuration["Threadline:MaxSessionNameCharacters"]) ?? 120;

        return new ThreadlineServiceOptions(requireApiToken, apiToken, maxContextCharacters, maxSessionNameCharacters);
    }

    public bool IsAuthorized(HttpRequest request)
    {
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

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool? ParseBool(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;

    private static int? ParsePositiveInt(string? value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
}
