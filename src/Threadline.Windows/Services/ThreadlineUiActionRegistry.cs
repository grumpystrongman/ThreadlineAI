namespace Threadline.Windows.Services;

public sealed class ThreadlineUiActionRegistry
{
    private readonly Dictionary<string, ThreadlineUiActionRegistration> _actions = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string id, string displayName, Func<Task> handler, string? capabilityId = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Action id is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(handler);
        _actions[id.Trim()] = new ThreadlineUiActionRegistration(id.Trim(), displayName.Trim(), capabilityId, handler);
    }

    public Task ExecuteAsync(string id)
    {
        if (!_actions.TryGetValue(id, out var action))
        {
            throw new InvalidOperationException($"Threadline action '{id}' is not registered.");
        }

        return action.Handler();
    }

    public IReadOnlyList<ThreadlineUiActionRegistration> List() =>
        _actions.Values.OrderBy(action => action.DisplayName).ToArray();
}

public sealed record ThreadlineUiActionRegistration(
    string Id,
    string DisplayName,
    string? CapabilityId,
    Func<Task> Handler);
