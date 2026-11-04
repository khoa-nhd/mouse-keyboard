using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>One clickable screen element, in physical pixels.</summary>
public sealed record ClickableTarget(Rectangle Bounds, string Name = "")
{
    /// <summary>
    /// A geometry-aware click point. When null or outside <see cref="Bounds"/>
    /// the target falls back to its center.
    /// </summary>
    public Point? ClickPointOverride { get; init; }

    /// <summary>Geometry diagnostics from the source scanner, when available.</summary>
    public TargetGeometry? Geometry { get; init; }

    public TargetType Type { get; init; } = TargetType.Unknown;

    public double GeometryConfidence => Geometry?.Confidence ?? 1.0;

    public Point Center => new(
        Bounds.Left + (Bounds.Width / 2),
        Bounds.Top + (Bounds.Height / 2));

    /// <summary>The click point within the target bounds.</summary>
    public Point ClickPoint =>
        ClickPointOverride is { } point && Bounds.Contains(point)
            ? point
            : Center;

    /// <summary>True when an explicit, in-bounds click point was supplied.</summary>
    public bool HasCustomClickPoint =>
        ClickPointOverride is { } point && Bounds.Contains(point);
}

/// <summary>Raw scanner data before filtering. Kept separate so filtering is testable.</summary>
public sealed record RawClickableTarget(
    Rectangle Bounds,
    bool IsOffscreen,
    bool IsEnabled,
    string Name = "",
    Point? ClickPoint = null,
    TargetGeometry? Geometry = null,
    TargetType Type = TargetType.Unknown);

/// <summary>
/// Turns a raw scanner dump into a clean hint list: visible on a monitor,
/// deduplicated by center, capped, readable top-to-bottom/left-to-right, and
/// with click points protected from adjacent controls.
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

            bool clipped = bounds != candidate.Bounds;
            TargetGeometry geometry = candidate.Geometry ??
                                      new TargetGeometry { Confidence = 0.98 };

            if (clipped)
            {
                geometry = geometry with
                {
                    Issues = geometry.Issues | GeometryIssue.PartiallyOffScreen,
                    IsClipped = true,
                    Confidence = Math.Max(0.05, geometry.Confidence - 0.15)
                };
            }

            Point desired = candidate.ClickPoint ?? center;
            Point clickPoint = HintCoordinates.ClampToBounds(
                desired,
                TargetBounds.FromRectangle(bounds));

            centers.Add(center);
            targets.Add(new ClickableTarget(bounds, candidate.Name)
            {
                ClickPointOverride = clickPoint,
                Geometry = geometry,
                Type = candidate.Type
            });
        }

        IReadOnlyList<ClickableTarget> ordered = targets
            .OrderBy(t => t.Bounds.Top)
            .ThenBy(t => t.Bounds.Left)
            .Take(cap)
            .ToArray();

        return TargetGeometryNeighbors.Protect(ordered);
    }
}
