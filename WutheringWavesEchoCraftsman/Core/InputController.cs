using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

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

        SendAbsoluteMove(x, y);
        Thread.Sleep(40);

        SendMouse(MouseEventFlags.LeftDown, "MouseLeftDown");
        Thread.Sleep(60);
        SendMouse(MouseEventFlags.LeftUp, "MouseLeftUp");
    }

    public void PressKey(ushort virtualKey)
    {
        if (DryRun)
        {
            _log($"[DryRun] 키 입력 예정: 0x{virtualKey:X2}");
            return;
        }

        SendKeyboard(virtualKey, 0, "KeyDown");
        Thread.Sleep(40);
        SendKeyboard(virtualKey, KeyEventFlags.KeyUp, "KeyUp");
    }

    public (int X, int Y) GetCursorPosition()
    {
        return GetCursorPos(out var point)
            ? (point.X, point.Y)
            : (0, 0);
    }

    private void SendMouse(MouseEventFlags flags, string label)
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

        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        var error = Marshal.GetLastWin32Error();
        _log($"{label} SendInput => sent={sent}, error={error}");
    }

    private void SendAbsoluteMove(int x, int y)
    {
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var normalizedX = NormalizeAbsoluteCoordinate(x, virtualScreen.Left, virtualScreen.Width);
        var normalizedY = NormalizeAbsoluteCoordinate(y, virtualScreen.Top, virtualScreen.Height);

        var input = new INPUT
        {
            type = InputType.Mouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    dwFlags = MouseEventFlags.Move | MouseEventFlags.Absolute | MouseEventFlags.VirtualDesk,
                },
            },
        };

        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        var error = Marshal.GetLastWin32Error();
        _log($"MouseMoveAbsolute({x}, {y}) normalized=({normalizedX}, {normalizedY}) SendInput => sent={sent}, error={error}");
    }

    private static int NormalizeAbsoluteCoordinate(int value, int origin, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        return (int)Math.Round((value - origin) * 65535.0 / (size - 1));
    }

    private void SendKeyboard(ushort virtualKey, KeyEventFlags flags, string label)
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

        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        var error = Marshal.GetLastWin32Error();
        _log($"{label} SendInput(vk=0x{virtualKey:X2}) => sent={sent}, error={error}");
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
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        Absolute = 0x8000,
        VirtualDesk = 0x4000,
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
