using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Threadline.Core;

namespace Threadline.Infrastructure.Security;

public sealed class DpapiProtectedSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ThreadlineAI.SecretStore.v1");
    private readonly string _rootDirectory;

    public DpapiProtectedSecretStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreadlineAI", "secrets")
            : rootDirectory;
    }

    public async Task<SecretDescriptor> SetSecretAsync(string name, string secretValue, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);

        Directory.CreateDirectory(_rootDirectory);
        var normalizedName = NormalizeName(name);
        var reference = ToReference(normalizedName);
        var path = ToPath(reference);
        var now = DateTimeOffset.UtcNow;
        var existing = File.Exists(path) ? await ReadEnvelopeAsync(path, cancellationToken) : null;
        var descriptor = new SecretDescriptor(reference, normalizedName, SecretProtectionKind.WindowsDpapiCurrentUser, existing?.CreatedAt ?? now, existing is null ? null : now, metadata);
        var protectedBytes = Protect(Encoding.UTF8.GetBytes(secretValue));
        var envelope = new SecretEnvelope(descriptor.Reference, descriptor.Name, descriptor.ProtectionKind, descriptor.CreatedAt, descriptor.UpdatedAt, metadata, Convert.ToBase64String(protectedBytes));

        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, path, overwrite: true);
        return descriptor;
    }

    public async Task<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = ToPath(reference);
        if (!File.Exists(path))
        {
            return null;
        }

        var envelope = await ReadEnvelopeAsync(path, cancellationToken);
        var secretBytes = Unprotect(Convert.FromBase64String(envelope.ProtectedValue));
        return Encoding.UTF8.GetString(secretBytes);
    }

    public async Task<SecretDescriptor?> DescribeSecretAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = ToPath(reference);
        if (!File.Exists(path))
        {
            return null;
        }

        var envelope = await ReadEnvelopeAsync(path, cancellationToken);
        return new SecretDescriptor(envelope.Reference, envelope.Name, envelope.ProtectionKind, envelope.CreatedAt, envelope.UpdatedAt, envelope.Metadata);
    }

    public Task<bool> DeleteSecretAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = ToPath(reference);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<int> DeleteAllSecretsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return Task.FromResult(0);
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_rootDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
            deleted++;
        }

        return Task.FromResult(deleted);
    }

    private static string ToReference(string normalizedName) => $"secret://local/{normalizedName}";

    private string ToPath(string reference)
    {
        if (!reference.StartsWith("secret://local/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Secret reference must start with secret://local/.", nameof(reference));
        }

        var name = reference["secret://local/".Length..];
        var safeName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        return Path.Combine(_rootDirectory, safeName + ".json");
    }

    private static string NormalizeName(string name) =>
        string.Join('/', name.Trim().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static async Task<SecretEnvelope> ReadEnvelopeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SecretEnvelope>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Secret envelope is empty or invalid.");
    }

    private static byte[] Protect(byte[] value) => InvokeProtectedData("Protect", value);
    private static byte[] Unprotect(byte[] value) => InvokeProtectedData("Unprotect", value);

    private static byte[] InvokeProtectedData(string methodName, byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI protected secret storage requires Windows.");
        }

        var protectedDataType = Type.GetType("System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData")
            ?? Type.GetType("System.Security.Cryptography.ProtectedData")
            ?? throw new PlatformNotSupportedException("System.Security.Cryptography.ProtectedData is not available in this runtime.");
        var scopeType = Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security.Cryptography.ProtectedData")
            ?? Type.GetType("System.Security.Cryptography.DataProtectionScope")
            ?? throw new PlatformNotSupportedException("System.Security.Cryptography.DataProtectionScope is not available in this runtime.");
        var currentUser = Enum.Parse(scopeType, "CurrentUser");
        var method = protectedDataType.GetMethod(methodName, [typeof(byte[]), typeof(byte[]), scopeType])
            ?? throw new MissingMethodException(protectedDataType.FullName, methodName);

        return (byte[])method.Invoke(null, [value, Entropy, currentUser])!;
    }

    private sealed record SecretEnvelope(
        string Reference,
        string Name,
        SecretProtectionKind ProtectionKind,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt,
        IReadOnlyDictionary<string, string>? Metadata,
        string ProtectedValue);
}
