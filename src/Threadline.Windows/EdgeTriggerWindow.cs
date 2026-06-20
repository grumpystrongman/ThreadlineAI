using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed class EdgeTriggerWindow : Window
{
    private const int ShowNoActivate = 4;
    private const int HideWindow = 0;

    private readonly Border _triggerPill;
    private AppWindow? _appWindow;
    private bool _isConfigured;
    private bool _isVisible;

    public EdgeTriggerWindow()
    {
        Title = "Threadline edge trigger";
        ExtendsContentIntoTitleBar = true;

        var root = new Grid
        {
            Width = 42,
            Height = 118,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(1, 0, 0, 0))
        };

        _triggerPill = new Border
        {
            Width = 34,
            Height = 104,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            Opacity = 0.72,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(218, 32, 32, 36)),
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(180, 255, 255, 255)),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = "✦",
                        FontSize = 17,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    },
                    new TextBlock
                    {
                        Text = "AI",
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 6, 0, 0)
                    }
                }
            }
        };

        if (_triggerPill.Child is Grid contentGrid && contentGrid.Children.Count >= 2)
        {
            Grid.SetRow(contentGrid.Children[0], 1);
            Grid.SetRow(contentGrid.Children[1], 2);
        }

        root.Children.Add(_triggerPill);
        root.PointerEntered += (_, _) => _triggerPill.Opacity = 0.96;
        root.PointerExited += (_, _) => _triggerPill.Opacity = 0.72;
        root.PointerPressed += OnTriggerPressed;

        Content = root;
    }

    public event EventHandler? TriggerRequested;

    public void ShowAt(PointInt32 location, SizeInt32 size)
    {
        EnsureConfigured(size);
        if (_appWindow is null) return;

        _appWindow.Resize(size);
        _appWindow.Move(location);
        _ = ShowWindow(WindowNative.GetWindowHandle(this), ShowNoActivate);
        _isVisible = true;
    }

    public void HideTrigger()
    {
        if (!_isConfigured || !_isVisible) return;

        _ = ShowWindow(WindowNative.GetWindowHandle(this), HideWindow);
        _isVisible = false;
    }

    private void EnsureConfigured(SizeInt32 size)
    {
        if (_isConfigured) return;

        Activate();
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        _appWindow.Resize(size);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        _ = ShowWindow(hwnd, HideWindow);
        _isConfigured = true;
        _isVisible = false;
    }

    private void OnTriggerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        TriggerRequested?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
