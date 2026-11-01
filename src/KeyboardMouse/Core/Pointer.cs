using System.Drawing;
using KeyboardMouse.Native;

namespace KeyboardMouse.Core;

/// <summary>
/// Reads the physical cursor position. Kept behind a tiny helper so
/// controllers can take a <c>Func&lt;Point&gt;</c> and tests can fake it.
/// </summary>
public static class Pointer
{
    public static Point Position() =>
        User32.GetCursorPos(out var point)
            ? new Point(point.X, point.Y)
            : Point.Empty;
}
