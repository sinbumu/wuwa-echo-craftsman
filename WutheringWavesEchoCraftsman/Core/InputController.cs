using System.Runtime.InteropServices;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class InputController
{
    private readonly Action<string> _log;

    public InputController(bool dryRun, Action<string>? log = null)
    {
        DryRun = dryRun;
        _log = log ?? (_ => { });
    }

    public bool DryRun { get; set; }

    public void Click(int x, int y)
    {
        if (DryRun)
        {
            _log($"[DryRun] 클릭 예정: ({x}, {y})");
            return;
        }

        SetCursorPos(x, y);
        SendMouse(MouseEventFlags.LeftDown);
        SendMouse(MouseEventFlags.LeftUp);
    }

    public void PressKey(ushort virtualKey)
    {
        if (DryRun)
        {
            _log($"[DryRun] 키 입력 예정: 0x{virtualKey:X2}");
            return;
        }

        SendKeyboard(virtualKey, 0);
        SendKeyboard(virtualKey, KeyEventFlags.KeyUp);
    }

    public (int X, int Y) GetCursorPosition()
    {
        return GetCursorPos(out var point)
            ? (point.X, point.Y)
            : (0, 0);
    }

    private static void SendMouse(MouseEventFlags flags)
    {
        var input = new INPUT
        {
            type = InputType.Mouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                },
            },
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyboard(ushort virtualKey, KeyEventFlags flags)
    {
        var input = new INPUT
        {
            type = InputType.Keyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags,
                },
            },
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1,
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
    }

    [Flags]
    private enum KeyEventFlags : uint
    {
        KeyUp = 0x0002,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public InputType type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public MouseEventFlags dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KeyEventFlags dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}

public static class VirtualKeys
{
    public const ushort Escape = 0x1B;
    public const ushort C = 0x43;
    public const ushort Z = 0x5A;
}
