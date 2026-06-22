using System.Security.Cryptography;

namespace Threadline.Service;

public static class LocalApiTokenStore
{
    private const int TokenByteLength = 32;

    public static string DefaultTokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI",
        "service-token.txt");

    public static string GetOrCreateToken(string? tokenPath = null)
    {
        var path = string.IsNullOrWhiteSpace(tokenPath) ? DefaultTokenPath : tokenPath;
        var existing = ReadToken(path);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenByteLength));
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, token);
        File.Move(temporaryPath, path, overwrite: true);
        TryHardenFile(path);
        return token;
    }

    public static string? TryReadToken(string? tokenPath = null) => ReadToken(string.IsNullOrWhiteSpace(tokenPath) ? DefaultTokenPath : tokenPath);

    private static string? ReadToken(string path)
    {
        if (!File.Exists(path)) return null;
        var token = File.ReadAllText(path).Trim();
        return token.Length < 32 ? null : token;
    }

    private static void TryHardenFile(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch
        {
            // Best-effort only. The bearer token still prevents unauthenticated local API calls.
        }
    }
}
