namespace Threadline.Windows.Services;

public sealed class ActiveWindowDiagnostics
{
    private readonly NativeUiAutomationReader _nativeUiAutomationReader = new();

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
