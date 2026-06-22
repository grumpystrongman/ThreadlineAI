namespace Threadline.Windows.Services;

public sealed class ActiveWindowDiagnostics
{
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();
    private readonly NativeWindowContextProvider _nativeWindowContextProvider = new();

    public IReadOnlyList<string> Inspect(ThreadlineTarget target)
    {
        var details = new List<string>
        {
            $"Diagnostic target: {target.Title}",
            $"Diagnostic app: {target.Window.ApplicationName}",
            $"Diagnostic process: {target.Window.ProcessName}",
            $"Diagnostic provider: {target.ProviderKey}",
            $"Diagnostic target kind: {target.Kind}",
            $"Diagnostic can read body: {target.CanReadBody}",
            $"Diagnostic confidence: {target.Confidence}"
        };

        var nativeContext = _nativeWindowContextProvider.Capture(target.Window);
        details.Add($"Native context provider: {nativeContext.ProviderName}");
        details.Add($"Native context level: {nativeContext.LevelDisplayName}");
        details.Add($"Native context content length: {nativeContext.Content.Length}");
        details.Add($"Native context guidance: {nativeContext.Guidance}");

        foreach (var metadata in nativeContext.Metadata.Take(8))
        {
            details.Add($"Native context metadata: {metadata.Key}={metadata.Value}");
        }

        foreach (var warning in nativeContext.Warnings.Take(5))
        {
            details.Add($"Native context warning: {warning}");
        }

        var native = _nativeUiAutomationReader.ReadWindow(target.Window.Handle);
        details.Add($"Native UI success: {native.Success}");
        details.Add($"Native UI source: {native.ProcessName}");
        details.Add($"Native UI text length: {native.Content.Length}");
        details.Add($"Native UI warning count: {native.Warnings.Count}");

        if (native.Success)
        {
            var lineCount = native.Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            details.Add($"Native UI line count: {lineCount}");
        }

        foreach (var warning in native.Warnings.Take(5))
        {
            details.Add($"Native warning: {warning}");
        }

        return details;
    }
}
