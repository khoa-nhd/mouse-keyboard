using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Converts source-local coordinates into the canonical hint coordinate space:
/// physical pixels in the Windows virtual-desktop coordinate system. The origin
/// is (SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN), which may be negative. X grows
/// right, Y grows down, and rectangles use exclusive right/bottom edges.
///
/// UIA bounding rectangles already use this space in a PerMonitorV2 process.
/// Screen-capture frames are frame-local; pass their desktop origin and capture
/// scale here before creating a <see cref="VisualTarget"/>.
/// </summary>
public static class HintCoordinates
{
    /// <summary>
    /// Maps a frame-local rectangle into desktop pixels. <paramref name="scaleX"/>
    /// and <paramref name="scaleY"/> are desktop pixels per frame pixel; use 1
    /// for a native-resolution capture.
    /// </summary>
    public static TargetBounds FrameToDesktop(
        Rectangle frameBounds,
        Rectangle localBounds,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
        ValidateScale(scaleX, nameof(scaleX));
        ValidateScale(scaleY, nameof(scaleY));

        return new TargetBounds(
            frameBounds.X + ToDesktop(localBounds.X, scaleX),
            frameBounds.Y + ToDesktop(localBounds.Y, scaleY),
            ToDesktop(localBounds.Width, scaleX),
            ToDesktop(localBounds.Height, scaleY));
    }

    /// <summary>Maps a frame-local point into desktop pixels.</summary>
    public static Point FrameToDesktop(
        Rectangle frameBounds,
        Point localPoint,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
        ValidateScale(scaleX, nameof(scaleX));
        ValidateScale(scaleY, nameof(scaleY));

        return new Point(
            frameBounds.X + ToDesktop(localPoint.X, scaleX),
            frameBounds.Y + ToDesktop(localPoint.Y, scaleY));
    }

    /// <summary>
    /// Non-throwing variant of <see cref="FrameToDesktop(Rectangle, Rectangle,
    /// double, double)"/> for scanner input that may contain a malformed scale
    /// or an overflowing frame-local rectangle.
    /// </summary>
    public static bool TryFrameToDesktop(
        Rectangle frameBounds,
        Rectangle localBounds,
        double scaleX,
        double scaleY,
        out TargetBounds bounds)
    {
        bounds = default;

        if (!double.IsFinite(scaleX) || scaleX <= 0 ||
            !double.IsFinite(scaleY) || scaleY <= 0 ||
            localBounds.Width <= 0 || localBounds.Height <= 0)
        {
            return false;
        }

        double left = frameBounds.X + (localBounds.X / scaleX);
        double top = frameBounds.Y + (localBounds.Y / scaleY);
        double width = localBounds.Width / scaleX;
        double height = localBounds.Height / scaleY;

        if (!IsInt32(left) || !IsInt32(top) || !IsInt32(width) || !IsInt32(height))
        {
            return false;
        }

        bounds = new TargetBounds(
            (int)Math.Round(left, MidpointRounding.AwayFromZero),
            (int)Math.Round(top, MidpointRounding.AwayFromZero),
            (int)Math.Round(width, MidpointRounding.AwayFromZero),
            (int)Math.Round(height, MidpointRounding.AwayFromZero));

        return bounds.IsValid;
    }

    /// <summary>
    /// Non-throwing variant of <see cref="FrameToDesktop(Rectangle, Point,
    /// double, double)"/>.
    /// </summary>
    public static bool TryFrameToDesktop(
        Rectangle frameBounds,
        Point localPoint,
        double scaleX,
        double scaleY,
        out Point point)
    {
        point = default;

        if (!double.IsFinite(scaleX) || scaleX <= 0 ||
            !double.IsFinite(scaleY) || scaleY <= 0)
        {
            return false;
        }

        double x = frameBounds.X + (localPoint.X / scaleX);
        double y = frameBounds.Y + (localPoint.Y / scaleY);

        if (!IsInt32(x) || !IsInt32(y))
        {
            return false;
        }

        point = new Point(
            (int)Math.Round(x, MidpointRounding.AwayFromZero),
            (int)Math.Round(y, MidpointRounding.AwayFromZero));

        return true;
    }

    /// <summary>
    /// Maps a canonical desktop rectangle back into a frame's local pixels.
    /// Useful for diagnostics and for mapping overlay hit areas to a capture.
    /// </summary>
    public static Rectangle DesktopToFrame(
        Rectangle frameBounds,
        TargetBounds desktopBounds,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
        ValidateScale(scaleX, nameof(scaleX));
        ValidateScale(scaleY, nameof(scaleY));

        return new Rectangle(
            ToFrame(desktopBounds.X - frameBounds.X, scaleX),
            ToFrame(desktopBounds.Y - frameBounds.Y, scaleY),
            ToFrame(desktopBounds.Width, scaleX),
            ToFrame(desktopBounds.Height, scaleY));
    }

    /// <summary>Clips a target rectangle to a desktop or monitor rectangle.</summary>
    public static TargetBounds ClipTo(TargetBounds bounds, Rectangle clip) =>
        bounds.Intersect(TargetBounds.FromRectangle(clip));

    /// <summary>
    /// Clamps a point into a valid target rectangle. The result is always
    /// inside the bounds and safe to use as a click point.
    /// </summary>
    public static Point ClampToBounds(Point point, TargetBounds bounds)
    {
        if (!bounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds,
                "Cannot clamp a point into invalid bounds.");
        }

        return new Point(
            Math.Clamp(point.X, bounds.Left, bounds.Right - 1),
            Math.Clamp(point.Y, bounds.Top, bounds.Bottom - 1));
    }

    private static int ToDesktop(int frameValue, double scale) =>
        (int)Math.Round(frameValue / scale, MidpointRounding.AwayFromZero);

    private static int ToFrame(int desktopValue, double scale) =>
        (int)Math.Round(desktopValue * scale, MidpointRounding.AwayFromZero);

    private static void ValidateScale(double scale, string paramName)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                scale,
                "Scale must be a finite positive number.");
        }
    }

    private static bool IsInt32(double value) =>
        double.IsFinite(value) &&
        value >= int.MinValue &&
        value <= int.MaxValue;
}
