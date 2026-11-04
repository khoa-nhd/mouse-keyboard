using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>One clickable screen element, in physical pixels.</summary>
public sealed record ClickableTarget(Rectangle Bounds, string Name = "")
{
    public Point Center => new(
        Bounds.Left + (Bounds.Width / 2),
        Bounds.Top + (Bounds.Height / 2));

    /// <summary>The click point within the target bounds. Defaults to the center.</summary>
    public Point ClickPoint => Center;
}

/// <summary>Raw UIA data before filtering. Kept separate so filtering is testable.</summary>
public sealed record RawClickableTarget(
    Rectangle Bounds,
    bool IsOffscreen,
    bool IsEnabled,
    string Name = "");

/// <summary>
/// Turns a raw UIA element dump into a clean hint list: visible on a monitor,
/// deduplicated by center, capped, and readable top-to-bottom/left-to-right.
/// </summary>
public static class HintTargetFilter
{
    private const int DuplicateCenterPixels = 4;

    public static IReadOnlyList<ClickableTarget> Apply(
        IEnumerable<RawClickableTarget> raw,
        IReadOnlyList<MonitorInfo> monitors,
        int maxElements)
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
            var center = new Point(
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

            if (duplicate)
            {
                continue;
            }

            centers.Add(center);
            targets.Add(new ClickableTarget(bounds, candidate.Name));
        }

        return targets
            .OrderBy(t => t.Bounds.Top)
            .ThenBy(t => t.Bounds.Left)
            .Take(cap)
            .ToArray();
    }
}
