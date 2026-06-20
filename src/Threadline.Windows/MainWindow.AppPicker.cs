using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private readonly TabTargetRegistry _tabTargetRegistry = new();
    private ActiveWindowSnapshot? _selectedTargetWindow;
    private ThreadlineTarget? _selectedThreadlineTarget;

    private void RefreshOpenWindows_Click(object sender, RoutedEventArgs e)
    {
        LoadOpenWindows();
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
