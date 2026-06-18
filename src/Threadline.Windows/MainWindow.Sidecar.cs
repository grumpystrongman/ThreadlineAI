using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private void ConfigureSidecarWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var win = AppWindow.GetFromWindowId(id);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
            var width = 520;
            var height = Math.Max(700, area.Height - 80);
            win.Resize(new SizeInt32(width, height));
            win.Move(new PointInt32(area.X + area.Width - width - 24, area.Y + 40));
        }
        catch
        {
        }
    }
}
