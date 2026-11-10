using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>
/// Measurements taken from the source pixels around one candidate. Nothing here
/// is a decision — the classifier consumes these, which keeps the rules readable
/// and lets a test assert on the evidence instead of on the verdict.
/// </summary>
public readonly record struct ControlFeatures(
    double Contrast,
    double EdgeDensity,
    double FillUniformity,
    double BorderStrength,
    double TextCoverage,
    bool TextInside,
    bool LabelBelow,
    int RowPeers,
    int ColumnPeers,
    bool InsideContainer,
    bool RoundedCorners,
    bool HasKnob,
    bool HasChevron,
    bool ContainsOthers,
    bool LabelBeside,
    bool InTextFlow,
    int RowGap,
    int RowNeighbours,
    double SurfaceContrast,
    double TextOffset,
    double Aspect)
{
    public int Width { get; init; }

    public int Height { get; init; }

    public bool ThinHorizontal => Height <= 4 && Width >= 30;

    public bool ThinVertical => Width <= 4 && Height >= 30;

    public bool Square => Aspect is >= 0.8 and <= 1.25;

    public bool Wide => Aspect >= 1.6;

    public bool Tall => Aspect <= 0.62;

    public bool HasText => TextInside || TextCoverage >= 0.08;

    /// <summary>
    /// A control's label is centred in its box; a field's placeholder starts at
    /// the left padding. Without this, "Save changes" on a filled button and an
    /// empty search box look the same.
    /// </summary>
    public bool TextCentered => Math.Abs(TextOffset) <= 0.12;

    public bool HasBorder => BorderStrength >= 1.35;

    /// <summary>
    /// A box has a surface of its own only when its dominant tone differs from
    /// the pixels around it. Without this test every glyph box looked "flat
    /// filled", because a glyph covers little of its own bounding box.
    /// </summary>
    public bool HasFlatFill => FillUniformity >= 0.5 && SurfaceContrast >= 8;

    public bool HighEdgeDetail => EdgeDensity >= 0.12;

    public bool LowDetail => EdgeDensity <= 0.08;
}

/// <summary>
/// Feature extraction over the source image. Everything is computed per box, so
/// the cost follows the number of candidates and their size rather than the
/// whole screen: this pass must stay inside the same budget as detection.
/// </summary>
public static class ControlFeatureExtractor
{
    /// <summary>Gradient magnitude above which a pixel counts as an edge.</summary>
    public const int EdgeThreshold = 60;

    public static ControlFeatures[] Extract(
        Image32 image,
        IReadOnlyList<VisualCandidate> candidates,
        IReadOnlyList<Rectangle> textLines)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(candidates);

        var features = new ControlFeatures[candidates.Count];
        var boxes = new Rectangle[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            boxes[i] = Rectangle.Intersect(
                candidates[i].Bounds,
                new Rectangle(0, 0, image.Width, image.Height));

            features[i] = Measure(image, candidates[i], boxes[i], textLines);
        }

        // Grouping evidence needs every box at once: three icons in a row is the
        // strongest signal that any one of them is a control.
        for (int i = 0; i < candidates.Count; i++)
        {
            int rowPeers = 0;
            int rowNeighbours = 0;
            int columnPeers = 0;
            bool inside = false;
            bool knob = false;
            bool chevron = false;
            bool contains = false;
            int rowGap = int.MaxValue;

            var box = boxes[i];

            for (int j = 0; j < candidates.Count; j++)
            {
                if (i == j || boxes[j].Width == 0)
                {
                    continue;
                }

                var other = boxes[j];
                bool similarHeight = Math.Abs(other.Height - box.Height) <= 4;
                bool similarSize = similarHeight && Math.Abs(other.Width - box.Width) <= 10;
                bool similarCentre = Math.Abs((other.Top + (other.Height / 2)) - (box.Top + (box.Height / 2))) <= 6;
                bool sameRow = similarCentre && 0.5 * (other.Height + box.Height) > 0;
                bool nearby = Math.Abs(other.Left - box.Left) <= 4 && Math.Abs(other.Bottom - box.Bottom) <= 4;

                int gap = HorizontalGap(box, other);
                bool rowAligned = Math.Abs((other.Top + (other.Height / 2)) - (box.Top + (box.Height / 2))) <= 8;

                // Any same-row sibling in the band counts, not just the adjacent
                // one: the end icons of a six-icon toolbar have a single neighbour
                // each and were not recognised as a group.
                if (rowAligned && !nearby && gap is >= 4 and <= 220 &&
                    other.Height >= 10 && box.Height >= 10)
                {
                    rowNeighbours++;
                }

                if (similarHeight && sameRow && !nearby && gap <= 48)
                {
                    rowPeers++;
                    rowGap = Math.Min(rowGap, gap);
                }

                if (similarSize &&
                    Math.Abs((other.Top + (other.Height / 2)) - (box.Top + (box.Height / 2))) <= 6 &&
                    VerticalGap(box, other) <= 40)
                {
                    columnPeers++;
                }

                if (box.Contains(other) && other.Width * (double)other.Height <= 0.5 * box.Width * box.Height)
                {
                    contains = true;
                }

                if (other.Contains(box) && other.Width * (double)other.Height >= 2.0 * box.Width * box.Height)
                {
                    inside = true;
                }

                if (candidates[j].Shape == VisualShape.Circle &&
                    other.Contains(box) == false &&
                    box.Contains(other) &&
                    other.Width >= 0.25 * box.Width &&
                    other.Width <= 0.7 * box.Width)
                {
                    knob = true;
                }

                if (candidates[j].Shape == VisualShape.Icon &&
                    box.Contains(other) &&
                    other.Left >= box.Left + (int)(0.72 * box.Width))
                {
                    chevron = true;
                }
            }

            features[i] = features[i] with
            {
                RowPeers = rowPeers,
                ColumnPeers = columnPeers,
                InsideContainer = inside,
                HasKnob = knob,
                HasChevron = chevron,
                ContainsOthers = contains,
                RowGap = rowGap == int.MaxValue ? 0 : rowGap,
                RowNeighbours = rowNeighbours
            };
        }

        return features;
    }

    private static int HorizontalGap(Rectangle a, Rectangle b)
    {
        if (a.Right < b.Left)
        {
            return b.Left - a.Right;
        }

        return b.Right < a.Left ? a.Left - b.Right : 0;
    }

    private static int VerticalGap(Rectangle a, Rectangle b)
    {
        if (a.Bottom < b.Top)
        {
            return b.Top - a.Bottom;
        }

        return b.Bottom < a.Top ? a.Top - b.Bottom : 0;
    }

    private static ControlFeatures Measure(
        Image32 image,
        VisualCandidate candidate,
        Rectangle box,
        IReadOnlyList<Rectangle> textLines)
    {
        if (box.Width < 3 || box.Height < 3)
        {
            return new ControlFeatures(0, 0, 0, 0, 0, false, false, 0, 0, false, false, false, false, false, false, false, 0, 0, 0, 0, 1)
            {
                Width = Math.Max(0, box.Width),
                Height = Math.Max(0, box.Height)
            };
        }

        var histogram = new int[32];
        long area = 0;
        long edgePixels = 0;
        long borderGradient = 0;
        long borderCount = 0;
        long innerGradient = 0;
        long innerCount = 0;

        int inset = box.Width > 8 && box.Height > 8 ? 1 : 0;
        int innerInset = Math.Min(5, Math.Min(box.Width, box.Height) / 4);

        for (int y = box.Top + inset; y < box.Bottom - inset; y++)
        {
            int gradient = 0;

            for (int x = box.Left + inset; x < box.Right - inset; x++)
            {
                int luma = image.Luma(x, y);
                histogram[luma >> 3]++;
                area++;

                int gx = Math.Abs(image.Luma(Math.Min(image.Width - 1, x + 1), y) -
                                  image.Luma(Math.Max(0, x - 1), y));
                int gy = Math.Abs(image.Luma(x, Math.Min(image.Height - 1, y + 1)) -
                                  image.Luma(x, Math.Max(0, y - 1)));
                gradient = gx + gy;

                if (gradient > EdgeThreshold)
                {
                    edgePixels++;
                }

                int dx = Math.Min(x - box.Left, box.Right - 1 - x);
                int dy = Math.Min(y - box.Top, box.Bottom - 1 - y);

                if (dx < 2 || dy < 2)
                {
                    borderGradient += gradient;
                    borderCount++;
                }
                else if (dx > innerInset && dy > innerInset)
                {
                    innerGradient += gradient;
                    innerCount++;
                }
            }
        }

        int p5 = Percentile(histogram, area, 0.05);
        int p95 = Percentile(histogram, area, 0.95);
        int mode = 0;

        foreach (int count in histogram)
        {
            if (count > mode)
            {
                mode = count;
            }
        }

        var insetBox = Rectangle.Inflate(box, -2, -2);

        int modalBin = 0;

        for (int i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] > histogram[modalBin])
            {
                modalBin = i;
            }
        }

        long textPixels = 0;
        Rectangle? textUnion = null;
        bool textInside = false;
        bool labelBelow = false;
        bool labelBeside = false;
        bool inTextFlow = false;

        if (textLines is not null)
        {
            foreach (var line in textLines)
            {
                var overlap = Rectangle.Intersect(line, insetBox);

                if (overlap.Width > 0 && overlap.Height > 0)
                {
                    textPixels += overlap.Width * (long)overlap.Height;
                    textUnion = textUnion is null ? overlap : Rectangle.Union(textUnion.Value, overlap);

                    if (overlap.Width * (double)overlap.Height >= 0.4 * Math.Min(line.Width * (double)line.Height, insetBox.Width * (double)insetBox.Height))
                    {
                        textInside = true;
                    }
                }

                if (line.Top >= box.Bottom - 2 && line.Top <= box.Bottom + 16)
                {
                    int shared = Math.Min(line.Right, box.Right) - Math.Max(line.Left, box.Left);

                    if (shared >= 0.35 * line.Width && line.Width <= 2.4 * box.Width)
                    {
                        labelBelow = true;
                    }
                }

                bool isOtherLine = Rectangle.Intersect(line, box).IsEmpty;

                if (isOtherLine)
                {
                    int above = box.Top - line.Bottom;
                    int below = line.Top - box.Bottom;
                    int sharedWidth = Math.Min(line.Right, box.Right) - Math.Max(line.Left, box.Left);

                    if ((above is >= 0 and <= 12 || below is >= 0 and <= 12) &&
                        sharedWidth >= 0.4 * line.Width)
                    {
                        inTextFlow = true;
                    }
                }

                if (line.Top >= box.Top - 6 && line.Bottom <= box.Bottom + 8)
                {
                    int gap = line.Left - box.Right;

                    // A 14 px ring next to a short word is a labelled control; the
                    // ratio test alone would reject every label longer than 50 px.
                    if (gap is >= -2 and <= 24 && line.Width <= (3.5 * box.Width) + 60)
                    {
                        labelBeside = true;
                    }
                }
            }
        }

        double outsideLuma = OutsideLuma(image, box);
        double surfaceContrast = Math.Abs((modalBin * 8) + 4 - outsideLuma);

        double textOffset = textUnion is null || box.Width == 0
            ? 0
            : (textUnion.Value.Left + (textUnion.Value.Width / 2.0) - (box.Left + (box.Width / 2.0))) / box.Width;

        long boxArea = box.Width * (long)box.Height;
        double textCoverage = boxArea == 0 ? 0 : Math.Min(1, textPixels / (double)boxArea);
        double uniformity = area == 0 ? 0 : mode / (double)area;
        double edgeDensity = area == 0 ? 0 : edgePixels / (double)area;
        double borderMean = borderCount == 0 ? 0 : borderGradient / (double)borderCount;
        double innerMean = innerCount == 0 ? 0 : innerGradient / (double)innerCount;
        double borderStrength = innerMean <= 1 ? (borderMean > 20 ? 4 : 1) : borderMean / innerMean;

        return new ControlFeatures(
            Contrast: p95 - p5,
            EdgeDensity: edgeDensity,
            FillUniformity: uniformity,
            BorderStrength: Math.Min(4, borderStrength),
            TextCoverage: textCoverage,
            TextInside: textInside,
            LabelBelow: labelBelow,
            RowPeers: 0,
            ColumnPeers: 0,
            InsideContainer: false,
            RoundedCorners: candidate.Shape == VisualShape.RoundedRectangle,
            HasKnob: false,
            HasChevron: false,
            ContainsOthers: false,
            LabelBeside: labelBeside,
            InTextFlow: inTextFlow,
            RowGap: 0,
            RowNeighbours: 0,
            SurfaceContrast: surfaceContrast,
            TextOffset: textOffset,
            Aspect: box.Height == 0 ? 0 : box.Width / (double)box.Height)
        {
            Width = box.Width,
            Height = box.Height
        };
    }

    /// <summary>Mean luma of the ring just outside the box: the page tone.</summary>
    private static double OutsideLuma(Image32 image, Rectangle box)
    {
        long sum = 0;
        long count = 0;

        for (int y = box.Top - 4; y < box.Bottom + 4; y++)
        {
            for (int x = box.Left - 4; x < box.Right + 4; x++)
            {
                if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom)
                {
                    continue;
                }

                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
                {
                    continue;
                }

                sum += image.Luma(x, y);
                count++;
            }
        }

        return count == 0 ? 255 : sum / (double)count;
    }

    private static int Percentile(int[] histogram, long total, double fraction)
    {
        if (total == 0)
        {
            return 0;
        }

        long target = (long)(total * fraction);
        long seen = 0;

        for (int i = 0; i < histogram.Length; i++)
        {
            seen += histogram[i];

            if (seen >= target)
            {
                return (i * 8) + 4;
            }
        }

        return 255;
    }
}
