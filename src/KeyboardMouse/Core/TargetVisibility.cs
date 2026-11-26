using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Decides whether an accessible element can actually be seen and clicked, and
/// where.
///
/// UI Automation reports every element of a window, including the ones another
/// window is lying on top of and the ones scrolled out of the window's own
/// frame. Wide scan scopes read every window on the monitor, so on a normal
/// desktop close to half the hints pointed at controls hidden behind the front
/// window: labels drawn over unrelated content, clicks landing on whatever
/// covered them.
///
/// The test is the same one the click itself will meet: which top-level window
/// a point belongs to. A target is kept only where its own window answers.
/// The hit test is injected, so the rules stay testable without a desktop.
/// </summary>
public sealed class TargetVisibility
{
    /// <summary>Pixels an element may stick out of its window frame (rounded, shadowed edges).</summary>
    public const int FrameTolerance = 2;

    private readonly Func<Point, IntPtr> _rootAt;
    private readonly IReadOnlyDictionary<IntPtr, Rectangle> _frames;

    /// <param name="rootAt">Top-level window that owns a screen point (a click there reaches it).</param>
    /// <param name="frames">Frame of every scanned window, in physical pixels.</param>
    public TargetVisibility(Func<Point, IntPtr> rootAt, IReadOnlyDictionary<IntPtr, Rectangle> frames)
    {
        _rootAt = rootAt ?? throw new ArgumentNullException(nameof(rootAt));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
    }

    /// <summary>
    /// The visible part of a target and the point to click, or false when no
    /// part of it can be reached. A target from an unknown window (no handle,
    /// no frame) is trusted as it is: there is nothing to check it against.
    /// </summary>
    public bool TryResolve(IntPtr window, Rectangle bounds, out Rectangle visible, out Point? click)
    {
        visible = bounds;
        click = null;

        if (window == IntPtr.Zero || !_frames.TryGetValue(window, out var frame) || frame.Width <= 0 || frame.Height <= 0)
        {
            return true;
        }

        // Content scrolled out of a list or a page is still in the tree, with
        // real coordinates outside the frame. It is not on screen.
        visible = Rectangle.Intersect(bounds, Rectangle.Inflate(frame, FrameTolerance, FrameTolerance));

        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return false;
        }

        var centre = Centre(visible);

        if (Reaches(window, centre))
        {
            return true;
        }

        // Partly covered: try the rest of the box on a 3x3 lattice and keep
        // the first cell its own window still owns. The label moves to that
        // cell, so it is not drawn over the window that covers the rest.
        foreach (var cell in Cells(visible))
        {
            var point = Centre(cell);

            if (point != centre && Reaches(window, point))
            {
                visible = cell;
                click = point;
                return true;
            }
        }

        return false;
    }

    private bool Reaches(IntPtr window, Point point)
    {
        try
        {
            return _rootAt(point) == window;
        }
        catch
        {
            // A failed hit test must not hide a target that may be fine.
            return true;
        }
    }

    private static Point Centre(Rectangle r) => new(r.Left + (r.Width / 2), r.Top + (r.Height / 2));

    /// <summary>Nine cells of the box, middle row first, then the rest top to bottom.</summary>
    private static IEnumerable<Rectangle> Cells(Rectangle box)
    {
        int[] xs = [box.Left, box.Left + (box.Width / 3), box.Left + (2 * box.Width / 3), box.Right];
        int[] ys = [box.Top, box.Top + (box.Height / 3), box.Top + (2 * box.Height / 3), box.Bottom];
        int[] rows = [1, 0, 2];

        foreach (int row in rows)
        {
            for (int column = 0; column < 3; column++)
            {
                var cell = Rectangle.FromLTRB(xs[column], ys[row], xs[column + 1], ys[row + 1]);

                if (cell.Width > 0 && cell.Height > 0)
                {
                    yield return cell;
                }
            }
        }
    }
}
