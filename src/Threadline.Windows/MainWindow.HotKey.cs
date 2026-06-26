using System.Runtime.InteropServices;
using Threadline.Windows.Services;
using WinRT.Interop;

namespace Threadline.Windows;

public sealed partial class MainWindow
{
    private const int GwlWndProc = -4;

    private HotKeyService? _hotKeyService;
    private nint _originalWndProc;
    private WndProcDelegate? _wndProcDelegate;

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    private void InitializeHotKeyService()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == nint.Zero) return;

            _hotKeyService = new HotKeyService();
            _hotKeyService.Register(hwnd, ToggleSidecarViaHotKey);

            if (!_hotKeyService.IsRegistered)
            {
                AddTimeline("Global hotkey (Ctrl+Shift+Space) could not be registered. Another app may hold it.");
                _hotKeyService.Dispose();
                _hotKeyService = null;
                return;
            }

            _wndProcDelegate = HotKeyWndProc;
            _originalWndProc = SetWindowLongPtr(hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            AddTimeline("Global hotkey registered: Ctrl+Shift+Space toggles the sidecar.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] HotKey init failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private nint HotKeyWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (_hotKeyService is not null && _hotKeyService.ProcessMessage(msg, wParam))
        {
            return nint.Zero;
        }

        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    private void ToggleSidecarViaHotKey()
    {
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                if (hwnd != nint.Zero && IsIconic(hwnd))
                {
                    _sidecarCollapsedToHandle = false;
                    _sidecarWindowHiddenForTrigger = false;
                    _floatingTriggerTarget = null;
                    _edgeTriggerWindow?.HideTrigger();
                    ShowMainSidecarWindow();
                    SetSidecarVisualState();
                    PlaceSidecarForTarget(GetBestSidecarTarget(), "Restored from Ctrl+Shift+Space after Windows minimized the sidecar.");
                    AddTimeline("Sidecar restored from minimized state with Ctrl+Shift+Space.");
                    return;
                }

                if (_sidecarCollapsedToHandle || _sidecarWindowHiddenForTrigger)
                {
                    RestoreSidecarFromFloatingTrigger();
                }
                else
                {
                    HideSidecarBehindFloatingTrigger();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Threadline] HotKey toggle failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void DisposeHotKeyService()
    {
        _hotKeyService?.Dispose();
        _hotKeyService = null;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);
}
