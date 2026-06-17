namespace Threadline.Windows.Services;

public enum ThreadlineTargetKind
{
    Window,
    Tab,
    Document,
    ShellTab,
    BrowserTab
}

public sealed record ThreadlineTarget(
    string Id,
    ThreadlineTargetKind Kind,
    ActiveWindowSnapshot Window,
    string Title,
    string ProviderKey,
    bool IsActive,
    bool CanReadBody,
    string Confidence,
    string Guidance)
{
    public override string ToString()
    {
        var marker = Kind == ThreadlineTargetKind.Window ? "Window" : Kind.ToString();
        var active = IsActive ? "active" : "available";
        return $"{Window.ApplicationName} [{marker}, {active}] — {Title}";
    }
}

public interface ITabProvider
{
    bool CanInspect(ActiveWindowSnapshot window);
    IReadOnlyList<ThreadlineTarget> GetTargets(ActiveWindowSnapshot window);
}

public sealed class TabTargetRegistry
{
    private readonly OpenWindowCatalog _windowCatalog = new();
    private readonly IReadOnlyList<ITabProvider> _providers =
    [
        new NotepadTabProvider(),
        new BrowserTabProvider()
    ];

    public IReadOnlyList<ThreadlineTarget> ListTargets()
    {
        var targets = new List<ThreadlineTarget>();
        foreach (var window in _windowCatalog.ListOpenWindows())
        {
            targets.Add(CreateWindowTarget(window));
            foreach (var provider in _providers.Where(provider => provider.CanInspect(window)))
            {
                targets.AddRange(provider.GetTargets(window));
            }
        }

        return targets
            .GroupBy(target => target.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static ThreadlineTarget CreateWindowTarget(ActiveWindowSnapshot window) =>
        new(
            $"window:{window.Handle}",
            ThreadlineTargetKind.Window,
            window,
            window.WindowTitle ?? window.ApplicationName,
            "native-ui",
            true,
            true,
            "medium",
            "Generic app window target. Threadline may use native UI fallback unless a better provider is available.");
}
