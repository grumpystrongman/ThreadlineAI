namespace Threadline.Windows.Services;

public sealed class ActiveWindowContentResolver
{
    private readonly BrowserExtensionContextProvider _browserProvider = new();
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly ContextSummarizer _contextSummarizer = new();

    public async Task<SummarizedContext> ResolveAsync(string sessionId, ThreadlineTarget target, CancellationToken cancellationToken = default)
    {
        if (target.Kind == ThreadlineTargetKind.BrowserTab)
        {
            var browser = await _browserProvider.TryGetLatestAsync(sessionId, target, cancellationToken);
            return browser is null
                ? MissingProviderContext(target, "No browser page data is available in this session yet.", "Use the browser extension to send the page or selected text to ThreadlineAI.")
                : AddRoute(browser, target, "browser-extension", "app-specific provider", "high");
        }

        if (target.ProviderKey.Equals("notepad-tabs", StringComparison.OrdinalIgnoreCase))
        {
            return NotepadNeedsProviderContext(target);
        }

        if (!target.CanReadBody)
        {
            return MissingProviderContext(target, target.Guidance, $"Provider confidence: {target.Confidence}.");
        }

        var nativeResult = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
        return AddRoute(_contextSummarizer.SummarizeNativeUi(nativeResult), target, "native-ui", "generic fallback", nativeResult.Success ? "medium" : "low");
    }

    private static SummarizedContext NotepadNeedsProviderContext(ThreadlineTarget target) =>
        new(
            target.Title,
            "notepad-active-document-needed",
            "Threadline can identify this Notepad tab, but active document body capture is not implemented yet.",
            [target.ToString(), "Resolver route: app-specific provider required", "Provider selected: notepad-active-document-needed", "Confidence: title-only"],
            ["Generic native UI capture is tab-ambiguous for modern Notepad."],
            target.Window.ToDisplayText());

    private static SummarizedContext MissingProviderContext(ThreadlineTarget target, string summary, string warning) =>
        new(
            target.Title,
            target.ProviderKey,
            summary,
            [target.ToString(), $"Resolver route: {target.ProviderKey}", $"Confidence: {target.Confidence}"],
            [warning],
            target.Window.ToDisplayText());

    private static SummarizedContext AddRoute(SummarizedContext context, ThreadlineTarget target, string provider, string route, string confidence)
    {
        var details = new List<string> { $"Resolver route: {route}", $"Provider selected: {provider}", $"Confidence: {confidence}", $"Target: {target}" };
        details.AddRange(context.KeyDetails);
        return new SummarizedContext(context.Title, context.Source, context.Summary, details, context.Warnings, context.RawContent);
    }
}
