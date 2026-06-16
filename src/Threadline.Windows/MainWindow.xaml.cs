using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public sealed partial class MainWindow : Window
{
    private readonly ActiveWindowMonitor _activeWindowMonitor = new();
    public MainWindow() { InitializeComponent(); RefreshActiveWindow(); }
    private void StartSession_Click(object sender, RoutedEventArgs e) { TimelineList.Items.Add($"{DateTimeOffset.Now:t} Session started"); RefreshActiveWindow(); }
    private void Ask_Click(object sender, RoutedEventArgs e)
    {
        var question = QuestionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(question)) return;
        ChatTranscript.Text += $"\n\nYou: {question}\nThreadline: Local service integration is the next implementation step.";
        QuestionBox.Text = string.Empty;
    }
    private void RefreshActiveWindow() => CurrentWindowText.Text = _activeWindowMonitor.GetActiveWindowSnapshot().ToDisplayText();
}
