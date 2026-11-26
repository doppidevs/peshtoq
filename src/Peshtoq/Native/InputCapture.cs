using System.Runtime.InteropServices;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Native;

/// <summary>Receives each captured event and returns true to block it from reaching the local machine.</summary>
public interface IInputHandler
{
    bool OnMouseMove(int absX, int absY, bool injected);
    bool OnMouseButton(int button, bool down, bool injected);
    bool OnWheel(int delta, bool horizontal, bool injected);
    bool OnKey(ushort vk, ushort scan, bool down, bool extended, bool injected);
}

/// <summary>
/// Global low-level mouse+keyboard hooks on a dedicated thread with its own message loop (the LL
/// hooks require one). The callbacks only classify the event and ask the handler whether to suppress
/// it — they never do network or disk work, so Windows will not drop the hook for being slow.
/// </summary>
public sealed class InputCapture : IDisposable
{
    private readonly IInputHandler _handler;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook, _kbdHook;
    private HookProc? _mouseProc, _kbdProc; // keep the delegates alive for the hook's lifetime
    private volatile bool _running;

    public InputCapture(IInputHandler handler) => _handler = handler;

    public void Start()
    {
        if (_thread is not null) return;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Peshtoq.InputCapture" };
        _thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _mouseProc = MouseProc;
        _kbdProc = KbdProc;
        IntPtr hMod = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        _kbdHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, hMod, 0);

        while (_running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_kbdHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbdHook); _kbdHook = IntPtr.Zero; }
    }

    private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HC_ACTION && TryMouse((int)wParam, lParam, out bool suppress) && suppress)
            return (IntPtr)1;
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private bool TryMouse(int msg, IntPtr lParam, out bool suppress)
    {
        suppress = false;
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        bool injected = (data.flags & LLMHF_INJECTED) != 0;
        switch (msg)
        {
            case WM_MOUSEMOVE:
                suppress = _handler.OnMouseMove(data.pt.X, data.pt.Y, injected); return true;
            case WM_LBUTTONDOWN: suppress = _handler.OnMouseButton(0, true, injected); return true;
            case WM_LBUTTONUP: suppress = _handler.OnMouseButton(0, false, injected); return true;
            case WM_RBUTTONDOWN: suppress = _handler.OnMouseButton(1, true, injected); return true;
            case WM_RBUTTONUP: suppress = _handler.OnMouseButton(1, false, injected); return true;
            case WM_MBUTTONDOWN: suppress = _handler.OnMouseButton(2, true, injected); return true;
            case WM_MBUTTONUP: suppress = _handler.OnMouseButton(2, false, injected); return true;
            case WM_XBUTTONDOWN:
            case WM_XBUTTONUP:
                int xb = (int)(data.mouseData >> 16) == XBUTTON2 ? 4 : 3;
                suppress = _handler.OnMouseButton(xb, msg == WM_XBUTTONDOWN, injected); return true;
            case WM_MOUSEWHEEL:
                suppress = _handler.OnWheel((short)(data.mouseData >> 16), false, injected); return true;
            case WM_MOUSEHWHEEL:
                suppress = _handler.OnWheel((short)(data.mouseData >> 16), true, injected); return true;
        }
        return false;
    }

    private IntPtr KbdProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HC_ACTION)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool injected = (data.flags & LLKHF_INJECTED) != 0;
            bool extended = (data.flags & LLKHF_EXTENDED) != 0;
            if (_handler.OnKey((ushort)data.vkCode, (ushort)data.scanCode, down, extended, injected))
                return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Stop()
    {
        if (_thread is null) return;
        _running = false;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1500);
        _thread = null;
    }

    public void Dispose() => Stop();
}
