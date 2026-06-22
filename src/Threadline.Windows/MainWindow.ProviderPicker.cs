using Microsoft.UI.Xaml;
using Threadline.Windows.Services;
using System.Threading.Tasks;

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
            _lastFollowTarget = selected;
            CurrentWindowText.Text = $"Selected target:\n{selected}\n\n{selected.Window.ToDisplayText()}";
            PlaceSidecarForTarget(selected, "Selected target attached.");
            _attachment = await _client.AttachWindowAsync(_session!.Id, selected.Window);
            _lastContextSummary = await _contentResolver.ResolveAsync(_session!.Id, selected);
            UpdateCurrentContextPanel(_lastContextSummary);
            AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
            AddTimeline($"Selected target {selected.Title}; context source: {_lastContextSummary.Source}");
        });
    }

    private async void ToggleDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (!IsDrawerOpenFor(DiagnosticsTargetPickerPanel))
        {
            OpenDiagnosticsTargetPickerPanel();
            LoadOpenWindows();
            DiagnosticsPanel.Visibility = Visibility.Visible;
            DiagnosticsText.Text = "Gathering diagnostics...";
            await ShowProductDiagnosticsAsync();
            return;
        }

        DiagnosticsPanel.Visibility = DiagnosticsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        // If we just showed the diagnostics panel, refresh diagnostics.
        if (DiagnosticsPanel.Visibility == Visibility.Visible)
        {
            DiagnosticsText.Text = "Gathering diagnostics...";
            await ShowProductDiagnosticsAsync();
        }
    }

    private void UpdateCurrentContextPanel(SummarizedContext context)
    {
        var status = BuildContextStatus(context);
        ContextStatusText.Text = status;
        CurrentContextText.Text = $"Source: {context.Source} • Confidence: {context.Confidence}\nSummary: {context.Summary}";
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

        if (source.Contains("needed", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("provider", StringComparison.OrdinalIgnoreCase))
        {
            return $"Provider needed • {context.Confidence}";
        }

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

        if (context.Confidence == ContextConfidence.None)
        {
            return "No readable context";
        }

        return $"{context.Source} • {context.Confidence}";
    }
}
