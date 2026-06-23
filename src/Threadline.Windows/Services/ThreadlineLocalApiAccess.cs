namespace Threadline.Windows.Services;

internal static class ThreadlineLocalApiAccess
{
    public const string TokenHeaderName = "X-Threadline-Token";

    public static string DefaultTokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI",
        "service-token.txt");

    public static void ApplyTo(HttpClient httpClient, string? localAccessToken = null)
    {
        var token = ResolveToken(localAccessToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (!httpClient.DefaultRequestHeaders.Contains(TokenHeaderName))
        {
            httpClient.DefaultRequestHeaders.Add(TokenHeaderName, token);
        }
    }

    public static string? ResolveToken(string? localAccessToken = null)
    {
        if (!string.IsNullOrWhiteSpace(localAccessToken))
        {
            return localAccessToken.Trim();
        }

        return TryLoadToken();
    }

    public static string? TryLoadToken(string? tokenPath = null)
    {
        var path = string.IsNullOrWhiteSpace(tokenPath) ? DefaultTokenPath : tokenPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var token = File.ReadAllText(path).Trim();
            return token.Length >= 32 ? token : null;
        }
        catch
        {
            return null;
        }
    }
}
