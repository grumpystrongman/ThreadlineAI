using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private bool _firstRunSetupChecked;

    private static string FirstRunStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThreadlineAI",
        "first-run-complete.json");

    private async Task ShowFirstRunSetupWizardIfNeededAsync()
    {
        if (_firstRunSetupChecked) return;
        _firstRunSetupChecked = true;

        if (File.Exists(FirstRunStatePath)) return;

        try
        {
            var serviceSummary = await BuildFirstRunServiceSummaryAsync();
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = "ThreadlineAI is now packaged as commercial Windows software. Setup verifies the local service, explains the browser extension, and points you to diagnostics if anything is missing.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = serviceSummary,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });
            content.Children.Add(new TextBlock
            {
                Text = "Browser extension: install the Chrome/Edge extension from the packaged browser-extension folder, then paste the local token from %LOCALAPPDATA%\\ThreadlineAI\\service-token.txt into the extension options page. The extension should show Connected after it can call the local service.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });
            content.Children.Add(new TextBlock
            {
                Text = "Privacy: local database, provider credentials, extension token, logs, and diagnostics stay under your local ThreadlineAI profile unless you explicitly export or clear them.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });

            var dialog = new ContentDialog
            {
                Title = "ThreadlineAI first-run setup",
                Content = content,
                PrimaryButtonText = "Finish setup",
                SecondaryButtonText = "Open tools",
                CloseButtonText = "Later",
                XamlRoot = RootShell.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FirstRunStatePath)!);
                await File.WriteAllTextAsync(FirstRunStatePath, "{\"completed\":true,\"build\":\"21\"}");
                AppendTranscript("Threadline Setup", "First-run setup completed. Use Tools for service health, diagnostics export, and clear-local-data controls.");
                AddTimeline("First-run setup completed.");
            }
            else if (result == ContentDialogResult.Secondary)
            {
                OpenDiagnosticsTargetPickerPanel();
                AppendTranscript("Threadline Setup", "Opened Tools. Check Service verifies the local service; Diagnostics export creates a support-ready package.");
            }
        }
        catch (Exception ex)
        {
            AddTimeline("First-run setup wizard unavailable: " + ex.Message);
        }
    }

    private async Task<string> BuildFirstRunServiceSummaryAsync()
    {
        try
        {
            var health = await _client.GetHealthAsync();
            return $"Service: {health.Status}; version: {health.ProductVersion ?? health.ServiceVersion ?? "unknown"}; token required: {health.AuthRequired}; API: {health.ApiCompatibility ?? "unknown"}.";
        }
        catch (Exception ex)
        {
            return "Service: not connected yet. Use Tools > Check Service after installation, or run the service repair/install script. Detail: " + ex.Message;
        }
    }
}
