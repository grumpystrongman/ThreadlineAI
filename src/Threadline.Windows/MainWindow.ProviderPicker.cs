using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ActiveWindowContentResolver _contentResolver = new();

    private async void UseSelectedTarget_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            EnsureSession();
            if (OpenWindowsList.SelectedItem is not ThreadlineTarget selected)
            {
                throw new InvalidOperationException("Select a target first.");
            }

            _selectedThreadlineTarget = selected;
            _selectedTargetWindow = selected.Window;
            _lastForegroundWindow = selected.Window;
            CurrentWindowText.Text = $"Selected target:\n{selected}\n\n{selected.Window.ToDisplayText()}";
            _attachment = await _client.AttachWindowAsync(_session!.Id, selected.Window);
            _lastContextSummary = await _contentResolver.ResolveAsync(_session!.Id, selected);
            UpdateCurrentContextPanel(_lastContextSummary);
            AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
        });
    }

    private void ToggleDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsPanel.Visibility = DiagnosticsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateCurrentContextPanel(SummarizedContext context)
    {
        var status = BuildContextStatus(context);
        ContextStatusText.Text = status;
        CurrentContextText.Text = $"Status:\n{status}\n\nSource:\n{context.Source}\n\nConfidence:\n{context.Confidence}\n\nSummary:\n{context.Summary}";
        DiagnosticsText.Text = context.Diagnostics?.ToDisplayText() ?? "No diagnostics are available for the current context.";
    }

    private void ResetCurrentContextPanel()
    {
        ContextStatusText.Text = "No context";
        CurrentContextText.Text = "No resolved context yet. Select a target and click Use.";
        DiagnosticsText.Text = "No diagnostics yet.";
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
    }

    private static string BuildContextStatus(SummarizedContext context)
    {
        var source = context.Source ?? string.Empty;

        if (source.Contains("browser", StringComparison.OrdinalIgnoreCase))
        {
            return $"Browser • {context.Confidence}";
        }

        if (source.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            return $"File-backed • {context.Confidence}";
        }

        if (source.Contains("native", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("automation", StringComparison.OrdinalIgnoreCase))
        {
            return $"Native UI • {context.Confidence}";
        }

        if (source.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("ocr", StringComparison.OrdinalIgnoreCase))
        {
            return $"Screenshot required • {context.Confidence}";
        }

        if (source.Contains("needed", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("provider", StringComparison.OrdinalIgnoreCase))
        {
            return $"Provider needed • {context.Confidence}";
        }

        if (context.Confidence == ContextConfidence.None)
        {
            return "No readable context";
        }

        return $"{context.Source} • {context.Confidence}";
    }
}
