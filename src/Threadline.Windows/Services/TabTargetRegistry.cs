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
    public string DisplayLabel => BuildDisplayLabel();

    public override string ToString() => DisplayLabel;

    private string BuildDisplayLabel()
    {
        if (Kind == ThreadlineTargetKind.Window)
        {
            return $"▸ {Window.ApplicationName} — {Title}  [Native window]";
        }

        var providerBadge = ProviderKey switch
        {
            "browser-extension" => "Browser extension",
            "notepad-tabs" => "Notepad tab resolver",
            "native-ui" => "Native UI",
            _ => ProviderKey
        };

        var state = CanReadBody || ProviderKey.Equals("browser-extension", StringComparison.OrdinalIgnoreCase)
            ? "ready"
            : "needs resolver";
        var icon = Kind switch
        {
            ThreadlineTargetKind.BrowserTab => "🌐",
            ThreadlineTargetKind.Tab => "↳",
            ThreadlineTargetKind.Document => "📄",
            ThreadlineTargetKind.ShellTab => "⌁",
            _ => "↳"
        };

        return $"   {icon} {Title}  [{providerBadge}; {state}]";
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
