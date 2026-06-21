using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private SidecarToggleState AttachSidecarToggle => new(_attachSidecarToTarget);

    private TextForwarder CurrentWindowText => new(value =>
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(value)
                ? "No resolved context yet."
                : value.Trim();

            CurrentContextText.Text = text;
        }
        catch
        {
            // Compatibility shim only; visual status failures should not crash startup.
        }
    });

    private void ToggleAttachSidecar_Click(object sender, RoutedEventArgs e)
    {
        _attachSidecarToTarget = !_attachSidecarToTarget;

        if (_attachSidecarToTarget)
        {
            PlaceSidecarForTarget(GetBestSidecarTarget(), "Attach sidecar mode enabled.");
            AddTimeline("Sidecar attach mode enabled.");
        }
        else
        {
            DockSidecarToScreen("Sidecar: Screen dock mode. Attach is off.");
            AddTimeline("Sidecar screen dock mode enabled.");
        }

        UpdateAttachSidecarButtonContent(sender);
    }

    private void UpdateAttachSidecarButtonContent(object? sender)
    {
        try
        {
            if (sender is Button button)
            {
                button.Content = _attachSidecarToTarget ? "Attach" : "Screen Dock";
                return;
            }

            AttachSidecarButton.Content = _attachSidecarToTarget ? "Attach" : "Screen Dock";
        }
        catch
        {
            // Button text is a convenience; placement state is the source of truth.
        }
    }

    private sealed record SidecarToggleState(bool? IsChecked);

    private sealed class TextForwarder
    {
        private readonly Action<string?> _setText;

        public TextForwarder(Action<string?> setText)
        {
            _setText = setText;
        }

        public string? Text
        {
            get => null;
            set => _setText(value);
        }
    }
}
