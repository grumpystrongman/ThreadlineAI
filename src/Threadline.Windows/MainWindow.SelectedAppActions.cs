using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private async void AttachSelectedWindow_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            EnsureSession();
            if (OpenWindowsList.SelectedItem is not ActiveWindowSnapshot selected)
            {
                throw new InvalidOperationException("Select an open app window first.");
            }

            _selectedTargetWindow = selected;
            _lastForegroundWindow = selected;
            _attachment = await _client.AttachWindowAsync(_session!.Id, selected);
            CurrentWindowText.Text = FormatAttachment(_attachment);
            AddTimeline($"Attached selected target {_attachment.Snapshot.ApplicationName}: {_attachment.Snapshot.WindowTitle}");
        });
    }

    private async void CaptureSelectedWindow_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() =>
        {
            EnsureSession();
            if (OpenWindowsList.SelectedItem is not ActiveWindowSnapshot selected)
            {
                throw new InvalidOperationException("Select an open app window first.");
            }

            _selectedTargetWindow = selected;
            _lastForegroundWindow = selected;
            CurrentWindowText.Text = "Selected target:\n" + selected.ToDisplayText();
            _lastNativeUiResult = _nativeUiAutomationReader.ReadWindow(selected.Handle);
            _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
            AppendTranscript("Selected App Summary", _lastContextSummary.ToPromptContext());
            AddTimeline(_lastNativeUiResult.Success ? "Summarized selected app context." : "Selected app capture found no readable context.");
            return Task.CompletedTask;
        });
    }
}
