using System.Collections.Concurrent;
using Threadline.Core;

namespace Threadline.Infrastructure.Security;

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, StoredSecret> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public Task<SecretDescriptor> SetSecretAsync(string name, string secretValue, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretValue);

        var normalizedName = NormalizeName(name);
        var reference = ToReference(normalizedName);
        var now = DateTimeOffset.UtcNow;
        var descriptor = _secrets.TryGetValue(reference, out var existing)
            ? existing.Descriptor with { UpdatedAt = now, Metadata = metadata }
            : new SecretDescriptor(reference, normalizedName, SecretProtectionKind.InMemory, now, null, metadata);

        _secrets[reference] = new StoredSecret(secretValue, descriptor);
        return Task.FromResult(descriptor);
    }

    public Task<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryGetValue(reference, out var secret) ? secret.Value : null);

    public Task<SecretDescriptor?> DescribeSecretAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryGetValue(reference, out var secret) ? secret.Descriptor : null);

    public Task<bool> DeleteSecretAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryRemove(reference, out _));

    private static string ToReference(string normalizedName) => $"secret://memory/{normalizedName}";

    private static string NormalizeName(string name) =>
        string.Join('/', name.Trim().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record StoredSecret(string Value, SecretDescriptor Descriptor);
}
