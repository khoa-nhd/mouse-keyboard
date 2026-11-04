using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Deterministic, source-neutral similarity measurements for two targets. The
/// functions operate on canonical desktop pixels and never inspect pixels or
/// text outside the supplied target values.
/// </summary>
public static class TargetSimilarity
{
    public static double IoU(TargetBounds a, TargetBounds b)
    {
        long intersection = IntersectionArea(a, b);
        if (intersection == 0)
        {
            return 0;
        }

        long union = Area(a) + Area(b) - intersection;
        return union > 0 ? intersection / (double)union : 0;
    }

    /// <summary>
    /// Intersection divided by the smaller rectangle's area. This is the useful
    /// containment signal when a text/icon box sits inside a button: IoU is
    /// tiny, but the small box is fully explained by the larger control.
    /// </summary>
    public static double IntersectionOverSmaller(TargetBounds a, TargetBounds b)
    {
        long intersection = IntersectionArea(a, b);
        long smaller = Math.Min(Area(a), Area(b));
        return smaller > 0 ? intersection / (double)smaller : 0;
    }

    public static double NormalizedCenterDistance(TargetBounds a, TargetBounds b)
    {
        double dx = a.Center.X - b.Center.X;
        double dy = a.Center.Y - b.Center.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double maxDiagonal = Math.Max(Diagonal(a), Diagonal(b));

        return maxDiagonal > 0
            ? Math.Min(1.0, distance / maxDiagonal)
            : 1.0;
    }

    public static double SizeSimilarity(TargetBounds a, TargetBounds b)
    {
        double width = Ratio(a.Width, b.Width);
        double height = Ratio(a.Height, b.Height);
        return (width + height) / 2.0;
    }

    public static double AspectSimilarity(TargetBounds a, TargetBounds b)
    {
        double aspectA = a.Width / (double)Math.Max(1, a.Height);
        double aspectB = b.Width / (double)Math.Max(1, b.Height);
        return Ratio(aspectA, aspectB);
    }

    public static double ShapeSimilarity(TargetBounds a, TargetBounds b) =>
        (0.55 * AspectSimilarity(a, b)) + (0.45 * SizeSimilarity(a, b));

    /// <summary>
    /// Text similarity in [0, 1]. Both-empty is neutral (0.5), a missing side
    /// gets no text bonus, exact matches score 1, and token overlap/Levenshtein
    /// handle OCR variations and captions such as "Save" vs "Save all".
    /// </summary>
    public static double TextSimilarity(string? a, string? b)
    {
        string left = NormalizeText(a);
        string right = NormalizeText(b);

        if (left.Length == 0 && right.Length == 0)
        {
            return 0.5;
        }

        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1.0;
        }

        double edit = 1.0 - (LevenshteinDistance(left, right) /
                             (double)Math.Max(left.Length, right.Length));
        double tokens = TokenJaccard(left, right);

        return Math.Clamp(Math.Max(edit, tokens), 0, 1);
    }

    public static double TypeCompatibility(TargetType a, TargetType b)
    {
        if (a == b)
        {
            return 1.0;
        }

        if (a == TargetType.Unknown || b == TargetType.Unknown)
        {
            return 0.5;
        }

        if (IsButtonLike(a) && IsButtonLike(b))
        {
            return 0.75;
        }

        if (IsToggleLike(a) && IsToggleLike(b))
        {
            return 0.75;
        }

        if (IsTextEntryLike(a) && IsTextEntryLike(b))
        {
            return 0.70;
        }

        if (IsTextLike(a) && IsTextLike(b))
        {
            return 0.60;
        }

        // A text/icon label is often nested in a button. That is a containment
        // signal, not a claim that the semantic types are the same.
        if (IsDecorative(a) || IsDecorative(b))
        {
            return 0.20;
        }

        return 0;
    }

    /// <summary>
    /// Alignment and proximity used by fusion. It deliberately rewards
    /// containment (label inside a button) but only weakly rewards a nearby
    /// control, so adjacent buttons are not fused by proximity alone.
    /// </summary>
    public static double VisualGrouping(TargetBounds a, TargetBounds b)
    {
        if (a.Contains(b) || b.Contains(a))
        {
            return 1.0;
        }

        long intersection = IntersectionArea(a, b);
        double centerProximity = 1.0 - NormalizedCenterDistance(a, b);
        double alignment = Alignment(a, b);
        double proximity;

        if (intersection > 0)
        {
            proximity = 0.85;
        }
        else
        {
            double maxDimension = Math.Max(
                Math.Max(a.Width, a.Height),
                Math.Max(b.Width, b.Height));
            double gapX = Math.Max(0, Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right));
            double gapY = Math.Max(0, Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom));
            double gap = Math.Sqrt((gapX * gapX) + (gapY * gapY));
            proximity = maxDimension > 0
                ? Math.Clamp(1.0 - (gap / maxDimension), 0, 1)
                : 0;
        }

        return Math.Clamp(
            (0.50 * proximity) + (0.30 * alignment) + (0.20 * centerProximity),
            0,
            1);
    }

    /// <summary>
    /// Returns true when two targets describe the same control, with a
    /// likelihood in [0, 1]. Adjacent controls with disjoint bounds never match.
    /// </summary>
    public static bool TryGetDuplicate(
        VisualTarget a,
        VisualTarget b,
        out double likelihood)
    {
        likelihood = 0;

        if (ReferenceEquals(a, b) || !a.Bounds.IntersectsWith(b.Bounds))
        {
            return false;
        }

        long areaA = Area(a.Bounds);
        long areaB = Area(b.Bounds);
        long smaller = Math.Min(areaA, areaB);
        long larger = Math.Max(areaA, areaB);

        if (smaller <= 0 || larger <= 0)
        {
            return false;
        }

        double areaRatio = larger / (double)smaller;
        double iou = IoU(a.Bounds, b.Bounds);
        double intersection = IntersectionOverSmaller(a.Bounds, b.Bounds);
        double centerDistance = NormalizedCenterDistance(a.Bounds, b.Bounds);
        double sizeSimilarity = SizeSimilarity(a.Bounds, b.Bounds);

        bool aContainsB = a.Bounds.Contains(b.Bounds);
        bool bContainsA = b.Bounds.Contains(a.Bounds);

        if (aContainsB || bContainsA)
        {
            VisualTarget parent = aContainsB ? a : b;
            VisualTarget child = aContainsB ? b : a;

            // A text or icon region inside a real interactive control is a
            // decoration of that control, not a second target.
            if (IsInteractiveLike(parent.Type) &&
                IsDecorative(child.Type) &&
                areaRatio <= 16)
            {
                likelihood = Math.Clamp(
                    0.80 + (0.20 * intersection),
                    0,
                    1);
                return true;
            }

            // A container that only wraps a clearly interactive child yields to
            // that child. Huge panes are handled by the geometry selector,
            // which can distinguish multiple children.
            if (IsContainerLike(parent.Type) &&
                IsInteractiveLike(child.Type) &&
                areaRatio <= 8)
            {
                likelihood = Math.Clamp(
                    0.65 + (0.25 * intersection) + (0.10 * (1 - centerDistance)),
                    0,
                    1);
                return true;
            }

            // Nested controls with different interaction semantics are
            // distinct controls, even when their rectangles overlap heavily: a
            // button or checkbox inside a list row is independently clickable.
            if (parent.Type != child.Type &&
                IsInteractiveLike(parent.Type) &&
                IsInteractiveLike(child.Type))
            {
                return false;
            }

            // The same control detected at multiple scales, or a native box
            // replacing a coarse box. Only same/compatible types are eligible;
            // distinct nested controls (a checkbox inside a list row) survive.
            if ((parent.Type == child.Type ||
                 TypeCompatibility(parent.Type, child.Type) >= 0.70) &&
                areaRatio <= 2.5 &&
                centerDistance <= 0.30)
            {
                likelihood = Math.Clamp(
                    0.58 + (0.22 * (1 - centerDistance)) +
                    (0.20 * sizeSimilarity),
                    0,
                    1);
                return true;
            }

            return false;
        }

        if (iou >= 0.55)
        {
            likelihood = Math.Clamp(
                0.55 + (0.45 * ((iou - 0.55) / 0.45)),
                0,
                1);
            return true;
        }

        if (intersection >= 0.80 &&
            areaRatio <= 2.5 &&
            (a.Type == b.Type || TypeCompatibility(a.Type, b.Type) >= 0.70))
        {
            likelihood = Math.Clamp(
                0.55 + (0.30 * ((intersection - 0.80) / 0.20)) +
                (0.15 * (1 - Math.Min(1, centerDistance))),
                0,
                1);
            return true;
        }

        // Nearly identical small boxes can have imperfect edges but the same
        // centre. Requiring high size similarity prevents adjacent controls
        // from collapsing just because their centres are close.
        if (centerDistance <= 0.08 && sizeSimilarity >= 0.70)
        {
            likelihood = Math.Clamp(
                0.50 + (0.30 * sizeSimilarity) + (0.20 * (1 - centerDistance)),
                0,
                1);
            return true;
        }

        return false;
    }

    public static bool IsButtonLike(TargetType type) =>
        type is TargetType.Button or TargetType.ListItem or TargetType.MenuItem or
            TargetType.TabItem or TargetType.TreeItem;

    public static bool IsToggleLike(TargetType type) =>
        type is TargetType.CheckBox or TargetType.RadioButton;

    public static bool IsTextEntryLike(TargetType type) =>
        type is TargetType.Edit or TargetType.ComboBox;

    public static bool IsTextLike(TargetType type) =>
        type is TargetType.Text or TargetType.Hyperlink;

    public static bool IsDecorative(TargetType type) =>
        type is TargetType.Text or TargetType.Icon or TargetType.Other;

    public static bool IsInteractiveLike(TargetType type) =>
        type is TargetType.Button or TargetType.Hyperlink or
            TargetType.MenuItem or TargetType.TabItem or TargetType.ListItem or
            TargetType.TreeItem or TargetType.CheckBox or TargetType.RadioButton or
            TargetType.ComboBox or TargetType.Edit or TargetType.Icon;

    public static bool IsContainerLike(TargetType type) =>
        type is TargetType.Unknown or TargetType.Other or TargetType.Text;

    private static long IntersectionArea(TargetBounds a, TargetBounds b)
    {
        int width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        int height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

        return width > 0 && height > 0
            ? (long)width * height
            : 0;
    }

    private static long Area(TargetBounds bounds) =>
        (long)Math.Max(0, bounds.Width) * Math.Max(0, bounds.Height);

    private static double Diagonal(TargetBounds bounds) =>
        Math.Sqrt(
            (bounds.Width * (double)bounds.Width) +
            (bounds.Height * (double)bounds.Height));

    private static double Ratio(double a, double b)
    {
        double smaller = Math.Min(Math.Abs(a), Math.Abs(b));
        double larger = Math.Max(Math.Abs(a), Math.Abs(b));
        return larger > 0 ? Math.Clamp(smaller / larger, 0, 1) : 0;
    }

    private static double Alignment(TargetBounds a, TargetBounds b)
    {
        double horizontal = Math.Max(
            EdgeSimilarity(a.Left, b.Left, a.Width, b.Width),
            Math.Max(
                EdgeSimilarity(a.Center.X, b.Center.X, a.Width, b.Width),
                EdgeSimilarity(a.Right, b.Right, a.Width, b.Width)));
        double vertical = Math.Max(
            EdgeSimilarity(a.Top, b.Top, a.Height, b.Height),
            Math.Max(
                EdgeSimilarity(a.Center.Y, b.Center.Y, a.Height, b.Height),
                EdgeSimilarity(a.Bottom, b.Bottom, a.Height, b.Height)));

        return (horizontal + vertical) / 2.0;
    }

    private static double EdgeSimilarity(int a, int b, int aSize, int bSize)
    {
        int scale = Math.Max(1, Math.Max(aSize, bSize));
        return Math.Clamp(1.0 - (Math.Abs(a - b) / (double)scale), 0, 1);
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(
                ' ',
                value.Trim().ToLowerInvariant()
                    .Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static double TokenJaccard(string a, string b)
    {
        var left = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var right = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        int intersection = left.Count(right.Contains);
        int union = left.Count + right.Count - intersection;
        return union > 0 ? intersection / (double)union : 0;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
