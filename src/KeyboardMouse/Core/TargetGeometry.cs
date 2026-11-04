using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Non-fatal or fatal conditions observed while refining a target's geometry.
/// A flag is diagnostic: it never contains pixel data, text or a screenshot.
/// </summary>
[Flags]
public enum GeometryIssue
{
    None = 0,
    ZeroSized = 1 << 0,
    TooSmall = 1 << 1,
    ImplausiblyLarge = 1 << 2,
    OffScreen = 1 << 3,
    PartiallyOffScreen = 1 << 4,
    ClickPointOutside = 1 << 5,
    InvalidTransform = 1 << 6,
    WeakEdges = 1 << 7,
    ShadowIncluded = 1 << 8,
    RoundedCorners = 1 << 9,
    SharedBoundary = 1 << 10,
    OverlapsNeighbor = 1 << 11,
    AmbiguousShape = 1 << 12,
    ContainerLike = 1 << 13,
    UsedFallback = 1 << 14
}

/// <summary>Why a click point was chosen.</summary>
public enum ClickPointStrategy
{
    Center,
    SafeInterior,
    TextCenter,
    IconCenter,
    ShapeCenter,
    NeighborSafe
}

/// <summary>
/// Presentation-free geometry diagnostics for one target. Confidence is a
/// ranking signal, not a probability; a clearly bounded button scores higher
/// than a candidate whose edges are ambiguous.
/// </summary>
public sealed record TargetGeometry
{
    public static TargetGeometry Default { get; } = new();

    /// <summary>Geometry confidence in [0, 1].</summary>
    public double Confidence { get; init; } = 1.0;

    public ClickPointStrategy ClickStrategy { get; init; } = ClickPointStrategy.Center;

    public GeometryIssue Issues { get; init; }

    /// <summary>Fraction of the four edges supported by a visible boundary.</summary>
    public double EdgeSupport { get; init; } = 1.0;

    /// <summary>Normalized colour contrast between control and background.</summary>
    public double BackgroundContrast { get; init; } = 1.0;

    public bool HasRoundedCorners { get; init; }

    /// <summary>True when the target extends past the capture or monitor area.</summary>
    public bool IsClipped { get; init; }

    /// <summary>True when pixel refinement could not build a reliable mask.</summary>
    public bool UsedFallback { get; init; }

    public bool HasIssue(GeometryIssue issue) => (Issues & issue) != 0;
}

/// <summary>Physical-pixel size limits for one target type.</summary>
public readonly record struct TargetSizeLimits(
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    double MaxAspect);

/// <summary>The result of validating target geometry.</summary>
public readonly record struct GeometryValidation(
    bool Accepted,
    TargetGeometry Geometry);

/// <summary>
/// Type-specific geometry checks. Bounds and click points are always in
/// canonical physical desktop pixels. Rules scale with the monitor DPI, but
/// never convert coordinates or change the canonical space.
/// </summary>
public static class TargetGeometryValidator
{
    private const double HardFailureConfidencePenalty = 0.25;

    public static TargetSizeLimits LimitsFor(TargetType type, double dpiScale = 1.0)
    {
        double scale = double.IsFinite(dpiScale) && dpiScale > 0
            ? Math.Clamp(dpiScale, 0.5, 4.0)
            : 1.0;

        // Minimums grow only with the square root of DPI: a control is larger
        // at 150%, but a genuinely tiny icon must stay small. Maximums grow
        // linearly, because a high-DPI window legitimately uses more pixels.
        double min = Math.Sqrt(scale);
        double max = scale;

        (int minWidth, int minHeight, int maxWidth, int maxHeight, double maxAspect) rule =
            type switch
            {
                TargetType.CheckBox => (5, 5, 200, 200, 3.0),
                TargetType.RadioButton => (5, 5, 200, 200, 3.0),
                TargetType.Icon => (5, 5, 320, 320, 6.0),
                TargetType.Edit => (16, 8, 4096, 800, 80.0),
                TargetType.ComboBox => (20, 8, 2048, 400, 40.0),
                TargetType.Button => (8, 6, 1800, 700, 40.0),
                TargetType.Hyperlink => (8, 6, 2400, 300, 50.0),
                TargetType.MenuItem => (8, 6, 2400, 400, 50.0),
                TargetType.TabItem => (8, 6, 1600, 400, 40.0),
                TargetType.ListItem => (6, 6, 3200, 1600, 60.0),
                TargetType.TreeItem => (6, 6, 3200, 1600, 60.0),
                TargetType.Text => (4, 4, 4000, 2000, 80.0),
                _ => (4, 4, 4096, 2400, 80.0)
            };

        return new TargetSizeLimits(
            Math.Max(1, (int)Math.Round(rule.minWidth * min)),
            Math.Max(1, (int)Math.Round(rule.minHeight * min)),
            Math.Max(1, (int)Math.Round(rule.maxWidth * max)),
            Math.Max(1, (int)Math.Round(rule.maxHeight * max)),
            rule.maxAspect);
    }

    /// <summary>
    /// Validates bounds and a click point. Strict mode rejects implausible
    /// geometry for CV candidates; non-strict mode keeps UIA targets but flags
    /// suspicious geometry so it ranks below a clear control.
    /// </summary>
    public static GeometryValidation Validate(
        TargetBounds bounds,
        Point clickPoint,
        TargetType type,
        Rectangle screen,
        double dpiScale = 1.0,
        bool strict = false,
        TargetGeometry? geometry = null)
    {
        TargetGeometry current = geometry ?? TargetGeometry.Default;
        GeometryIssue issues = current.Issues;
        double confidence = Math.Clamp(current.Confidence, 0.0, 1.0);
        bool accepted = true;
        bool clipped = current.IsClipped;

        if (!bounds.IsValid)
        {
            return Reject(
                current,
                GeometryIssue.ZeroSized,
                strict,
                "Target bounds must be positive.");
        }

        if (!bounds.Contains(clickPoint))
        {
            return Reject(
                current,
                GeometryIssue.ClickPointOutside,
                strict,
                "The click point must be inside the target bounds.");
        }

        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return Reject(
                current,
                GeometryIssue.InvalidTransform,
                strict,
                "The capture/screen rectangle is invalid.");
        }

        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return Reject(
                current,
                GeometryIssue.InvalidTransform,
                strict,
                "The DPI scale is invalid.");
        }

        TargetBounds screenBounds = TargetBounds.FromRectangle(screen);
        TargetBounds visible = bounds.Intersect(screenBounds);

        if (!visible.IsValid)
        {
            return Reject(
                current,
                GeometryIssue.OffScreen,
                strict,
                "The target is outside the capture area.");
        }

        if (visible != bounds)
        {
            issues |= GeometryIssue.PartiallyOffScreen;
            clipped = true;
            confidence -= 0.15;

            long visibleArea = (long)visible.Width * visible.Height;
            long targetArea = (long)bounds.Width * bounds.Height;

            if (targetArea <= 0 || visibleArea * 100 < targetArea * 55)
            {
                return Reject(
                    current with
                    {
                        Issues = issues,
                        IsClipped = true,
                        Confidence = Math.Max(0.0, confidence)
                    },
                    GeometryIssue.OffScreen,
                    strict,
                    "Most of the target is outside the capture area.");
            }
        }

        TargetSizeLimits limits = LimitsFor(type, dpiScale);

        if (bounds.Width < limits.MinWidth || bounds.Height < limits.MinHeight)
        {
            issues |= GeometryIssue.TooSmall;
            confidence = Math.Min(confidence, 0.40);

            if (strict)
            {
                accepted = false;
            }
        }

        if (bounds.Width > limits.MaxWidth ||
            bounds.Height > limits.MaxHeight ||
            bounds.Width / (double)Math.Max(1, bounds.Height) > limits.MaxAspect)
        {
            issues |= GeometryIssue.ImplausiblyLarge;
            confidence = Math.Min(confidence, 0.35);

            if (strict)
            {
                accepted = false;
            }
        }

        long captureArea = (long)screen.Width * screen.Height;
        long boundsArea = (long)bounds.Width * bounds.Height;

        if (captureArea > 0 &&
            boundsArea * 100 >= captureArea * 70 &&
            type is not TargetType.Text)
        {
            issues |= GeometryIssue.ContainerLike;
            confidence = Math.Min(confidence, 0.30);

            if (strict)
            {
                accepted = false;
            }
        }

        if (issues.HasFlag(GeometryIssue.WeakEdges) ||
            issues.HasFlag(GeometryIssue.AmbiguousShape) ||
            current.UsedFallback)
        {
            confidence -= 0.10;
        }

        if (issues.HasFlag(GeometryIssue.ShadowIncluded))
        {
            confidence -= 0.03;
        }

        if (!accepted)
        {
            confidence -= HardFailureConfidencePenalty;
        }

        return new GeometryValidation(
            accepted,
            current with
            {
                Issues = issues,
                Confidence = Math.Clamp(confidence, 0.0, 0.99),
                IsClipped = clipped
            });
    }

    private static GeometryValidation Reject(
        TargetGeometry geometry,
        GeometryIssue issue,
        bool strict,
        string _)
    {
        TargetGeometry rejected = geometry with
        {
            Issues = geometry.Issues | issue,
            Confidence = Math.Clamp(geometry.Confidence - 0.30, 0.0, 0.35)
        };

        // Strict callers drop bad CV candidates. Non-strict callers keep the
        // target but receive a low-confidence diagnostic so it ranks lower.
        return new GeometryValidation(!strict, rejected);
    }
}

/// <summary>
/// Neighbour-aware click-point selection. It never changes bounds and never
/// moves a point outside the target; it only prefers a safe interior point when
/// another control shares or nearly shares an edge.
/// </summary>
public static class TargetGeometryNeighbors
{
    public const int DefaultGap = 3;

    /// <summary>
    /// Adjusts click points for a target list. Each returned target keeps its
    /// bounds and metadata, but its point and diagnostics may be updated.
    /// </summary>
    public static IReadOnlyList<VisualTarget> Protect(
        IReadOnlyList<VisualTarget> targets,
        int gap = DefaultGap)
    {
        if (targets.Count < 2)
        {
            return targets;
        }

        var protectedTargets = new List<VisualTarget>(targets.Count);

        foreach (VisualTarget target in targets)
        {
            var neighbours = new List<Rectangle>();
            foreach (VisualTarget other in targets)
            {
                if (ReferenceEquals(other, target))
                {
                    continue;
                }

                if (AreNeighbours(target.Bounds.ToRectangle(), other.Bounds.ToRectangle(), gap))
                {
                    neighbours.Add(other.Bounds.ToRectangle());
                }
            }

            if (neighbours.Count == 0)
            {
                protectedTargets.Add(target);
                continue;
            }

            Point point = ChooseSafePoint(
                target.Bounds.ToRectangle(),
                target.ClickPoint,
                neighbours);
            GeometryIssue issue = GeometryIssue.SharedBoundary;
            bool alreadyProtected =
                target.Geometry.ClickStrategy == ClickPointStrategy.NeighborSafe &&
                target.Geometry.HasIssue(GeometryIssue.SharedBoundary);

            foreach (Rectangle neighbour in neighbours)
            {
                Rectangle overlap = Rectangle.Intersect(target.Bounds.ToRectangle(), neighbour);
                if (overlap.Width > 0 && overlap.Height > 0)
                {
                    issue |= GeometryIssue.OverlapsNeighbor;
                }
            }

            TargetGeometry geometry = target.Geometry with
            {
                Issues = target.Geometry.Issues | issue,
                ClickStrategy = ClickPointStrategy.NeighborSafe,
                Confidence = alreadyProtected
                    ? target.Geometry.Confidence
                    : Math.Max(0.05, target.Geometry.Confidence - 0.02)
            };

            protectedTargets.Add(target with
            {
                ClickPoint = point,
                Geometry = geometry
            });
        }

        return protectedTargets;
    }

    /// <summary>
    /// Adjusts click points for a target list. Each returned target keeps its
    /// bounds and metadata, but its point and diagnostics may be updated.
    /// </summary>
    public static IReadOnlyList<ClickableTarget> Protect(
        IReadOnlyList<ClickableTarget> targets,
        int gap = DefaultGap)
    {
        if (targets.Count < 2)
        {
            return targets;
        }

        var protectedTargets = new List<ClickableTarget>(targets.Count);

        foreach (ClickableTarget target in targets)
        {
            var neighbours = new List<Rectangle>();
            foreach (ClickableTarget other in targets)
            {
                if (ReferenceEquals(other, target))
                {
                    continue;
                }

                if (AreNeighbours(target.Bounds, other.Bounds, gap))
                {
                    neighbours.Add(other.Bounds);
                }
            }

            if (neighbours.Count == 0)
            {
                protectedTargets.Add(target);
                continue;
            }

            Point point = ChooseSafePoint(target.Bounds, target.ClickPoint, neighbours);
            TargetGeometry geometry = target.Geometry ?? TargetGeometry.Default;
            GeometryIssue issue = GeometryIssue.SharedBoundary;
            bool alreadyProtected =
                geometry.ClickStrategy == ClickPointStrategy.NeighborSafe &&
                geometry.HasIssue(GeometryIssue.SharedBoundary);

            if (neighbours.Exists(rect => Rectangle.Intersect(target.Bounds, rect) is
                    { Width: > 0, Height: > 0 }))
            {
                issue |= GeometryIssue.OverlapsNeighbor;
            }

            geometry = geometry with
            {
                Issues = geometry.Issues | issue,
                ClickStrategy = ClickPointStrategy.NeighborSafe,
                Confidence = alreadyProtected
                    ? geometry.Confidence
                    : Math.Max(0.05, geometry.Confidence - 0.02)
            };

            protectedTargets.Add(target with
            {
                ClickPointOverride = point,
                Geometry = geometry
            });
        }

        return protectedTargets;
    }

    /// <summary>
    /// Adjusts a single click point for the supplied neighbouring bounds.
    /// </summary>
    public static Point ChooseSafePoint(
        Rectangle bounds,
        Point preferred,
        IReadOnlyList<Rectangle> neighbours)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || neighbours.Count == 0)
        {
            return preferred;
        }

        if (!bounds.Contains(preferred))
        {
            preferred = new Point(
                bounds.Left + (bounds.Width / 2),
                bounds.Top + (bounds.Height / 2));
        }

        int insetX = Math.Min(Math.Max(1, bounds.Width / 6), Math.Max(1, (bounds.Width - 1) / 2));
        int insetY = Math.Min(Math.Max(1, bounds.Height / 6), Math.Max(1, (bounds.Height - 1) / 2));

        int minX = bounds.Left + insetX;
        int maxX = bounds.Right - 1 - insetX;
        int minY = bounds.Top + insetY;
        int maxY = bounds.Bottom - 1 - insetY;

        if (minX > maxX)
        {
            minX = maxX = bounds.Left + (bounds.Width / 2);
        }

        if (minY > maxY)
        {
            minY = maxY = bounds.Top + (bounds.Height / 2);
        }

        int shiftX = Math.Max(2, bounds.Width / 3);
        int shiftY = Math.Max(2, bounds.Height / 3);

        List<int> xs = Sample(
            Math.Max(minX, preferred.X - shiftX),
            Math.Min(maxX, preferred.X + shiftX),
            preferred.X);

        List<int> ys = Sample(
            Math.Max(minY, preferred.Y - shiftY),
            Math.Min(maxY, preferred.Y + shiftY),
            preferred.Y);

        Point best = preferred;
        double bestScore = double.NegativeInfinity;

        foreach (int x in xs)
        {
            foreach (int y in ys)
            {
                var candidate = new Point(x, y);
                if (!bounds.Contains(candidate))
                {
                    continue;
                }

                double neighbourDistance = double.MaxValue;
                foreach (Rectangle neighbour in neighbours)
                {
                    neighbourDistance = Math.Min(
                        neighbourDistance,
                        DistanceToRectangle(candidate, neighbour));
                }

                if (double.IsPositiveInfinity(neighbourDistance))
                {
                    neighbourDistance = 0;
                }

                int borderDistance = Math.Min(
                    Math.Min(x - bounds.Left, bounds.Right - 1 - x),
                    Math.Min(y - bounds.Top, bounds.Bottom - 1 - y));
                double preferredDistance =
                    Math.Abs(x - preferred.X) + Math.Abs(y - preferred.Y);

                // Prefer distance from neighbours first, then a safe distance
                // from our own border, then the original point.
                double score =
                    (neighbourDistance * 3.0) +
                    (Math.Min(8, borderDistance) * 0.75) -
                    (preferredDistance * 0.20);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        return bounds.Contains(best) ? best : preferred;
    }

    public static bool AreNeighbours(Rectangle a, Rectangle b, int gap)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
        {
            return false;
        }

        int dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        int dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));

        return dx <= gap && dy <= gap;
    }

    public static double DistanceToRectangle(Point point, Rectangle rectangle)
    {
        int dx = Math.Max(
            Math.Max(rectangle.Left - point.X, point.X - (rectangle.Right - 1)),
            0);
        int dy = Math.Max(
            Math.Max(rectangle.Top - point.Y, point.Y - (rectangle.Bottom - 1)),
            0);

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static List<int> Sample(int min, int max, int centre)
    {
        var values = new List<int>(9) { centre };

        if (min < max)
        {
            values.Add(min);
            values.Add(max);
            values.Add(min + ((max - min) / 2));
        }

        values.Sort();
        return values.Distinct().ToList();
    }
}
