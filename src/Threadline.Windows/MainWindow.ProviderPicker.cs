using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly BrowserExtensionContextProvider _browserProvider = new();

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

            if (selected.Kind == ThreadlineTargetKind.BrowserTab)
            {
                var summary = await _browserProvider.TryGetLatestAsync(_session!.Id, selected);
                _lastContextSummary = summary ?? new SummarizedContext(
                    selected.Title,
                    selected.ProviderKey,
                    "No page data is available in this session yet.",
                    [selected.ToString()],
                    ["Page data is missing."],
                    selected.Window.ToDisplayText());
                AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
                return;
            }

            if (!selected.CanReadBody)
            {
                _lastContextSummary = new SummarizedContext(selected.Title, selected.ProviderKey, selected.Guidance, [selected.ToString()], [$"Provider confidence: {selected.Confidence}."], selected.Window.ToDisplayText());
                AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
                return;
            }

            _lastNativeUiResult = _nativeUiAutomationReader.ReadWindow(selected.Window.Handle);
            _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
            AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
        });
    }
}
