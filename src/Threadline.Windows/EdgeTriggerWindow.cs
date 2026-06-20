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

        _root = new Grid
        {
            Width = 40,
            Height = 112,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0))
        };

        var sparkleText = new TextBlock
        {
            Text = "✦",
            FontSize = 17,
            Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(235, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(sparkleText, 1);

        var aiText = new TextBlock
        {
            Text = "AI",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(235, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(aiText, 2);

        var pillContent = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };
        pillContent.Children.Add(sparkleText);
        pillContent.Children.Add(aiText);

        _triggerPill = new Border
        {
            Width = 40,
            Height = 112,
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Opacity = 0.54,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(188, 28, 28, 34)),
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(120, 255, 255, 255)),
            Child = pillContent
        };

        _root.Children.Add(_triggerPill);
        _root.PointerEntered += (_, _) => SetHoverState(true);
        _root.PointerExited += (_, _) => SetHoverState(false);
        _root.PointerPressed += OnTriggerPressed;
        _root.Tapped += OnTriggerTapped;
        _triggerPill.PointerPressed += OnTriggerPressed;
        _triggerPill.Tapped += OnTriggerTapped;

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
        _triggerPill.Width = size.Width;
        _triggerPill.Height = size.Height;

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
        _triggerPill.Opacity = isHovering ? 0.96 : 0.54;
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
        var region = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, 30, 30);
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
