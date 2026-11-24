using System.Windows;
using System.Windows.Interop;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Native;

/// <summary>
/// Registers a window as a top-edge AppBar, so Windows reserves the strip for it
/// and maximized windows start below it (the same mechanism the taskbar uses).
/// </summary>
internal sealed class AppBar : IDisposable
{
    private readonly Window _window;
    private readonly double _heightDip;
    private IntPtr _hwnd;
    private uint _callbackMessage;
    private bool _registered;

    public event Action<bool>? FullscreenChanged;

    public AppBar(Window window, double heightDip)
    {
        _window = window;
        _heightDip = heightDip;
    }

    public void Register()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _callbackMessage = RegisterWindowMessage("Peshtoq.AppBarMessage");
        var abd = NewData();
        abd.uCallbackMessage = _callbackMessage;
        SHAppBarMessage(ABM_NEW, ref abd);
        _registered = true;
        UpdatePosition();
    }

    public void UpdatePosition()
    {
        if (!_registered) return;

        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int height = (int)Math.Round(_heightDip * scale);
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);

        var abd = NewData();
        abd.uEdge = ABE_TOP;
        abd.rc = new RECT { Left = 0, Top = 0, Right = screenWidth, Bottom = height };
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + height;
        SHAppBarMessage(ABM_SETPOS, ref abd);

        MoveWindow(_hwnd, abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, height, true);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_registered && msg == (int)_callbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    UpdatePosition();
                    break;
                case ABN_FULLSCREENAPP:
                    FullscreenChanged?.Invoke(lParam != IntPtr.Zero);
                    break;
            }
            handled = true;
        }
        else if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            _window.Dispatcher.BeginInvoke(UpdatePosition);
        }
        return IntPtr.Zero;
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    public void Dispose()
    {
        if (!_registered) return;
        var abd = NewData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }
}
