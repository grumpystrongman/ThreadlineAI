using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private void EnsureReadableCheckBoxLabels()
    {
        SetReadableCheckBoxLabel(AllowProviderContextToggle, "Provider context");
        SetReadableCheckBoxLabel(SaveWorkThreadMemoryToggle, "Memory");
        SetReadableCheckBoxLabel(ShowContextReceiptsToggle, "Receipts");
        SetReadableCheckBoxLabel(ScreenshotVisionConsentToggle, "Vision once", new SolidColorBrush(Colors.SaddleBrown));
    }

    private static void SetReadableCheckBoxLabel(CheckBox checkBox, string label, Brush? foreground = null)
    {
        checkBox.Content = CreateReadableCheckBoxText(label, foreground);
        checkBox.Foreground = foreground ?? new SolidColorBrush(Colors.Black);
        checkBox.MinWidth = 0;
        checkBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        checkBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private static TextBlock CreateReadableCheckBoxText(string label, Brush? foreground = null) => new()
    {
        Text = label,
        Foreground = foreground ?? new SolidColorBrush(Colors.Black),
        FontSize = 11,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 0, 0)
    };
}
