using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>One clickable screen element, in physical pixels.</summary>
public sealed record ClickableTarget(Rectangle Bounds, string Name = "")
{
    /// <summary>
    /// Where the click should land when the box centre is not the safe spot.
    /// Set by the geometry pass for rounded, glyph shaped or irregularly drawn
    /// controls; null means the box centre.
    /// </summary>
    public Point? ClickPoint { get; init; }

    public Point Center => new(
        Bounds.Left + (Bounds.Width / 2),
        Bounds.Top + (Bounds.Height / 2));

    /// <summary>The point a click should actually use.</summary>
    public Point HitPoint => ClickPoint ?? Center;
}

/// <summary>Raw UIA data before filtering. Kept separate so filtering is testable.</summary>
public sealed record RawClickableTarget(
    Rectangle Bounds,
    bool IsOffscreen,
    bool IsEnabled,
    string Name = "")
{
    /// <summary>Top-level window the element was read from; zero when unknown.</summary>
    public IntPtr Window { get; init; }

    /// <summary>The accessibility element itself, for checks made while reading its window.</summary>
    public object? Source { get; init; }
}

/// <summary>
/// Turns a raw UIA element dump into a clean hint list: visible on a monitor,
/// deduplicated by center, capped, and readable top-to-bottom/left-to-right.
/// </summary>
public static class HintTargetFilter
{
    private const int DuplicateCenterPixels = 4;

    /// <summary>Share of the larger box two boxes must share to count as one control.</summary>
    public const double SameControlShare = 0.7;

    /// <summary>Whether two boxes overlap so much that they are the same control.</summary>
    public static bool SameControl(Rectangle a, Rectangle b)
    {
        var overlap = Rectangle.Intersect(a, b);

        if (overlap.Width <= 0 || overlap.Height <= 0)
        {
            return false;
        }

        long larger = Math.Max((long)a.Width * a.Height, (long)b.Width * b.Height);
        return (long)overlap.Width * overlap.Height >= SameControlShare * larger;
    }

    /// <summary>Width or height above which an unnamed element is panel-sized.</summary>
    public const int ContainerWidth = 600;

    public const int ContainerHeight = 200;

    /// <summary>
    /// Web pages mark whole sections as clickable (a click listener on a
    /// wrapper), so a page reported its content area, a code block or the
    /// composer frame as one more "button" with a label in its corner. An
    /// unnamed, panel-sized element that holds at least two other targets is
    /// such a wrapper: the controls inside it are the real targets.
    /// </summary>
    private static void DropContainers(List<ClickableTarget> targets)
    {
        if (targets.Count < 3)
        {
            return;
        }

        targets.RemoveAll(container =>
        {
            if (container.Name.Length > 0 ||
                (container.Bounds.Width <= ContainerWidth && container.Bounds.Height <= ContainerHeight))
            {
                return false;
            }

            int inside = 0;

            foreach (var other in targets)
            {
                if (!ReferenceEquals(other, container) &&
                    container.Bounds.Contains(other.Bounds) &&
                    other.Bounds != container.Bounds &&
                    ++inside >= 2)
                {
                    return true;
                }
            }

            return false;
        });
    }

    /// <param name="region">
    /// Rectangle the scan covered. Targets outside it belong to a window the
    /// scope excluded, so they are dropped instead of hinted.
    /// </param>
    /// <param name="visibility">
    /// Drops elements another window covers or that are scrolled out of their
    /// own window, and moves the click onto the visible part of the rest.
    /// </param>
    public static IReadOnlyList<ClickableTarget> Apply(
        IEnumerable<RawClickableTarget> raw,
        IReadOnlyList<MonitorInfo> monitors,
        int maxElements,
        Rectangle? region = null,
        TargetVisibility? visibility = null)
    {
        int cap = Math.Clamp(maxElements, 1, 2000);
        var targets = new List<ClickableTarget>(Math.Min(cap, 128));
        var centers = new List<Point>();

        foreach (var candidate in raw)
        {
            if (candidate.IsOffscreen || !candidate.IsEnabled)
            {
                continue;
            }

            if (candidate.Bounds.Width < 1 || candidate.Bounds.Height < 1)
            {
                continue;
            }

            Rectangle? visible = null;
            foreach (var monitor in monitors)
            {
                var intersection = Rectangle.Intersect(candidate.Bounds, monitor.Bounds);
                if (intersection.Width > 0 && intersection.Height > 0)
                {
                    visible = intersection;
                    break;
                }
            }

            if (visible is null)
            {
                continue;
            }

            var bounds = visible.Value;

            if (region is { Width: > 0, Height: > 0 } scope)
            {
                var clipped = Rectangle.Intersect(bounds, scope);
                if (clipped.Width <= 0 || clipped.Height <= 0)
                {
                    continue;
                }

                bounds = clipped;
            }

            Point? click = null;

            if (visibility is not null)
            {
                if (!visibility.TryResolve(candidate.Window, bounds, out var seen, out click))
                {
                    continue;
                }

                bounds = seen;
            }

            var center = click ?? new Point(
                bounds.Left + (bounds.Width / 2),
                bounds.Top + (bounds.Height / 2));

            bool duplicate = false;
            foreach (var existing in centers)
            {
                if (Math.Abs(existing.X - center.X) <= DuplicateCenterPixels &&
                    Math.Abs(existing.Y - center.Y) <= DuplicateCenterPixels)
                {
                    duplicate = true;
                    break;
                }
            }

            // A row and the link filling it, or a button and its inner part, are
            // one control to the user: two labels for it is only clutter.
            if (!duplicate)
            {
                foreach (var existing in targets)
                {
                    if (SameControl(existing.Bounds, bounds))
                    {
                        duplicate = true;
                        break;
                    }
                }
            }

            if (duplicate)
            {
                continue;
            }

            centers.Add(center);
            targets.Add(new ClickableTarget(bounds, candidate.Name) { ClickPoint = click });
        }

        DropContainers(targets);

        var ordered = targets
            .OrderBy(t => t.Bounds.Top)
            .ThenBy(t => t.Bounds.Left)
            .ToArray();

        if (ordered.Length <= cap)
        {
            return ordered;
        }

        // Too many targets for one set of labels. Taking the first ones would
        // leave the bottom of the screen without hints, so keep the reading
        // order and sample the whole list instead: every part of the screen
        // stays addressable.
        var spread = new List<ClickableTarget>(cap);
        double step = ordered.Length / (double)cap;

        for (int i = 0; i < cap; i++)
        {
            int index = (int)Math.Round(i * step);
            spread.Add(ordered[Math.Clamp(index, 0, ordered.Length - 1)]);
        }

        return spread;
    }
}
