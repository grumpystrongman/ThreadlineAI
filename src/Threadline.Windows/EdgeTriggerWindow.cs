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

    private readonly Border _triggerTab;
    private readonly Grid _root;
    private AppWindow? _appWindow;
    private PointInt32 _lastLocation;
    private SizeInt32 _lastSize;
    private bool _isConfigured;
    private bool _isVisible;
    private bool _isPointerInside;

    public EdgeTriggerWindow()
    {
        Title = "Threadline edge trigger";
        ExtendsContentIntoTitleBar = true;

        var tabBackground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(236, 30, 34, 48));

        _root = new Grid
        {
            Width = 56,
            Height = 132,
            IsHitTestVisible = true,
            Background = tabBackground
        };

        var sparkleText = new TextBlock
        {
            Text = "✦",
            FontSize = 20,
            Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(245, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(sparkleText, 1);

        var aiText = new TextBlock
        {
            Text = "AI",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(245, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(aiText, 2);

        var openText = new TextBlock
        {
            Text = "open",
            FontSize = 9,
            Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(215, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(openText, 3);

        var tabContent = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };
        tabContent.Children.Add(sparkleText);
        tabContent.Children.Add(aiText);
        tabContent.Children.Add(openText);

        _triggerTab = new Border
        {
            Width = 56,
            Height = 132,
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Opacity = 0.86,
            IsHitTestVisible = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = tabBackground,
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(170, 255, 255, 255)),
            Child = tabContent
        };

        _root.Children.Add(_triggerTab);
        _root.PointerEntered += (_, _) => SetHoverState(true);
        _root.PointerExited += (_, _) => SetHoverState(false);
        _root.PointerPressed += OnTriggerPressed;
        _root.Tapped += OnTriggerTapped;
        _triggerTab.PointerEntered += (_, _) => SetHoverState(true);
        _triggerTab.PointerExited += (_, _) => SetHoverState(false);
        _triggerTab.PointerPressed += OnTriggerPressed;
        _triggerTab.Tapped += OnTriggerTapped;

        Content = _root;
    }

    public event EventHandler? TriggerRequested;

    public bool IsVisible => _isVisible;

    public bool IsPointerInside => _isPointerInside;

    public bool IsCursorWithinReach(int x, int y, int padding)
    {
        if (!_isVisible) return false;

        return x >= _lastLocation.X - padding &&
               x <= _lastLocation.X + _lastSize.Width + padding &&
               y >= _lastLocation.Y - padding &&
               y <= _lastLocation.Y + _lastSize.Height + padding;
    }

    public void ShowAt(PointInt32 location, SizeInt32 size)
    {
        EnsureConfigured(size);
        if (_appWindow is null) return;

        _lastLocation = location;
        _lastSize = size;
        _root.Width = size.Width;
        _root.Height = size.Height;
        _triggerTab.Width = size.Width;
        _triggerTab.Height = size.Height;
        _triggerTab.CornerRadius = new CornerRadius(Math.Max(16, size.Width / 2 - 4));

        var hwnd = WindowNative.GetWindowHandle(this);
        _appWindow.Resize(size);
        ApplyRoundedWindowRegion(hwnd, size);
        _appWindow.Move(location);
        _ = ShowWindow(hwnd, ShowNoActivate);
        _isVisible = true;
    }

    public void HideTrigger()
    {
        if (!_isConfigured || !_isVisible) return;

        _ = ShowWindow(WindowNative.GetWindowHandle(this), HideWindow);
        _isVisible = false;
        _isPointerInside = false;
    }

    private void EnsureConfigured(SizeInt32 size)
    {
        if (_isConfigured) return;

        Activate();
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        _appWindow.Resize(size);
        ApplyRoundedWindowRegion(hwnd, size);

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

    private void SetHoverState(bool isHovering)
    {
        _isPointerInside = isHovering;
        _triggerTab.Opacity = isHovering ? 0.98 : 0.86;
        var background = isHovering
            ? global::Windows.UI.Color.FromArgb(248, 52, 58, 82)
            : global::Windows.UI.Color.FromArgb(236, 30, 34, 48);
        var brush = new SolidColorBrush(background);
        _root.Background = brush;
        _triggerTab.Background = brush;
    }

    private void OnTriggerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        RequestTrigger();
    }

    private void OnTriggerTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        RequestTrigger();
    }

    private void RequestTrigger()
    {
        TriggerRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyRoundedWindowRegion(nint hwnd, SizeInt32 size)
    {
        var radius = Math.Max(24, size.Width - 4);
        var region = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, radius, radius);
        if (region == nint.Zero) return;

        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);
}
