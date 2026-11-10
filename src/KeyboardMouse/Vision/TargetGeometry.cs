using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>
/// What went wrong (or nearly went wrong) with one target's geometry. Reported
/// instead of silently corrected so a bad box can be explained later.
/// </summary>
[Flags]
public enum GeometryIssue
{
    None = 0,

    /// <summary>Empty or negative bounds.</summary>
    ZeroSize = 1 << 0,

    /// <summary>Smaller than the smallest plausible control of its type.</summary>
    TooSmall = 1 << 1,

    /// <summary>Larger than the largest plausible control of its type.</summary>
    TooLarge = 1 << 2,

    /// <summary>Entirely outside the captured frame.</summary>
    OffFrame = 1 << 3,

    /// <summary>No click point could be placed inside the visible bounds.</summary>
    ClickOutside = 1 << 4,

    /// <summary>Partly outside the frame; the visible part is still usable.</summary>
    ClippedByFrame = 1 << 5,

    /// <summary>Overlaps another target: the two boxes describe the same pixels.</summary>
    NeighbourOverlap = 1 << 6,

    /// <summary>Touches another target with no gap to spare.</summary>
    NeighbourTooClose = 1 << 7,

    /// <summary>More than a fifth of an edge was cut away as a halo or shadow.</summary>
    TrimmedHeavily = 1 << 8,

    /// <summary>The click point had to move away from the box centre.</summary>
    ClickShifted = 1 << 9,

    /// <summary>This box sits inside a larger target that was also found.</summary>
    InsideLargerTarget = 1 << 10,

    /// <summary>No visible boundary: the box sits on a flat, undefined area.</summary>
    LowDefinition = 1 << 11,

    /// <summary>The shape is not a filled rectangle; the centre is not necessarily inside it.</summary>
    IrregularShape = 1 << 12,

    /// <summary>Any of these makes the box unusable as a click target.</summary>
    Fatal = ZeroSize | TooSmall | TooLarge | OffFrame | ClickOutside
}

/// <summary>
/// The geometry of one target: where it is, where a safe click lands, and how
/// much of that can be believed. Click point and bounds are in capture pixels;
/// <see cref="ClickPoint"/> is always inside <see cref="Bounds"/> when
/// <see cref="IsClickable"/> is true.
/// </summary>
public sealed record TargetGeometry(
    Rectangle Bounds,
    Rectangle SafeRegion,
    Point ClickPoint,
    double Confidence,
    GeometryIssue Issues,
    string Notes)
{
    public bool IsValid => (Issues & GeometryIssue.Fatal) == 0;

    public bool IsClickable =>
        IsValid && Bounds.Width > 0 && Bounds.Height > 0 && Bounds.Contains(ClickPoint);

    /// <summary>How far the click point is from the geometric centre.</summary>
    public int ShiftPixels
    {
        get
        {
            int dx = ClickPoint.X - (Bounds.Left + (Bounds.Width / 2));
            int dy = ClickPoint.Y - (Bounds.Top + (Bounds.Height / 2));
            return (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)));
        }
    }

    public static TargetGeometry Empty(Rectangle bounds, GeometryIssue issue, string notes) => new(
        bounds,
        Rectangle.Empty,
        Point.Empty,
        0,
        issue,
        notes);
}

/// <summary>
/// Plausible geometry per control type. A radio ring is 10-20 px, a dialog
/// button is 24-400 px wide, a card is allowed to be large — one shared limit
/// would either reject every small control or accept a whole window as a button.
/// </summary>
public readonly record struct GeometryConstraints(
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    int HitPadding)
{
    public static GeometryConstraints For(ControlType type) => type switch
    {
        ControlType.Button => new(22, 14, 600, 160, 0),
        ControlType.IconButton or ControlType.ToolbarItem => new(11, 11, 96, 96, 4),
        ControlType.ToggleButton => new(18, 12, 320, 80, 0),
        ControlType.Tab => new(18, 14, 320, 80, 0),
        ControlType.Link => new(14, 9, 900, 44, 0),
        ControlType.TextField => new(32, 14, 900, 120, 0),
        ControlType.Dropdown => new(26, 14, 480, 80, 0),
        ControlType.Checkbox or ControlType.RadioButton => new(9, 9, 44, 44, 0),
        ControlType.Switch => new(20, 9, 160, 48, 0),
        ControlType.Slider => new(36, 7, 1200, 48, 0),
        ControlType.Scrollbar => new(5, 20, 48, 1600, 0),
        ControlType.MenuItem => new(22, 11, 600, 60, 0),
        ControlType.Card => new(40, 36, 4000, 3000, 0),
        ControlType.Image => new(20, 20, 4000, 3000, 0),
        ControlType.Separator => new(6, 1, 4000, 24, 0),
        ControlType.StaticLabel => new(6, 6, 4000, 400, 0),
        ControlType.DecorativeIcon => new(4, 4, 400, 400, 0),
        _ => new(6, 6, 4000, 3000, 0)
    };
}

/// <summary>
/// Turns a detected box into a usable click target.
///
/// Detection finds a box; a box is not yet a target. A shadow widens it, a
/// rounded corner leaves its corner pixels empty, a glyph is a 14 px shape that
/// a mouse can barely hit, and two neighbours make the centre a bad guess. This
/// trims the halo, grows undersized glyphs to a hittable size without eating the
/// gap to a neighbour, picks a click point that stays clear of edges and
/// neighbours, and reports how much of that was guesswork.
/// </summary>
public static class GeometryEstimator
{
    /// <summary>How far from an edge a boundary may sit and still be found.</summary>
    public const int EdgeWindow = 6;

    /// <summary>Preferred smallest clickable side for a glyph-shaped control.</summary>
    public const int GlyphHitSize = 24;

    /// <summary>A click point closer than this to another target is moved.</summary>
    public const int NeighbourClearance = 6;

    private const int InkDelta = 26;
    private const double EdgeStrength = 9;

    public static TargetGeometry Estimate(
        ControlType type,
        Rectangle bounds,
        Image32 image,
        IReadOnlyList<Rectangle>? neighbours = null,
        Rectangle? frame = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        neighbours ??= Array.Empty<Rectangle>();
        var area = frame ?? new Rectangle(0, 0, image.Width, image.Height);
        var constraints = GeometryConstraints.For(type);
        var issues = GeometryIssue.None;
        var notes = new List<string>(3);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return TargetGeometry.Empty(bounds, GeometryIssue.ZeroSize, "empty bounds");
        }

        var visible = Rectangle.Intersect(bounds, area);

        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return TargetGeometry.Empty(bounds, GeometryIssue.OffFrame, "outside the captured frame");
        }

        if (visible != bounds)
        {
            issues |= GeometryIssue.ClippedByFrame;
            notes.Add("clipped by frame");
        }

        var refined = Refine(image, visible, type, out int trimmedEdges);
        int trimX = visible.Width - refined.Width;
        int trimY = visible.Height - refined.Height;

        // Per axis: a small ring can lose its whole edge to the halo rule, and
        // that is the detector being right, not the control being undefined.
        bool heavyX = refined.Width <= 2 || trimX > Math.Max(2, visible.Width / 5);
        bool heavyY = refined.Height <= 2 || trimY > Math.Max(2, visible.Height / 5);

        if (heavyX || heavyY)
        {
            refined = heavyX && heavyY
                ? visible
                : Rectangle.FromLTRB(
                    heavyX ? visible.Left : refined.Left,
                    heavyY ? visible.Top : refined.Top,
                    heavyX ? visible.Right : refined.Right,
                    heavyY ? visible.Bottom : refined.Bottom);
            trimX = visible.Width - refined.Width;
            trimY = visible.Height - refined.Height;
            notes.Add("kept detected bounds");
        }
        else if (trimX >= 2 || trimY >= 2)
        {
            notes.Add($"trimmed {trimX}x{trimY} px");
        }

        // The fill and the boundary are properties of the control as drawn, so
        // they are measured before the hit area is grown: after the growth the
        // box edge is page background and every glyph would read as undefined.
        var fill = ModalLuma(image, Shrink(refined, 4));
        double definition = BoundaryDefinition(image, refined);

        refined = GrowToHitSize(refined, constraints, image, area, neighbours, out bool grown);

        if (grown)
        {
            notes.Add("expanded to hit size");
        }

        if (refined.Width < constraints.MinWidth || refined.Height < constraints.MinHeight)
        {
            issues |= GeometryIssue.TooSmall;
        }

        long frameArea = (long)area.Width * area.Height;
        long boxArea = (long)refined.Width * refined.Height;

        if (refined.Width > constraints.MaxWidth ||
            refined.Height > constraints.MaxHeight ||
            (boxArea > 0.92 * frameArea && type is not (ControlType.Card or ControlType.Image)))
        {
            issues |= GeometryIssue.TooLarge;
        }

        var inset = SafeInset(type, refined);
        var safe = Rectangle.Inflate(refined, -inset, -inset);

        if (safe.Width < 2 || safe.Height < 2)
        {
            safe = Rectangle.Intersect(refined, area);
        }

        safe = Rectangle.Intersect(safe, area);

        if (safe.Width <= 0 || safe.Height <= 0)
        {
            safe = Rectangle.Intersect(refined, area);
        }

        // A link is a text line and a separator is a rule: neither has a border
        // that the page colour can be compared against.
        bool borderlessByNature = type is ControlType.Card or ControlType.Image or
            ControlType.Separator or ControlType.Link or ControlType.StaticLabel or
            ControlType.Checkbox or ControlType.RadioButton or ControlType.DecorativeIcon;

        if (definition < 0.3 && !borderlessByNature)
        {
            issues |= GeometryIssue.LowDefinition;
            notes.Add($"boundary {definition:0.00}");
        }

        int overlapCount = 0;
        int closest = int.MaxValue;

        foreach (var neighbour in neighbours)
        {
            var intersection = Rectangle.Intersect(refined, neighbour);

            if (intersection.Width > 0 && intersection.Height > 0)
            {
                long inter = (long)intersection.Width * intersection.Height;

                if (inter > 0.05 * Math.Min(boxArea, (long)neighbour.Width * neighbour.Height))
                {
                    overlapCount++;
                }
            }

            closest = Math.Min(closest, Gap(refined, neighbour));
        }

        if (overlapCount > 0)
        {
            issues |= GeometryIssue.NeighbourOverlap;
            notes.Add($"{overlapCount} overlapping neighbour(s)");
        }
        else if (closest <= 1)
        {
            issues |= GeometryIssue.NeighbourTooClose;
        }

        var ink = NearestInk(image, refined, fill, Center(refined));
        bool irregular = ink is not null && IsIrregular(image, type, refined, fill, ink.Value);

        if (irregular)
        {
            issues |= GeometryIssue.IrregularShape;
        }

        var (click, shifted) = ChooseClickPoint(refined, safe, ink, irregular, neighbours, area);

        if (!refined.Contains(click))
        {
            issues |= GeometryIssue.ClickOutside;
        }

        if (shifted)
        {
            issues |= GeometryIssue.ClickShifted;
            notes.Add("click point moved from the centre");
        }

        if (neighbours.Any(n => n.Contains(refined) && boxArea < 0.5 * (long)n.Width * n.Height))
        {
            issues |= GeometryIssue.InsideLargerTarget;
            notes.Add("inside a larger target");
        }

        double confidence = 1.0;

        if ((issues & GeometryIssue.ClippedByFrame) != 0) confidence -= 0.18;
        if ((issues & GeometryIssue.LowDefinition) != 0) confidence -= 0.18;
        if ((issues & GeometryIssue.TrimmedHeavily) != 0) confidence -= 0.08;
        if ((issues & GeometryIssue.NeighbourOverlap) != 0) confidence -= 0.22;
        else if ((issues & GeometryIssue.NeighbourTooClose) != 0) confidence -= 0.08;
        if ((issues & GeometryIssue.IrregularShape) != 0) confidence -= 0.05;
        if ((issues & GeometryIssue.InsideLargerTarget) != 0) confidence -= 0.12;

        foreach (var issue in new[] { GeometryIssue.TooSmall, GeometryIssue.TooLarge, GeometryIssue.ClickOutside })
        {
            if ((issues & issue) != 0)
            {
                confidence -= 0.35;
            }
        }

        confidence = Math.Clamp(confidence, 0.02, 1.0);

        if (notes.Count == 0)
        {
            notes.Add("clean");
        }

        return new TargetGeometry(
            refined,
            safe,
            click,
            Math.Round(confidence, 3),
            issues,
            string.Join("; ", notes));
    }

    /// <summary>Runs <see cref="Estimate"/> for every control, using the other
    /// interactive controls as neighbours so no click point lands on a sibling.</summary>
    public static IReadOnlyList<ControlCandidate> Attach(
        Image32 image,
        IReadOnlyList<ControlCandidate> controls)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(controls);

        if (controls.Count == 0)
        {
            return controls;
        }

        var frame = new Rectangle(0, 0, image.Width, image.Height);
        var interactive = new List<Rectangle>(controls.Count);

        for (int i = 0; i < controls.Count; i++)
        {
            if (controls[i].IsInteractive)
            {
                interactive.Add(controls[i].Bounds);
            }
        }

        var result = new ControlCandidate[controls.Count];

        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            var others = new List<Rectangle>(Math.Max(0, interactive.Count - 1));

            foreach (var rect in interactive)
            {
                if (rect != control.Bounds)
                {
                    others.Add(rect);
                }
            }

            result[i] = control with
            {
                Geometry = Estimate(control.Type, control.Bounds, image, others, frame)
            };
        }

        return result;
    }

    public static int SafeInset(ControlType type, Rectangle box)
    {
        int half = Math.Min(box.Width, box.Height);

        return type switch
        {
            ControlType.Checkbox or ControlType.RadioButton or ControlType.Switch => 2,
            ControlType.Slider or ControlType.Scrollbar or ControlType.Separator => 1,
            ControlType.Link => 2,
            _ => Math.Clamp(half / 8, 2, 6)
        };
    }

    // ---------- bounds ----------

    /// <summary>
    /// Pulls each edge inward onto the strongest nearby boundary. A drop shadow
    /// or a 2 px antialias halo makes the detected box bigger than the control;
    /// the real edge is the strongest gradient within a few pixels of it.
    /// </summary>
    private static Rectangle Refine(Image32 image, Rectangle box, ControlType type, out int trimmedEdges)
    {
        trimmedEdges = 0;

        if (box.Width < 4 || box.Height < 4)
        {
            return box;
        }

        int left = box.Left;
        int top = box.Top;
        int right = box.Right;
        int bottom = box.Bottom;
        int innerTop = top + Math.Min(3, box.Height / 8);
        int innerBottom = bottom - Math.Min(3, box.Height / 8);
        int innerLeft = left + Math.Min(3, box.Width / 8);
        int innerRight = right - Math.Min(3, box.Width / 8);

        int maxStepX = Math.Max(1, Math.Min(EdgeWindow, box.Width / 4));
        int maxStepY = Math.Max(1, Math.Min(EdgeWindow, box.Height / 4));

        int snappedLeft = SnapLeft(image, box, left, innerTop, innerBottom, maxStepX);
        int snappedRight = SnapRight(image, box, right, innerTop, innerBottom, maxStepX);
        int snappedTop = SnapTop(image, box, top, innerLeft, innerRight, maxStepY);
        int snappedBottom = SnapBottom(image, box, bottom, innerLeft, innerRight, maxStepY);

        if (snappedLeft > left) { left = snappedLeft; trimmedEdges++; }
        if (snappedRight < right) { right = snappedRight; trimmedEdges++; }
        if (snappedTop > top) { top = snappedTop; trimmedEdges++; }
        if (snappedBottom < bottom) { bottom = snappedBottom; trimmedEdges++; }

        // Borderless controls have no strong edge to snap to: drop strips that
        // match the surrounding background instead.
        double outside = OutsideLuma(image, Rectangle.FromLTRB(left, top, right, bottom));
        int maxTrim = type is ControlType.Link or ControlType.Separator ? 1 : 4;

        for (int i = 0; i < maxTrim; i++)
        {
            bool changed = false;

            if (left + 4 < right &&
                BackgroundStrip(image, new Rectangle(left, top + 1, 1, bottom - top - 2), outside) &&
                !BackgroundStrip(image, new Rectangle(left + 1, top + 1, 1, bottom - top - 2), outside))
            {
                left++;
                trimmedEdges++;
                changed = true;
            }

            if (right - 4 > left &&
                BackgroundStrip(image, new Rectangle(right - 1, top + 1, 1, bottom - top - 2), outside) &&
                !BackgroundStrip(image, new Rectangle(right - 2, top + 1, 1, bottom - top - 2), outside))
            {
                right--;
                trimmedEdges++;
                changed = true;
            }

            if (top + 4 < bottom &&
                BackgroundStrip(image, new Rectangle(left + 1, top, right - left - 2, 1), outside) &&
                !BackgroundStrip(image, new Rectangle(left + 1, top + 1, right - left - 2, 1), outside))
            {
                top++;
                trimmedEdges++;
                changed = true;
            }

            if (bottom - 4 > top &&
                BackgroundStrip(image, new Rectangle(left + 1, bottom - 1, right - left - 2, 1), outside) &&
                !BackgroundStrip(image, new Rectangle(left + 1, bottom - 2, right - left - 2, 1), outside))
            {
                bottom--;
                trimmedEdges++;
                changed = true;
            }

            if (!changed)
            {
                break;
            }
        }

        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    /// <summary>
    /// Grows a glyph-sized target to something a mouse can hit, but never into a
    /// neighbour: half of the free gap is the budget on each side.
    /// </summary>
    private static Rectangle GrowToHitSize(
        Rectangle box,
        GeometryConstraints constraints,
        Image32 image,
        Rectangle frame,
        IReadOnlyList<Rectangle> neighbours,
        out bool grown)
    {
        grown = false;

        if (constraints.HitPadding <= 0)
        {
            return box;
        }

        int minSide = constraints.HitPadding * 4;

        if (box.Width >= minSide && box.Height >= minSide)
        {
            return box;
        }

        int growX = GrowBudget(box, neighbours, frame, horizontal: true);
        int growY = GrowBudget(box, neighbours, frame, horizontal: false);

        // Growing is for hittability, not for guessing a bigger control: a
        // quarter of the side is the most the box may claim beyond the pixels
        // that were actually detected.
        int targetX = Math.Min(GlyphHitSize, box.Width + Math.Max(2, box.Width / 4));
        int targetY = Math.Min(GlyphHitSize, box.Height + Math.Max(2, box.Height / 4));
        int wantX = Math.Max(0, (targetX - box.Width) / 2);
        int wantY = Math.Max(0, (targetY - box.Height) / 2);
        int dx = Math.Min(wantX, growX);
        int dy = Math.Min(wantY, growY);

        if (dx == 0 && dy == 0)
        {
            return box;
        }

        var grownBox = Rectangle.Inflate(box, dx, dy);
        grownBox = Rectangle.Intersect(grownBox, frame);
        grown = grownBox.Width > box.Width || grownBox.Height > box.Height;
        return grownBox;
    }

    private static int GrowBudget(Rectangle box, IReadOnlyList<Rectangle> neighbours, Rectangle frame, bool horizontal)
    {
        int start = horizontal ? box.Left : box.Top;
        int end = horizontal ? box.Right : box.Bottom;
        int limit = horizontal ? frame.Left : frame.Top;
        int far = horizontal ? frame.Right : frame.Bottom;

        int before = Math.Max(0, start - limit);
        int after = Math.Max(0, far - end);

        foreach (var neighbour in neighbours)
        {
            int nStart = horizontal ? neighbour.Left : neighbour.Top;
            int nEnd = horizontal ? neighbour.Right : neighbour.Bottom;

            if (nEnd <= start)
            {
                before = Math.Min(before, Math.Max(0, (start - nEnd - 1) / 2));
            }
            else if (nStart >= end)
            {
                after = Math.Min(after, Math.Max(0, (nStart - end - 1) / 2));
            }
            else if (horizontal && neighbour.Bottom > box.Top && neighbour.Top < box.Bottom)
            {
                before = 0;
                after = 0;
            }
            else if (!horizontal && neighbour.Right > box.Left && neighbour.Left < box.Right)
            {
                before = 0;
                after = 0;
            }
        }

        return Math.Min(before, after);
    }

    // ---------- click point ----------

    private static (Point Point, bool Shifted) ChooseClickPoint(
        Rectangle box,
        Rectangle safe,
        Point? ink,
        bool irregular,
        IReadOnlyList<Rectangle> neighbours,
        Rectangle frame)
    {
        var candidates = new List<(Point Point, double Score)>(3);
        var interior = Center(safe);

        if (safe.Contains(interior))
        {
            candidates.Add((interior, 1.0));
        }

        var boxMid = Center(box);

        if (safe.Contains(boxMid))
        {
            candidates.Add((boxMid, 0.98));
        }

        if (ink is { } inkPoint && box.Contains(inkPoint))
        {
            // Ink is part of what the user sees as the control. When the centre
            // is empty — a ring, an arc, a hollow glyph — the ink is the only
            // honest answer and outweighs every positional preference.
            candidates.Add((inkPoint, irregular ? 1.9 : 1.06));
        }

        if (candidates.Count == 0)
        {
            var clampX = Math.Clamp(interior.X, box.Left, box.Right - 1);
            var clampY = Math.Clamp(interior.Y, box.Top, box.Bottom - 1);
            var forced = new Point(clampX, clampY);
            bool moved = forced != Center(box);
            return (forced, moved);
        }

        double best = double.MinValue;
        Point chosen = candidates[0].Point;

        foreach (var (point, baseScore) in candidates)
        {
            double score = baseScore;

            score += 0.9 * NeighbourFactor(point, neighbours);
            score += 0.35 * FrameFactor(point, frame);
            score += 0.25 * InwardFactor(point, safe);

            if (!safe.Contains(point))
            {
                score -= 0.45;
            }

            if (score > best)
            {
                best = score;
                chosen = point;
            }
        }

        // A point that sits on a neighbour is not a click point: walk back toward
        // the interior until the gap is acceptable, or fall back to the safest
        // candidate that already had room.
        if (NeighbourGap(chosen, neighbours) < NeighbourClearance)
        {
            var retreat = Retreat(chosen, box, safe, neighbours);

            if (retreat is not null)
            {
                chosen = retreat.Value;
            }
            else
            {
                var ranked = candidates
                    .OrderByDescending(c => NeighbourGap(c.Point, neighbours))
                    .ThenByDescending(c => c.Score)
                    .First();
                chosen = ranked.Point;
            }
        }

        return (chosen, Distance(chosen, Center(box)) >= 2);
    }

    private static Point? Retreat(Point start, Rectangle box, Rectangle safe, IReadOnlyList<Rectangle> neighbours)
    {
        var target = Center(safe);
        int dx = Math.Sign(target.X - start.X);
        int dy = Math.Sign(target.Y - start.Y);

        for (int step = 1; step <= 12; step++)
        {
            var candidate = new Point(
                Math.Clamp(start.X + (dx * step), box.Left, box.Right - 1),
                Math.Clamp(start.Y + (dy * step), box.Top, box.Bottom - 1));

            if (NeighbourGap(candidate, neighbours) >= NeighbourClearance ||
                (dx == 0 && dy == 0))
            {
                return candidate;
            }
        }

        return null;
    }

    private static double NeighbourFactor(Point point, IReadOnlyList<Rectangle> neighbours)
    {
        double gap = NeighbourGap(point, neighbours);
        return Math.Clamp(gap / (NeighbourClearance * 2.0), 0, 1);
    }

    private static int NeighbourGap(Point point, IReadOnlyList<Rectangle> neighbours)
    {
        int best = int.MaxValue;

        foreach (var neighbour in neighbours)
        {
            var inflated = Rectangle.Inflate(neighbour, 1, 1);

            if (inflated.Contains(point))
            {
                return 0;
            }

            int dx = Math.Max(0, Math.Max(inflated.Left - point.X, point.X - (inflated.Right - 1)));
            int dy = Math.Max(0, Math.Max(inflated.Top - point.Y, point.Y - (inflated.Bottom - 1)));
            best = Math.Min(best, (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy))));
        }

        return best == int.MaxValue ? 999 : best;
    }

    private static double FrameFactor(Point point, Rectangle frame)
    {
        int gap = Math.Min(
            Math.Min(point.X - frame.Left, frame.Right - 1 - point.X),
            Math.Min(point.Y - frame.Top, frame.Bottom - 1 - point.Y));

        return Math.Clamp(gap / 12.0, 0, 1);
    }

    private static double InwardFactor(Point point, Rectangle safe)
    {
        if (!safe.Contains(point))
        {
            return 0;
        }

        int gap = Math.Min(
            Math.Min(point.X - safe.Left, safe.Right - 1 - point.X),
            Math.Min(point.Y - safe.Top, safe.Bottom - 1 - point.Y));

        int half = Math.Max(1, Math.Min(safe.Width, safe.Height) / 2);
        return Math.Clamp(gap / (double)half, 0, 1);
    }

    // ---------- pixels ----------

    /// <summary>
    /// The ink pixel closest to the box centre. A centroid would put a ring's
    /// click in its empty middle; picking an actual ink pixel keeps the click on
    /// something the user can see is part of the control, and for text it lands
    /// on the nearest letter, which is where a button click belongs anyway.
    /// </summary>
    private static Point? NearestInk(Image32 image, Rectangle area, int fill, Point target)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        long size = (long)area.Width * area.Height;
        int step = size > 12000 ? 3 : size > 3000 ? 2 : 1;
        long best = long.MaxValue;
        Point? bestPoint = null;
        int count = 0;

        for (int y = area.Top; y < area.Bottom; y += step)
        {
            for (int x = area.Left; x < area.Right; x += step)
            {
                if (Math.Abs(image.Luma(x, y) - fill) < InkDelta)
                {
                    continue;
                }

                count++;
                long dx = x - target.X;
                long dy = y - target.Y;
                long distance = (dx * dx) + (dy * dy);

                if (distance < best)
                {
                    best = distance;
                    bestPoint = new Point(x, y);
                }
            }
        }

        return count < Math.Max(4, size / 400) ? null : bestPoint;
    }

    /// <summary>
    /// True when the control is a thin or hollow drawing whose ink leaves the
    /// box centre on background — the case where the geometric centre is a bad
    /// click point.
    /// </summary>
    private static bool IsIrregular(Image32 image, ControlType type, Rectangle box, int fill, Point ink)
    {
        if (type is ControlType.Button or ControlType.TextField or ControlType.Dropdown or
            ControlType.MenuItem or ControlType.Tab or ControlType.ToggleButton or ControlType.Card)
        {
            // A button whose centre happens to fall between two words is still a
            // filled box: the centre is inside the control, so there is nothing
            // to correct.
            return false;
        }

        var center = Center(box);

        if (!box.Contains(center))
        {
            return false;
        }

        bool centerIsInk = Math.Abs(image.Luma(center.X, center.Y) - fill) >= InkDelta;
        bool inkIsAway = Distance(ink, center) >= Math.Max(3, Math.Min(box.Width, box.Height) / 6);

        return !centerIsInk && inkIsAway;
    }

    /// <summary>
    /// Share of the box edge where inside and outside differ. Comparing against
    /// the fill instead would call a flat accent button undefined: its border is
    /// the same colour as its middle, and only the step to the page is real.
    /// </summary>
    private static double BoundaryDefinition(Image32 image, Rectangle box)
    {
        if (box.Width < 3 || box.Height < 3 || image.Width < 3 || image.Height < 3)
        {
            return 0;
        }

        int defined = 0;
        int total = 0;
        int stepX = box.Width > 64 ? 2 : 1;
        int stepY = box.Height > 64 ? 2 : 1;
        int innerLeft = Math.Clamp(box.Left, 0, image.Width - 1);
        int innerRight = Math.Clamp(box.Right - 1, 0, image.Width - 1);
        int innerTop = Math.Clamp(box.Top, 0, image.Height - 1);
        int innerBottom = Math.Clamp(box.Bottom - 1, 0, image.Height - 1);
        int outerLeft = Math.Clamp(box.Left - 1, 0, image.Width - 1);
        int outerRight = Math.Clamp(box.Right, 0, image.Width - 1);
        int outerTop = Math.Clamp(box.Top - 1, 0, image.Height - 1);
        int outerBottom = Math.Clamp(box.Bottom, 0, image.Height - 1);

        for (int x = box.Left; x < box.Right; x += stepX)
        {
            int cx = Math.Clamp(x, 0, image.Width - 1);
            defined += Math.Abs(image.Luma(cx, innerTop) - image.Luma(cx, outerTop)) >= 12 ? 1 : 0;
            defined += Math.Abs(image.Luma(cx, innerBottom) - image.Luma(cx, outerBottom)) >= 12 ? 1 : 0;
            total += 2;
        }

        for (int y = box.Top + 1; y < box.Bottom - 1; y += stepY)
        {
            int cy = Math.Clamp(y, 0, image.Height - 1);
            defined += Math.Abs(image.Luma(innerLeft, cy) - image.Luma(outerLeft, cy)) >= 12 ? 1 : 0;
            defined += Math.Abs(image.Luma(innerRight, cy) - image.Luma(outerRight, cy)) >= 12 ? 1 : 0;
            total += 2;
        }

        return total == 0 ? 0 : (double)defined / total;
    }

    private static int SnapLeft(Image32 image, Rectangle box, int start, int top, int bottom, int steps)
    {
        int bestX = start;
        double best = EdgeStrength;

        for (int i = 0; i <= steps; i++)
        {
            int x = start + i;

            if (x >= box.Right - 2 || x >= image.Width)
            {
                break;
            }

            double strength = ColumnGradient(image, x, top, bottom);

            if (strength > best)
            {
                best = strength;
                bestX = x;
            }
        }

        return bestX;
    }

    private static int SnapRight(Image32 image, Rectangle box, int start, int top, int bottom, int steps)
    {
        int bestX = start;
        double best = EdgeStrength;

        for (int i = 0; i <= steps; i++)
        {
            int x = start - i;

            if (x <= box.Left + 2 || x <= 0)
            {
                break;
            }

            double strength = ColumnGradient(image, x, top, bottom);

            if (strength > best)
            {
                best = strength;
                bestX = x;
            }
        }

        return bestX;
    }

    private static int SnapTop(Image32 image, Rectangle box, int start, int left, int right, int steps)
    {
        int bestY = start;
        double best = EdgeStrength;

        for (int i = 0; i <= steps; i++)
        {
            int y = start + i;

            if (y >= box.Bottom - 2 || y >= image.Height)
            {
                break;
            }

            double strength = RowGradient(image, y, left, right);

            if (strength > best)
            {
                best = strength;
                bestY = y;
            }
        }

        return bestY;
    }

    private static int SnapBottom(Image32 image, Rectangle box, int start, int left, int right, int steps)
    {
        int bestY = start;
        double best = EdgeStrength;

        for (int i = 0; i <= steps; i++)
        {
            int y = start - i;

            if (y <= box.Top + 2 || y <= 0)
            {
                break;
            }

            double strength = RowGradient(image, y, left, right);

            if (strength > best)
            {
                best = strength;
                bestY = y;
            }
        }

        return bestY;
    }

    private static double ColumnGradient(Image32 image, int x, int top, int bottom)
    {
        if (x <= 0 || x >= image.Width || bottom - top < 2)
        {
            return 0;
        }

        int sum = 0;

        for (int y = top; y < bottom; y++)
        {
            sum += Math.Abs(image.Luma(x, y) - image.Luma(x - 1, y));
        }

        return (double)sum / (bottom - top);
    }

    private static double RowGradient(Image32 image, int y, int left, int right)
    {
        if (y <= 0 || y >= image.Height || right - left < 2)
        {
            return 0;
        }

        int sum = 0;

        for (int x = left; x < right; x++)
        {
            sum += Math.Abs(image.Luma(x, y) - image.Luma(x, y - 1));
        }

        return (double)sum / (right - left);
    }

    private static bool BackgroundStrip(Image32 image, Rectangle strip, double outside)
    {
        if (strip.Width <= 0 || strip.Height <= 0)
        {
            return false;
        }

        double mean = MeanLuma(image, strip);
        return Math.Abs(mean - outside) <= 9;
    }

    private static double MeanLuma(Image32 image, Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(0, 0, image.Width, image.Height));

        if (area.Width <= 0 || area.Height <= 0)
        {
            return 0;
        }

        long sum = 0;
        long count = 0;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                sum += image.Luma(x, y);
                count++;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }

    /// <summary>Most common colour bucket inside the box: the fill, not the border.</summary>
    public static int ModalLuma(Image32 image, Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(0, 0, image.Width, image.Height));

        if (area.Width <= 0 || area.Height <= 0)
        {
            area = Rectangle.Intersect(area, new Rectangle(0, 0, image.Width, image.Height));

            if (area.Width <= 0 || area.Height <= 0)
            {
                return 0;
            }
        }

        var histogram = new int[32];
        int step = (long)area.Width * area.Height > 2000 ? 2 : 1;

        for (int y = area.Top; y < area.Bottom; y += step)
        {
            for (int x = area.Left; x < area.Right; x += step)
            {
                histogram[image.Luma(x, y) >> 3]++;
            }
        }

        int best = 0;

        for (int i = 1; i < histogram.Length; i++)
        {
            if (histogram[i] > histogram[best])
            {
                best = i;
            }
        }

        return (best * 8) + 4;
    }

    /// <summary>Mean luma of the 2 px ring just outside the box: the page colour.</summary>
    private static double OutsideLuma(Image32 image, Rectangle box)
    {
        var frame = new Rectangle(0, 0, image.Width, image.Height);
        var ring = Rectangle.Intersect(Rectangle.Inflate(box, 2, 2), frame);
        long sum = 0;
        long count = 0;

        // Four strips, not the inflated rectangle: a ring scan must cost the
        // perimeter, otherwise a card costs a full-image pass.
        for (int x = ring.Left; x < ring.Right; x++)
        {
            for (int d = 0; d < 2; d++)
            {
                int top = ring.Top + d;
                int bottom = ring.Bottom - 1 - d;

                if (top >= box.Top && top < box.Bottom)
                {
                    continue;
                }

                if (top >= frame.Top && top < frame.Bottom) { sum += image.Luma(x, top); count++; }
                if (bottom >= frame.Top && bottom < frame.Bottom) { sum += image.Luma(x, bottom); count++; }
            }
        }

        for (int y = Math.Max(box.Top, ring.Top); y < Math.Min(box.Bottom, ring.Bottom); y++)
        {
            for (int d = 0; d < 2; d++)
            {
                int left = ring.Left + d;
                int right = ring.Right - 1 - d;

                if (left >= frame.Left && left < frame.Right) { sum += image.Luma(left, y); count++; }
                if (right >= frame.Left && right < frame.Right) { sum += image.Luma(right, y); count++; }
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }

    private static Rectangle Shrink(Rectangle box, int amount)
    {
        var shrunk = Rectangle.Inflate(box, -amount, -amount);
        return shrunk.Width < 2 || shrunk.Height < 2 ? box : shrunk;
    }

    // ---------- small math ----------

    public static Point Center(Rectangle box) => new(
        box.Left + (box.Width / 2),
        box.Top + (box.Height / 2));

    private static int Distance(Point a, Point b) => (int)Math.Round(
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y))));

    /// <summary>Gap between two rectangles in pixels; 0 when they touch or overlap.</summary>
    public static int Gap(Rectangle a, Rectangle b)
    {
        int dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        int dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)));
    }
}
