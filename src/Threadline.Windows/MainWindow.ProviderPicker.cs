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
            AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
        });
    }
}
