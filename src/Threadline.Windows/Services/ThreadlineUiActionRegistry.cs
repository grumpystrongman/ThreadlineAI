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

    public async Task ExecuteAsync(string id)
    {
        if (!_actions.TryGetValue(id, out var action))
        {
            throw new ThreadlineUiActionException(id, $"Threadline action '{id}' is not registered.");
        }

        try
        {
            await action.Handler();
        }
        catch (ThreadlineUiActionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ThreadlineUiActionException(action.Id, $"Action '{action.DisplayName}' failed: {ex.Message}", ex);
        }
    }

    public IReadOnlyList<ThreadlineUiActionRegistration> List() =>
        _actions.Values.OrderBy(action => action.DisplayName).ToArray();
}

public sealed record ThreadlineUiActionRegistration(
    string Id,
    string DisplayName,
    string? CapabilityId,
    Func<Task> Handler);

public sealed class ThreadlineUiActionException : InvalidOperationException
{
    public ThreadlineUiActionException(string actionId, string message, Exception? innerException = null)
        : base(message, innerException) => ActionId = actionId;

    public string ActionId { get; }
}
