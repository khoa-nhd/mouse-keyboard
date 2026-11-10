using System.Runtime.InteropServices;

namespace KeyboardMouse.Native;

internal static class Dwm
{
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>UWP apps keep hidden ghost windows that are enum-visible but cloaked.</summary>
    public const int DWMWA_CLOAKED = 14;

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    /// <summary>Integer-valued attribute overload (DWMWA_CLOAKED).</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
