using System.Collections.Concurrent;
using Threadline.Core;

namespace Threadline.Infrastructure;

public sealed class InMemoryAdapterRegistry : IAdapterRegistry
{
    private readonly ConcurrentDictionary<string, AdapterRegistration> _registrations = new();

    public Task<AdapterRegistration> RegisterAsync(AdapterRegistration registration, CancellationToken cancellationToken = default)
    {
        _registrations[registration.Id] = registration;
        return Task.FromResult(registration);
    }

    public Task<AdapterRegistration?> GetAsync(string adapterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_registrations.TryGetValue(adapterId, out var registration) ? registration : null);

    public Task<IReadOnlyList<AdapterRegistration>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AdapterRegistration>>(_registrations.Values.OrderBy(x => x.DisplayName).ToArray());

    public Task<AdapterRegistration?> MarkSeenAsync(string adapterId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_registrations.TryGetValue(adapterId, out var registration))
        {
            return Task.FromResult<AdapterRegistration?>(null);
        }

        var updated = registration.Seen(now);
        _registrations[adapterId] = updated;
        return Task.FromResult<AdapterRegistration?>(updated);
    }
}
