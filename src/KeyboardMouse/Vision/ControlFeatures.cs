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
    /// <summary>
    /// Share of the box outline, on its weakest side, that is drawn as a line.
    /// An outlined control is closed on all four sides; a word's bounding box
    /// touches ink only where letters happen to reach it.
    /// </summary>
    public double BorderContinuity { get; init; }

    /// <summary>
    /// Ink sits right beside the box, closer than ~0.6 of its height, on the
    /// left or the right. That is how letters and words sit in a sentence; a
    /// toolbar icon or a text-only button keeps clear space around itself.
    /// </summary>
    public bool InkBeside { get; init; }

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

    public bool HasBorder => BorderStrength >= 1.35 || BorderContinuity >= ClosedOutline;

    /// <summary>Weakest-side coverage that counts as a closed outline.</summary>
    public const double ClosedOutline = 0.85;

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

        // Dominant tone inside against dominant tone around: a word on a page
        // has the page tone on both sides. Comparing against the mean of the
        // ring instead let the neighbouring letters darken "outside", and every
        // word in a paragraph then read as a filled button.
        int interiorLuma = (modalBin * 8) + 4;
        double surfaceContrast = SideSurfaceContrast(image, box, interiorLuma);
        double continuity = OutlineContinuity(image, box, interiorLuma);

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
            Height = box.Height,
            BorderContinuity = continuity,
            InkBeside = HasInkBeside(image, box)
        };
    }

    /// <summary>
    /// Looks left and right of the box, within 0.6 of its height, for columns
    /// that hold ink against the local page tone. Two ink pixels in a column are
    /// needed so antialiasing noise and a one-pixel rule do not count.
    /// </summary>
    private static bool HasInkBeside(Image32 image, Rectangle box)
    {
        const int InkDelta = 40;

        if (box.Height < 6)
        {
            return false;
        }

        int reach = Math.Max(5, (int)Math.Round(0.6 * box.Height));
        int top = box.Top + (box.Height / 6);
        int bottom = box.Bottom - (box.Height / 6);

        // The first two columns are skipped: detected boxes sit a pixel inside
        // the ink, and the shape's own antialiased edge is not a neighbour.
        const int Skip = 2;

        return Side(box.Left - 1 - Skip, -1) || Side(box.Right + Skip, +1);

        bool Side(int start, int step)
        {
            int span = reach - Skip;
            int left = step < 0 ? start - span + 1 : start;
            var strip = Rectangle.Intersect(
                Rectangle.FromLTRB(left, top, left + span, bottom),
                new Rectangle(0, 0, image.Width, image.Height));

            if (strip.Width <= 0 || strip.Height <= 0)
            {
                return false;
            }

            int tone = ModalLuma(image, strip);

            for (int x = strip.Left; x < strip.Right; x++)
            {
                int ink = 0;

                for (int y = strip.Top; y < strip.Bottom; y++)
                {
                    if (Math.Abs(image.Luma(x, y) - tone) >= InkDelta && ++ink >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// For each side, the share of positions along it where a line differs from
    /// the interior tone, looking 2 px inside and 1 px outside the edge (box
    /// edges are rarely exact). Returns the weakest side. Corners are skipped
    /// so rounded outlines are not penalised.
    /// </summary>
    private static double OutlineContinuity(Image32 image, Rectangle box, int interiorLuma)
    {
        const int InkDelta = 30;

        if (box.Width < 8 || box.Height < 8)
        {
            return 0;
        }

        int cornerX = Math.Max(2, box.Width / 8);
        int cornerY = Math.Max(2, box.Height / 8);
        double weakest = 1;

        // Top, bottom: walk x; left, right: walk y.
        weakest = Math.Min(weakest, Horizontal(box.Top, +1));
        weakest = Math.Min(weakest, Horizontal(box.Bottom - 1, -1));
        weakest = Math.Min(weakest, Vertical(box.Left, +1));
        weakest = Math.Min(weakest, Vertical(box.Right - 1, -1));
        return weakest;

        double Horizontal(int edge, int inward)
        {
            int hits = 0;
            int total = 0;

            for (int x = box.Left + cornerX; x < box.Right - cornerX; x++)
            {
                total++;

                for (int d = -1; d <= 2; d++)
                {
                    int y = edge + (d * inward);

                    if (y >= 0 && y < image.Height && Math.Abs(image.Luma(x, y) - interiorLuma) >= InkDelta)
                    {
                        hits++;
                        break;
                    }
                }
            }

            return total == 0 ? 0 : hits / (double)total;
        }

        double Vertical(int edge, int inward)
        {
            int hits = 0;
            int total = 0;

            for (int y = box.Top + cornerY; y < box.Bottom - cornerY; y++)
            {
                total++;

                for (int d = -1; d <= 2; d++)
                {
                    int x = edge + (d * inward);

                    if (x >= 0 && x < image.Width && Math.Abs(image.Luma(x, y) - interiorLuma) >= InkDelta)
                    {
                        hits++;
                        break;
                    }
                }
            }

            return total == 0 ? 0 : hits / (double)total;
        }
    }

    /// <summary>
    /// How far the box's dominant tone stands out from the page around it,
    /// measured side by side. A strip of 4 px is read above, below, left and
    /// right of the box and its dominant tone compared with the interior's; the
    /// second strongest side wins. One side may legitimately match (two filled
    /// buttons touching, a box against the capture edge), but a word on a page
    /// matches the page on every side and gets no surface at all.
    /// </summary>
    private static double SideSurfaceContrast(Image32 image, Rectangle box, int interiorLuma)
    {
        Span<double> sides = stackalloc double[4];
        int count = 0;

        AddSide(Rectangle.FromLTRB(box.Left, box.Top - 4, box.Right, box.Top), ref count, sides);
        AddSide(Rectangle.FromLTRB(box.Left, box.Bottom, box.Right, box.Bottom + 4), ref count, sides);
        AddSide(Rectangle.FromLTRB(box.Left - 4, box.Top, box.Left, box.Bottom), ref count, sides);
        AddSide(Rectangle.FromLTRB(box.Right, box.Top, box.Right + 4, box.Bottom), ref count, sides);

        if (count == 0)
        {
            return 255;
        }

        var used = sides[..count];
        used.Sort();

        // Ascending: the last entry is the strongest side, the one before it
        // the second strongest.
        return count == 1 ? used[0] : used[count - 2];

        void AddSide(Rectangle strip, ref int n, Span<double> into)
        {
            var clipped = Rectangle.Intersect(strip, new Rectangle(0, 0, image.Width, image.Height));

            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                return;
            }

            into[n++] = Math.Abs(interiorLuma - ModalLuma(image, clipped));
        }
    }

    private static int ModalLuma(Image32 image, Rectangle area)
    {
        Span<int> histogram = stackalloc int[32];

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                histogram[image.Luma(x, y) >> 3]++;
            }
        }

        int modal = 0;

        for (int i = 1; i < histogram.Length; i++)
        {
            if (histogram[i] > histogram[modal])
            {
                modal = i;
            }
        }

        return (modal * 8) + 4;
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
