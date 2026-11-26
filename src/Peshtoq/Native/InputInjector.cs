using System.Runtime.InteropServices;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Native;

/// <summary>
/// Replays received input on this machine via SendInput. Relative mouse movement (DPI/resolution
/// independent) and scancode keyboard (layout independent). Used by the Target side of Peshtoq Link.
/// </summary>
public static class InputInjector
{
    private static readonly int Size = Marshal.SizeOf<INPUT>();

    public static void MoveRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        Send(new INPUT { type = INPUT_MOUSE, U = { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE } } });
    }

    /// <summary>button: 0=L 1=R 2=M 3=X1 4=X2.</summary>
    public static void Button(int button, bool down)
    {
        uint flags;
        uint mouseData = 0;
        switch (button)
        {
            case 0: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
            case 1: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
            case 2: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            case 3: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; mouseData = XBUTTON1; break;
            case 4: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; mouseData = XBUTTON2; break;
            default: return;
        }
        Send(new INPUT { type = INPUT_MOUSE, U = { mi = new MOUSEINPUT { mouseData = mouseData, dwFlags = flags } } });
    }

    public static void Wheel(int delta, bool horizontal)
    {
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = { mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL } },
        });
    }

    public static void Key(ushort scan, bool down, bool extended)
    {
        uint flags = KEYEVENTF_SCANCODE;
        if (!down) flags |= KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        Send(new INPUT { type = INPUT_KEYBOARD, U = { ki = new KEYBDINPUT { wScan = scan, dwFlags = flags } } });
    }

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Size);
}
