using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private readonly IntPtr _windowHandle;
    private readonly HwndSource? _source;
    private readonly HashSet<int> _registeredIds = [];

    public GlobalHotKeyManager(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WndProc);
    }

    public event EventHandler<int>? HotKeyPressed;

    public void Register(int id, uint modifiers, uint virtualKey)
    {
        if (!RegisterHotKey(_windowHandle, id, modifiers, virtualKey))
        {
            throw new InvalidOperationException($"핫키 등록 실패: id={id}, vk=0x{virtualKey:X2}");
        }

        _registeredIds.Add(id);
    }

    public void Dispose()
    {
        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey)
        {
            handled = true;
            HotKeyPressed?.Invoke(this, wParam.ToInt32());
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
