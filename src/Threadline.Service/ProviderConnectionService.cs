using Threadline.Core;

namespace Threadline.Service;

public sealed class ProviderConnectionService
{
    private readonly IProviderConnectionRepository _providers;
    private readonly IClock _clock;
    private readonly IAuditRepository? _audit;

    public ProviderConnectionService(
        IProviderConnectionRepository providers,
        IClock clock,
        IAuditRepository? audit = null)
    {
        _providers = providers;
        _clock = clock;
        _audit = audit;
    }

    public Task<IReadOnlyList<ProviderConnection>> ListAsync(CancellationToken cancellationToken = default) =>
        _providers.ListProviderConnectionsAsync(cancellationToken);

    public Task<ProviderConnection?> GetAsync(string providerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Task.FromResult<ProviderConnection?>(null);
        }

        return _providers.GetProviderConnectionAsync(providerName.Trim(), cancellationToken);
    }

    public async Task<ProviderConnection> SaveAsync(ProviderConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var normalized = connection with
        {
            ProviderName = connection.ProviderName.Trim(),
            UpdatedAt = _clock.UtcNow
        };

        await _providers.SaveProviderConnectionAsync(normalized, cancellationToken);
        await AppendProviderConfiguredAuditAsync(normalized, cancellationToken);
        return normalized;
    }

    private Task AppendProviderConfiguredAuditAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return Task.CompletedTask;
        }

        var metadata = new Dictionary<string, string>
        {
            ["provider"] = connection.ProviderName,
            ["authType"] = connection.AuthType.ToString(),
            ["status"] = connection.Status.ToString(),
            ["hasCredentialReference"] = (!string.IsNullOrWhiteSpace(connection.CredentialReference)).ToString(),
            ["hasBaseUrl"] = (!string.IsNullOrWhiteSpace(connection.BaseUrl)).ToString(),
            ["hasDefaultModel"] = (!string.IsNullOrWhiteSpace(connection.DefaultModel)).ToString()
        };

        if (connection.Metadata is not null)
        {
            foreach (var pair in connection.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !metadata.ContainsKey(pair.Key))
                {
                    metadata[$"provider.{pair.Key}"] = pair.Value;
                }
            }
        }

        return _audit.AppendAuditEventAsync(
            AuditEvent.Create(
                null,
                AuditEventType.ProviderConfigured,
                _clock.UtcNow,
                $"Provider configured: {connection.ProviderName}",
                metadata),
            cancellationToken);
    }
}
