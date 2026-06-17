using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly OpenWindowCatalog _openWindowCatalog = new();
    private ActiveWindowSnapshot? _selectedTargetWindow;

    private void RefreshOpenWindows_Click(object sender, RoutedEventArgs e)
    {
        var windows = _openWindowCatalog.ListOpenWindows();
        OpenWindowsList.Items.Clear();
        foreach (var window in windows)
        {
            OpenWindowsList.Items.Add(window);
        }

        AddTimeline($"Found {windows.Count} open app window(s).");
    }

    private void UseSelectedWindow_Click(object sender, RoutedEventArgs e)
    {
        if (OpenWindowsList.SelectedItem is not ActiveWindowSnapshot selected)
        {
            AddTimeline("Select an open app window first.");
            return;
        }

        _selectedTargetWindow = selected;
        _lastForegroundWindow = selected;
        CurrentWindowText.Text = "Selected target:\n" + selected.ToDisplayText();
        AddTimeline($"Selected target {selected.ApplicationName}: {selected.WindowTitle}");
    }
}
