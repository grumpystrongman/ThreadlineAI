using Microsoft.UI.Xaml;
using Threadline.Windows.Services;
using System.Threading.Tasks;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly ActiveWindowContentResolver _contentResolver = new();
    private readonly ScreenshotVisionConsentPolicy _screenshotVisionPolicy = new();

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
            var consent = BuildContextCaptureConsent(selected);
            _lastContextSummary = await _contentResolver.ResolveAsync(_session!.Id, selected, consent);
            ResetScreenshotVisionOneTimeApproval(_lastContextSummary);
            UpdateCurrentContextPanel(_lastContextSummary);
            AppendTranscript("Selected Target Preview", _lastContextSummary.ToPromptContext());
            AddTimeline($"Selected target {selected.Title}; context source: {_lastContextSummary.Source}; receipt: {_lastContextSummary.Receipt?.CaptureKind.ToString() ?? "none"}");
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

    private async void AllowScreenshotVisionForSelectedApp_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() =>
        {
            var window = GetScreenshotVisionPolicyWindow() ?? throw new InvalidOperationException("Select or attach a target app first.");
            _screenshotVisionPolicy.Allow(window);
            UpdateScreenshotVisionTrustStatus(window, "Screenshot/OCR app allowed. One-time approval is still required for every capture.");
            AddTimeline($"Screenshot/OCR allowed for app: {window.ApplicationName}");
            return Task.CompletedTask;
        });
    }

    private async void DenyScreenshotVisionForSelectedApp_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() =>
        {
            var window = GetScreenshotVisionPolicyWindow() ?? throw new InvalidOperationException("Select or attach a target app first.");
            _screenshotVisionPolicy.Deny(window);
            ScreenshotVisionConsentToggle.IsChecked = false;
            UpdateScreenshotVisionTrustStatus(window, "Screenshot/OCR app denied. Capture is blocked for this app.");
            AddTimeline($"Screenshot/OCR denied for app: {window.ApplicationName}");
            return Task.CompletedTask;
        });
    }

    private async void ResetScreenshotVisionForSelectedApp_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() =>
        {
            var window = GetScreenshotVisionPolicyWindow() ?? throw new InvalidOperationException("Select or attach a target app first.");
            _screenshotVisionPolicy.ResetToPromptEachTime(window);
            UpdateScreenshotVisionTrustStatus(window, "Screenshot/OCR reset to ask each time.");
            AddTimeline($"Screenshot/OCR reset to ask-each-time for app: {window.ApplicationName}");
            return Task.CompletedTask;
        });
    }

    private void UpdateCurrentContextPanel(SummarizedContext context)
    {
        var status = BuildContextStatus(context);
        ContextStatusText.Text = status;
        CurrentContextText.Text = $"Source: {context.Source} • Confidence: {context.Confidence}\nSummary: {context.Summary}";

        if (context.Receipt is null)
        {
            ReceiptSourceText.Text = "No receipt";
            ReceiptTrustText.Text = "Unknown trust";
        }
        else
        {
            ReceiptSourceText.Text = $"{context.Receipt.CaptureKind} • {context.Receipt.SourceUsed}";
            ReceiptTrustText.Text = context.Receipt.MissingRealWorkingContent
                ? "Missing real content"
                : $"{context.Receipt.Confidence} trust";
        }

        DiagnosticsText.Text = context.Diagnostics?.ToDisplayText() ?? "No diagnostics are available for the current context.";
    }

    private void ResetCurrentContextPanel()
    {
        ContextStatusText.Text = "No context";
        CurrentContextText.Text = "No resolved context yet. Select a target and click Use.";
        ReceiptSourceText.Text = "No receipt";
        ReceiptTrustText.Text = "No context";
        DiagnosticsText.Text = "No diagnostics yet.";
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
    }

    private static string BuildContextStatus(SummarizedContext context)
    {
        var receipt = context.Receipt;
        if (receipt is not null)
        {
            if (receipt.MissingRealWorkingContent) return $"Missing content • {receipt.Confidence}";
            if (receipt.IsPageText) return $"Page text • {receipt.Confidence}";
            if (receipt.IsSelectedText) return $"Selected text • {receipt.Confidence}";
            if (receipt.IsFileBacked) return $"File-backed • {receipt.Confidence}";
            if (receipt.IsUiAutomation) return $"UI Automation • {receipt.Confidence}";
            if (receipt.IsScreenshotVision) return $"Screenshot/OCR • {receipt.Confidence}";
            if (receipt.IsOcr) return $"OCR • {receipt.Confidence}";
            if (receipt.IsTitleOnly) return $"Title only • {receipt.Confidence}";
        }

        var source = context.Source ?? string.Empty;

        if (source.Contains("needed", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("missing", StringComparison.OrdinalIgnoreCase))
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
            return $"Screenshot/OCR • {context.Confidence}";
        }

        if (context.Confidence == ContextConfidence.None)
        {
            return "No readable context";
        }

        return $"{context.Source} • {context.Confidence}";
    }

    private ContextCaptureConsent BuildContextCaptureConsent(ThreadlineTarget target)
    {
        var oneTimeApproval = IsScreenshotVisionOneTimeApproved();
        var decision = _screenshotVisionPolicy.Evaluate(target.Window, oneTimeApproval, rawScreenshotStorageAllowed: false);
        UpdateScreenshotVisionTrustStatus(target.Window, decision.ToStatusText());

        return new ContextCaptureConsent(
            ClipboardSelectionAllowed: false,
            ScreenshotVisionAllowed: decision.Allowed,
            ScreenshotVisionUserApproved: decision.UserApprovedThisCapture,
            ScreenshotVisionAppAllowed: decision.AppPolicyAllowsCapture,
            RawScreenshotStorageAllowed: decision.RawScreenshotStorageAllowed,
            ScreenshotVisionConsentReason: decision.Reason);
    }

    private bool IsScreenshotVisionOneTimeApproved() => GetToggleValue(ScreenshotVisionConsentToggle, defaultValue: false);

    private void ResetScreenshotVisionOneTimeApproval(SummarizedContext context)
    {
        if (!IsScreenshotVisionOneTimeApproved())
        {
            return;
        }

        ScreenshotVisionConsentToggle.IsChecked = false;
        var window = GetScreenshotVisionPolicyWindow();
        var status = context.Receipt?.IsScreenshotVision == true || context.Receipt?.IsOcr == true
            ? "Screenshot/OCR used once; approval reset."
            : "Screenshot/OCR approval reset; no screenshot was needed for this context.";

        if (window is not null)
        {
            UpdateScreenshotVisionTrustStatus(window, status);
        }
        else
        {
            ScreenshotVisionPolicyText.Text = status;
            TrustControlStatusText.Text = status;
        }
    }

    private ActiveWindowSnapshot? GetScreenshotVisionPolicyWindow()
    {
        if (OpenWindowsList.SelectedItem is ThreadlineTarget selected)
        {
            return selected.Window;
        }

        if (_selectedThreadlineTarget is not null) return _selectedThreadlineTarget.Window;
        if (_lastFollowTarget is not null) return _lastFollowTarget.Window;
        return _lastForegroundWindow;
    }

    private void UpdateScreenshotVisionTrustStatus(ActiveWindowSnapshot window, string status)
    {
        var policyText = _screenshotVisionPolicy.DescribePolicy(window);
        ScreenshotVisionPolicyText.Text = $"Vision: {policyText} {status}";
        TrustControlStatusText.Text = status;
    }
}
