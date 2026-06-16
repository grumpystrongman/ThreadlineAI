using Threadline.Core;

namespace Threadline.Infrastructure.Security;

public sealed class SecretService
{
    private readonly ISecretStore _secretStore;
    private readonly IAuditRepository? _auditRepository;
    private readonly IClock _clock;

    public SecretService(ISecretStore secretStore, IClock clock, IAuditRepository? auditRepository = null)
    {
        _secretStore = secretStore;
        _clock = clock;
        _auditRepository = auditRepository;
    }

    public async Task<SecretDescriptor> SetAsync(string name, string secretValue, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        var descriptor = await _secretStore.SetSecretAsync(name, secretValue, metadata, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(null, AuditEventType.SecretStored, _clock.UtcNow, $"Secret stored: {descriptor.Name}", ToAuditMetadata(descriptor)), cancellationToken);
        return descriptor;
    }

    public async Task<string?> GetValueAsync(string reference, CancellationToken cancellationToken = default)
    {
        var value = await _secretStore.GetSecretAsync(reference, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(null, AuditEventType.SecretRead, _clock.UtcNow, "Secret value resolved for local use.", new Dictionary<string, string> { ["reference"] = reference, ["found"] = (value is not null).ToString() }), cancellationToken);
        return value;
    }

    public Task<SecretDescriptor?> DescribeAsync(string reference, CancellationToken cancellationToken = default) =>
        _secretStore.DescribeSecretAsync(reference, cancellationToken);

    public async Task<bool> DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var deleted = await _secretStore.DeleteSecretAsync(reference, cancellationToken);
        await AppendAuditAsync(AuditEvent.Create(null, AuditEventType.SecretDeleted, _clock.UtcNow, "Secret deleted.", new Dictionary<string, string> { ["reference"] = reference, ["deleted"] = deleted.ToString() }), cancellationToken);
        return deleted;
    }

    private static IReadOnlyDictionary<string, string> ToAuditMetadata(SecretDescriptor descriptor) => new Dictionary<string, string>
    {
        ["reference"] = descriptor.Reference,
        ["name"] = descriptor.Name,
        ["protectionKind"] = descriptor.ProtectionKind.ToString()
    };

    private Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        _auditRepository is null ? Task.CompletedTask : _auditRepository.AppendAuditEventAsync(auditEvent, cancellationToken);
}
