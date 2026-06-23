using Microsoft.UI.Xaml;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private void ToggleSessionManagerPanel_Click(object sender, RoutedEventArgs e)
    {
        _ = RunUiActionAsync(async () =>
        {
            if (IsDrawerOpenFor(WorkThreadSessionManagerPanel))
            {
                CloseShellDrawer();
                return;
            }

            OpenSessionManagerPanel();
            await RefreshWorkThreadSessionListAsync(selectActive: true);
        });
    }

    private void ToggleDiagnosticsTargetPickerPanel_Click(object sender, RoutedEventArgs e)
    {
        if (IsDrawerOpenFor(DiagnosticsTargetPickerPanel))
        {
            CloseShellDrawer();
            return;
        }

        OpenDiagnosticsTargetPickerPanel();
        LoadOpenWindows();
    }

    private void CloseShellOverlayPanel_Click(object sender, RoutedEventArgs e)
    {
        CloseShellDrawer();
    }

    private void OpenSessionsDrawer()
    {
        ShowShellDrawer("Sessions", WorkThreadSessionManagerPanel);
        WorkThreadListStatusText.Text = "Sessions are shown here so the main sidecar can stay focused on the conversation.";
    }

    private void OpenSettingsDrawer()
    {
        ShowShellDrawer("Settings", ProviderSettingsPanel);
    }

    private void OpenDiagnosticsTargetPickerPanel()
    {
        ShowShellDrawer("Diagnostics / Target Picker", DiagnosticsTargetPickerPanel);
        EnsureAmbientCaptureToolsPanel();
    }

    private void ShowShellDrawer(string title, FrameworkElement activePanel)
    {
        HideDrawerPanels();
        ShellOverlayTitleText.Text = title;
        activePanel.Visibility = Visibility.Visible;
        ShellOverlayPanel.Visibility = Visibility.Visible;
    }

    private void CloseShellDrawer()
    {
        HideDrawerPanels();
        ShellOverlayPanel.Visibility = Visibility.Collapsed;
    }

    private void HideDrawerPanels()
    {
        WorkThreadSessionManagerPanel.Visibility = Visibility.Collapsed;
        ProviderSettingsPanel.Visibility = Visibility.Collapsed;
        DiagnosticsTargetPickerPanel.Visibility = Visibility.Collapsed;
    }

    private bool IsDrawerOpenFor(FrameworkElement panel)
    {
        return ShellOverlayPanel.Visibility == Visibility.Visible && panel.Visibility == Visibility.Visible;
    }
}
