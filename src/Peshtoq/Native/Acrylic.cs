using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Peshtoq.Native;

/// <summary>
/// Windows acrylic blur-behind for a layered (AllowsTransparency) WPF window, via the undocumented
/// SetWindowCompositionAttribute. It fills the whole window rectangle with blurred backdrop + a tint,
/// so it suits a window that is entirely its surface (the header bar), not one that is mostly a
/// transparent canvas (the island).
/// </summary>
internal static class Acrylic
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int DrawLeftBorder = 0x20, DrawTopBorder = 0x40, DrawRightBorder = 0x80, DrawBottomBorder = 0x100;

    /// <summary>
    /// Turn on acrylic for the window, tinted with <paramref name="tint"/> (its alpha sets how much
    /// blur shows through). <paramref name="edges"/> picks which 1px accent borders Windows draws.
    /// Returns false when the OS refuses it, so the caller can fall back to a solid background.
    /// </summary>
    public static bool Enable(Window window, Color tint, bool bottomBorder = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        int flags = bottomBorder ? DrawBottomBorder : 0;
        uint gradient = ((uint)tint.A << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
        var accent = new AccentPolicy { AccentState = AccentEnableAcrylicBlurBehind, AccentFlags = flags, GradientColor = gradient };
        return Apply(hwnd, accent);
    }

    public static void Disable(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) Apply(hwnd, new AccentPolicy { AccentState = AccentDisabled });
    }

    private static bool Apply(IntPtr hwnd, AccentPolicy accent)
    {
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
