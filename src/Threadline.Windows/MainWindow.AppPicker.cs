using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly OpenWindowCatalog _openWindowCatalog = new();
    private readonly AppCaptureRouter _appCaptureRouter = new();
    private ActiveWindowSnapshot? _selectedTargetWindow;

    private void RefreshOpenWindows_Click(object sender, RoutedEventArgs e)
    {
        LoadOpenWindows();
    }

    private async void UseSelectedWindow_Click(object sender, RoutedEventArgs e)
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
            CurrentWindowText.Text = "Selected target:\n" + selected.ToDisplayText();
            _attachment = await _client.AttachWindowAsync(_session!.Id, selected);

            var plan = _appCaptureRouter.PlanFor(selected);
            AppendTranscript("Capture Plan", $"Provider: {plan.DisplayName}\nCan capture now: {plan.CanCaptureNow}\nDescription: {plan.Description}\nGuidance: {plan.Guidance}");

            if (plan.CanCaptureNow)
            {
                _lastNativeUiResult = _nativeUiAutomationReader.ReadWindow(selected.Handle);
                _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
                AppendTranscript("Selected App Preview", _lastContextSummary.ToPromptContext());
                AddTimeline($"Selected and captured {selected.ApplicationName}: {selected.WindowTitle}");
            }
            else
            {
                _lastContextSummary = new SummarizedContext(
                    selected.WindowTitle ?? selected.ApplicationName,
                    plan.DisplayName,
                    plan.Guidance,
                    [selected.ToString()],
                    ["Capture provider is planned but not implemented yet."],
                    selected.ToDisplayText());
                AppendTranscript("Selected App Preview", _lastContextSummary.ToPromptContext());
                AddTimeline($"Selected {selected.ApplicationName}; provider required: {plan.DisplayName}");
            }
        });
    }

    private void LoadOpenWindows()
    {
        var windows = _openWindowCatalog.ListOpenWindows();
        OpenWindowsList.Items.Clear();
        foreach (var window in windows)
        {
            OpenWindowsList.Items.Add(window);
        }

        AddTimeline($"Found {windows.Count} open app window(s).");
    }
}
