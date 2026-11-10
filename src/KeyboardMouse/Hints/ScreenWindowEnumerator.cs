using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using KeyboardMouse.Core;
using KeyboardMouse.Native;

namespace KeyboardMouse.Hints;

/// <summary>
/// Reads the top-level window list once per scan. This is the only place that
/// touches EnumWindows; the selection rules themselves live in
/// <see cref="WindowSelection"/> so they stay testable without a desktop.
/// </summary>
public static class ScreenWindowEnumerator
{
    private const int ClassNameLength = 256;
    private const int ExtendedFrameBounds = Dwm.DWMWA_EXTENDED_FRAME_BOUNDS;

    public static IReadOnlyList<ScreenWindow> Enumerate()
    {
        var windows = new List<ScreenWindow>(64);

        try
        {
            User32.EnumWindows((handle, _) =>
            {
                windows.Add(Describe(handle));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No desktop window manager: hints fall back to the foreground
            // handle only, which the scanner still adds explicitly.
        }

        return windows;
    }

    private static ScreenWindow Describe(IntPtr handle)
    {
        User32.GetWindowThreadProcessId(handle, out uint processId);

        return new ScreenWindow(
            handle,
            BoundsOf(handle),
            processId,
            User32.IsWindowVisible(handle),
            IsCloaked(handle),
            User32.IsIconic(handle),
            User32.GetWindow(handle, User32.GW_OWNER),
            ClassNameOf(handle));
    }

    /// <summary>
    /// Extended frame bounds, not GetWindowRect: the invisible resize border of
    /// aero windows would inflate every target by a few pixels and shift the
    /// click point away from the control.
    /// </summary>
    private static Rectangle BoundsOf(IntPtr handle)
    {
        RECT rect = default;
        int hr = Dwm.DwmGetWindowAttribute(
            handle,
            ExtendedFrameBounds,
            out rect,
            Marshal.SizeOf<RECT>());

        if (hr != 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            if (!User32.GetWindowRect(handle, out rect))
            {
                return Rectangle.Empty;
            }
        }

        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    /// <summary>
    /// UWP apps keep hidden ghost windows around; they are invisible but
    /// enum-visible and would add phantom targets on every scan.
    /// </summary>
    private static bool IsCloaked(IntPtr handle)
    {
        try
        {
            return Dwm.DwmGetWindowAttribute(
                handle,
                Dwm.DWMWA_CLOAKED,
                out int cloaked,
                sizeof(int)) == 0 && cloaked != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static string ClassNameOf(IntPtr handle)
    {
        var buffer = new StringBuilder(ClassNameLength);
        int length = User32.GetClassName(handle, buffer, ClassNameLength);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }
}
