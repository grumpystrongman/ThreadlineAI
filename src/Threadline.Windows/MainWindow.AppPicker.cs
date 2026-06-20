using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly TabTargetRegistry _tabTargetRegistry = new();
    private readonly AppCaptureRouter _appCaptureRouter = new();
    private ActiveWindowSnapshot? _selectedTargetWindow;
    private ThreadlineTarget? _selectedThreadlineTarget;

    private void RefreshOpenWindows_Click(object sender, RoutedEventArgs e)
    {
        LoadOpenWindows();
    }

    private async void UseSelectedTarget_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            EnsureSession();
            if (OpenWindowsList.SelectedItem is not ThreadlineTarget selected)
            {
                throw new InvalidOperationException("Select an open app or tab first.");
            }

            _selectedThreadlineTarget = selected;
            _selectedTargetWindow = selected.Window;
            _lastForegroundWindow = selected.Window;
            _lastFollowTarget = selected;
            CurrentWindowText.Text = $"Selected target:\n{selected}\n\n{selected.Window.ToDisplayText()}";
            PlaceSidecarForTarget(selected, "Selected target attached.");
            _attachment = await _client.AttachWindowAsync(_session!.Id, selected.Window);

            if (!selected.CanReadBody)
            {
                _lastContextSummary = new SummarizedContext(
                    selected.Title,
                    selected.ProviderKey,
                    selected.Guidance,
                    [selected.ToString()],
                    [$"Provider confidence: {selected.Confidence}. Body capture is not available for this target yet."],
                    selected.Window.ToDisplayText());
                AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
                AddTimeline($"Selected target {selected.Title}; provider needed: {selected.ProviderKey}");
                return;
            }

            var plan = _appCaptureRouter.PlanFor(selected.Window);
            if (plan.CanCaptureNow)
            {
                _lastNativeUiResult = _nativeUiAutomationReader.ReadWindow(selected.Window.Handle);
                _lastContextSummary = _contextSummarizer.SummarizeNativeUi(_lastNativeUiResult);
                AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
                AddTimeline($"Selected and captured {selected.Title}");
            }
            else
            {
                _lastContextSummary = new SummarizedContext(
                    selected.Title,
                    plan.DisplayName,
                    plan.Guidance,
                    [selected.ToString()],
                    ["Capture provider is planned but not implemented yet."],
                    selected.Window.ToDisplayText());
                AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
                AddTimeline($"Selected {selected.Title}; provider required: {plan.DisplayName}");
            }
        });
    }

    private void LoadOpenWindows()
    {
        var targets = _tabTargetRegistry.ListTargets();
        OpenWindowsList.Items.Clear();
        foreach (var target in targets)
        {
            OpenWindowsList.Items.Add(target);
        }

        AddTimeline($"Found {targets.Count} app/window/tab target(s).");
    }
}
