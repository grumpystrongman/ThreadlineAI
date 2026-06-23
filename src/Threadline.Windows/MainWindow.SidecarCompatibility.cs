using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    // Build 13.7A changed the visual control from a checkable toggle to a mockup-style button.
    // Keep the existing sidecar placement code working without reintroducing the old visual toggle.
    private readonly AttachSidecarToggleShim AttachSidecarToggle = new();

    // Older shell code writes foreground-window status to CurrentWindowText. The command-center shell
    // now presents that same status in CurrentContextText, so forward the old name to the new card.
    private TextBlock CurrentWindowText => CurrentContextText;

    private void ToggleAttachSidecar_Click(object sender, RoutedEventArgs e)
    {
        AttachSidecarToggle.IsChecked = !_attachSidecarToTarget;
        ToggleAttachSidecarMode_Click(sender, e);
        UpdateAttachSidecarButtonLabel();
    }

    private void UpdateAttachSidecarButtonLabel()
    {
        try
        {
            AttachSidecarButton.Content = _attachSidecarToTarget ? "Screen Dock" : "Resize";
        }
        catch
        {
            // The button may not be generated yet during XAML precompile/startup paths.
        }
    }

    private sealed class AttachSidecarToggleShim
    {
        public bool? IsChecked { get; set; } = true;
    }
}