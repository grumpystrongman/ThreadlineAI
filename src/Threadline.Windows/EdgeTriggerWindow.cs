using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Threadline.Windows;

public sealed class EdgeTriggerWindow
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint LwaColorKey = 0x00000001;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    private const uint WmPaint = 0x000F;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmNchitTest = 0x0084;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmMouseLeave = 0x02A3;
    private const nint HitTestClient = 1;
    private const nint MouseActivateNoActivate = 3;
    private const uint TrackMouseLeave = 0x00000002;
    private const int TransparentBkMode = 1;
    private const uint DrawTextCenter = 0x00000001;
    private const uint DrawTextVCenter = 0x00000004;
    private const uint DrawTextSingleLine = 0x00000020;
    private const int SemiBoldFontWeight = 600;

    private readonly WndProc _wndProc;
    private readonly string _windowClassName = "ThreadlineNativeEdgeIcon_" + Guid.NewGuid().ToString("N");

    private nint _hwnd;
    private bool _isRegistered;
    private bool _isVisible;
    private bool _isPointerInside;
    private bool _isTrackingMouseLeave;
    private PointInt32 _lastLocation;
    private SizeInt32 _lastSize;

    public EdgeTriggerWindow()
    {
        _wndProc = HandleWindowMessage;
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
        EnsureWindow();
        if (_hwnd == nint.Zero) return;

        var safeSize = new SizeInt32(Math.Max(48, size.Width), Math.Max(48, size.Height));
        _lastLocation = location;
        _lastSize = safeSize;

        _ = SetWindowPos(_hwnd, HwndTopmost, location.X, location.Y, safeSize.Width, safeSize.Height, SwpNoActivate | SwpShowWindow);
        _ = ShowWindow(_hwnd, SwShowNoActivate);
        _isVisible = true;
        _ = InvalidateRect(_hwnd, nint.Zero, true);
    }

    public void HideTrigger()
    {
        if (_hwnd == nint.Zero || !_isVisible) return;

        _ = ShowWindow(_hwnd, SwHide);
        _isVisible = false;
        _isPointerInside = false;
        _isTrackingMouseLeave = false;
    }

    private void EnsureWindow()
    {
        if (_hwnd != nint.Zero) return;

        RegisterWindowClass();

        var hInstance = GetModuleHandle(null);
        _hwnd = CreateWindowEx(
            WsExTopmost | WsExToolWindow | WsExLayered | WsExNoActivate,
            _windowClassName,
            "Threadline AI edge icon",
            WsPopup,
            0,
            0,
            64,
            64,
            nint.Zero,
            nint.Zero,
            hInstance,
            nint.Zero);

        if (_hwnd == nint.Zero) return;

        // Treat pure black as transparent. The paint routine fills the host window with black,
        // then draws only the visible AI icon. This avoids the black WinUI rectangle entirely.
        _ = SetLayeredWindowAttributes(_hwnd, Rgb(0, 0, 0), 255, LwaColorKey);
        _ = ShowWindow(_hwnd, SwHide);
    }

    private void RegisterWindowClass()
    {
        if (_isRegistered) return;

        var hInstance = GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            style = 0,
            lpfnWndProc = _wndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = nint.Zero,
            hCursor = LoadCursor(nint.Zero, 32512),
            hbrBackground = nint.Zero,
            lpszMenuName = null,
            lpszClassName = _windowClassName,
            hIconSm = nint.Zero
        };

        _ = RegisterClassEx(ref windowClass);
        _isRegistered = true;
    }

    private nint HandleWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WmPaint:
                PaintIcon(hwnd);
                return nint.Zero;
            case WmEraseBackground:
                return 1;
            case WmMouseActivate:
                return MouseActivateNoActivate;
            case WmNchitTest:
                return HitTestClient;
            case WmMouseMove:
                SetPointerInside(true);
                TrackMouseLeaveForWindow(hwnd);
                return nint.Zero;
            case WmMouseLeave:
                _isTrackingMouseLeave = false;
                SetPointerInside(false);
                return nint.Zero;
            case WmLButtonDown:
            case WmLButtonUp:
                SetPointerInside(true);
                TriggerRequested?.Invoke(this, EventArgs.Empty);
                return nint.Zero;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void SetPointerInside(bool isInside)
    {
        if (_isPointerInside == isInside) return;
        _isPointerInside = isInside;
        if (_hwnd != nint.Zero)
        {
            _ = InvalidateRect(_hwnd, nint.Zero, true);
        }
    }

    private void TrackMouseLeaveForWindow(nint hwnd)
    {
        if (_isTrackingMouseLeave) return;

        var trackMouseEvent = new TrackMouseEvent
        {
            cbSize = Marshal.SizeOf<TrackMouseEvent>(),
            dwFlags = TrackMouseLeave,
            hwndTrack = hwnd,
            dwHoverTime = 0
        };

        if (TrackMouseEvent(ref trackMouseEvent))
        {
            _isTrackingMouseLeave = true;
        }
    }

    private void PaintIcon(nint hwnd)
    {
        var hdc = BeginPaint(hwnd, out var paintStruct);
        if (hdc == nint.Zero) return;

        try
        {
            if (!GetClientRect(hwnd, out var clientRect)) return;

            var blackBrush = CreateSolidBrush(Rgb(0, 0, 0));
            _ = FillRect(hdc, ref clientRect, blackBrush);
            _ = DeleteObject(blackBrush);

            var width = Math.Max(1, clientRect.Right - clientRect.Left);
            var height = Math.Max(1, clientRect.Bottom - clientRect.Top);
            var iconSize = Math.Min(52, Math.Max(40, Math.Min(width, height) - 8));
            var left = (width - iconSize) / 2;
            var top = (height - iconSize) / 2;
            var right = left + iconSize;
            var bottom = top + iconSize;

            var fill = _isPointerInside ? Rgb(82, 92, 132) : Rgb(46, 52, 78);
            var border = _isPointerInside ? Rgb(255, 255, 255) : Rgb(180, 190, 220);
            var fillBrush = CreateSolidBrush(fill);
            var borderPen = CreatePen(0, 2, border);
            var oldBrush = SelectObject(hdc, fillBrush);
            var oldPen = SelectObject(hdc, borderPen);
            _ = Ellipse(hdc, left, top, right, bottom);
            _ = SelectObject(hdc, oldPen);
            _ = SelectObject(hdc, oldBrush);
            _ = DeleteObject(borderPen);
            _ = DeleteObject(fillBrush);

            var font = CreateFont(-17, 0, 0, 0, SemiBoldFontWeight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            var oldFont = font == nint.Zero ? nint.Zero : SelectObject(hdc, font);
            _ = SetBkMode(hdc, TransparentBkMode);
            _ = SetTextColor(hdc, Rgb(255, 255, 255));
            var textRect = new NativeRect { Left = left, Top = top, Right = right, Bottom = bottom };
            _ = DrawText(hdc, "AI", -1, ref textRect, DrawTextCenter | DrawTextVCenter | DrawTextSingleLine);
            if (oldFont != nint.Zero)
            {
                _ = SelectObject(hdc, oldFont);
            }
            if (font != nint.Zero)
            {
                _ = DeleteObject(font);
            }
        }
        finally
        {
            _ = EndPaint(hwnd, ref paintStruct);
        }
    }

    private static uint Rgb(byte red, byte green, byte blue) => (uint)(red | (green << 8) | (blue << 16));

    private delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint hdc;
        public int fErase;
        public NativeRect rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEvent
    {
        public int cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hWnd, out PaintStruct lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(nint hWnd, ref PaintStruct lpPaint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEvent lpEventTrack);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint hInstance, int lpCursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint colorRef);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hDC, ref NativeRect lprc, nint hbr);

    [DllImport("gdi32.dll")]
    private static extern nint CreatePen(int fnPenStyle, int nWidth, uint crColor);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool Ellipse(nint hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFont(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(nint hdc, uint colorRef);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(nint hdc, string lpchText, int cchText, ref NativeRect lprc, uint format);
}
