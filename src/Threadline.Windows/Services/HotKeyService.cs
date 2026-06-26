using System.Runtime.InteropServices;

namespace Threadline.Windows.Services;

public sealed class HotKeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int VkSpace = 0x20;

    private const int ToggleSidecarHotKeyId = 9001;

    private nint _windowHandle;
    private bool _registered;
    private Action? _toggleCallback;

    public void Register(nint windowHandle, Action toggleCallback)
    {
        _windowHandle = windowHandle;
        _toggleCallback = toggleCallback;

        _registered = RegisterHotKey(_windowHandle, ToggleSidecarHotKeyId, ModControl | ModShift, VkSpace);
    }

    public bool IsRegistered => _registered;

    public bool ProcessMessage(uint msg, nint wParam)
    {
        if (msg != WmHotkey) return false;
        if (wParam.ToInt32() != ToggleSidecarHotKeyId) return false;

        _toggleCallback?.Invoke();
        return true;
    }

    public void Dispose()
    {
        if (_registered && _windowHandle != nint.Zero)
        {
            UnregisterHotKey(_windowHandle, ToggleSidecarHotKeyId);
            _registered = false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
