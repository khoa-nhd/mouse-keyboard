using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>
/// Five complementary strategies. Each one answers a different question:
/// borders (unfilled controls on a matching background), fill regions
/// (solid-coloured controls, segmented strips), circles (radio buttons and
/// round toggles), underlines (links) and dense edge clusters (toolbar icons).
/// Running them together is what raises recall; the scores are what keep
/// precision usable.
/// </summary>
public static class VisualDetectors
{
    public static void RunAll(DetectionFrame frame, List<VisualCandidate> into)
    {
        var rectangles = BorderRectangles(frame);
        var fills = FilledRegions(frame);

        into.AddRange(rectangles);

        // Colour labelling is the most expensive pass; flat controls are large,
        // so skipping it on an upscaled frame costs no recall.
        if ((long)frame.Width * frame.Height <= 1_500_000)
        {
            into.AddRange(fills);
        }
        into.AddRange(SegmentedSlices(frame, rectangles.Concat(fills)));
        into.AddRange(Circles(frame));
        into.AddRange(Underlines(frame));
        into.AddRange(Icons(frame));
    }

    /// <summary>
    /// Splits wide flat containers on their internal dividers, which is what a
    /// segmented control is: one rounded container cut by a few vertical rules.
    /// Without this the container yields a single imprecise target.
    /// </summary>
    public static List<VisualCandidate> SegmentedSlices(
        DetectionFrame frame,
        IEnumerable<VisualCandidate> containers)
    {
        var results = new List<VisualCandidate>();

        foreach (var container in containers)
        {
            var bounds = container.Bounds;

            if (bounds.Width < 60 || bounds.Height < 20 || bounds.Height > 60)
            {
                continue;
            }

            if (bounds.Width < 2.2 * bounds.Height)
            {
                continue;
            }

            var positions = new List<int>();

            foreach (var segment in frame.Vertical)
            {
                if (segment.Position <= bounds.Left + 6 || segment.Position >= bounds.Right - 6)
                {
                    continue;
                }

                if (segment.Start > bounds.Top + 4 || segment.End < bounds.Bottom - 5)
                {
                    continue;
                }

                positions.Add(segment.Position);
            }

            positions.AddRange(ColorDividers(frame, bounds));
            positions.Sort();
            var dividers = new List<int>();

            foreach (int position in positions)
            {
                if (dividers.Count > 0 && position - dividers[^1] < 15)
                {
                    continue;
                }

                dividers.Add(position);
            }

            if (dividers.Count is 0 or > 6)
            {
                continue;
            }

            int previous = bounds.Left;
            var edges = new List<int>(dividers) { bounds.Right };

            foreach (int edge in edges)
            {
                if (edge - previous < 20)
                {
                    previous = edge;
                    continue;
                }

                results.Add(new VisualCandidate(
                    Rectangle.FromLTRB(previous, bounds.Top, edge, bounds.Bottom),
                    VisualShape.Rectangle,
                    VisualDetector.Segmented,
                    0.62));

                previous = edge;
            }
        }

        return results;
    }

    /// <summary>
    /// Pairs opposite edge lines with two matching verticals. Unlike blob
    /// labelling this keeps adjacent buttons apart, because each border line is
    /// its own segment even when two buttons share a 2 px gap.
    /// </summary>
    public static List<VisualCandidate> BorderRectangles(DetectionFrame frame)
    {
        var results = new List<VisualCandidate>();
        var horizontal = frame.Horizontal;
        var vertical = frame.Vertical;

        if (horizontal.Count < 2 || vertical.Count < 2)
        {
            return results;
        }

        var verticalsByX = new Dictionary<int, List<EdgeSegment>>(vertical.Count);

        foreach (var segment in vertical)
        {
            if (!verticalsByX.TryGetValue(segment.Position, out var list))
            {
                list = [];
                verticalsByX[segment.Position] = list;
            }

            list.Add(segment);
        }

        int limit = Math.Min(horizontal.Count, 120);
        int maxHeight = Math.Min(700, frame.Height - 2);
        int maxWidth = Math.Min(1200, frame.Width - 2);
        double frameArea = (double)frame.Width * frame.Height;

        for (int i = 0; i < limit; i++)
        {
            var top = horizontal[i];

            for (int j = 0; j < limit; j++)
            {
                var bottom = horizontal[j];
                int height = bottom.Position - top.Position;

                if (height < 12 || height > maxHeight)
                {
                    continue;
                }

                int leftEdge = Math.Max(top.Start, bottom.Start);
                int rightEdge = Math.Min(top.End, bottom.End);
                int width = rightEdge - leftEdge + 1;

                if (width < 16 || width > maxWidth)
                {
                    continue;
                }

                if (width < 0.85 * Math.Min(top.Length, bottom.Length))
                {
                    continue;
                }

                if (!TryFindSide(verticalsByX, leftEdge, top.Position, bottom.Position, out int leftX) ||
                    !TryFindSide(verticalsByX, rightEdge, top.Position, bottom.Position, out int rightX) ||
                    rightX - leftX < 16)
                {
                    continue;
                }

                var bounds = Rectangle.FromLTRB(leftX, top.Position, rightX + 1, bottom.Position + 1);

                if (bounds.Width * (double)bounds.Height > 0.7 * frameArea)
                {
                    continue;
                }

                var inner = Inner(bounds, 3);
                double interior = inner.Width > 0 && inner.Height > 0 ? frame.Edges.Density(inner) : 0;

                if (interior > 0.5)
                {
                    continue;
                }

                double horizontalCoverage = Math.Min(1, width / (double)Math.Max(top.Length, bottom.Length));
                double score = 0.55 + (0.25 * horizontalCoverage) + (0.2 * (1 - Math.Min(1, interior)));
                results.Add(new VisualCandidate(bounds, VisualShape.Rectangle, VisualDetector.BorderEdge, Math.Min(1, score)));

                if (results.Count >= 300)
                {
                    return results;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Flat-colour blobs: filled buttons, segmented controls, fields, chips.
    /// Quantized colour labelling means two touching buttons with different
    /// fills stay separate without any extra work.
    /// </summary>
    public static List<VisualCandidate> FilledRegions(DetectionFrame frame)
    {
        var results = new List<VisualCandidate>();
        double frameArea = (double)frame.Width * frame.Height;

        foreach (var blob in Blobs.FromColorKeys(frame.Color))
        {
            var bounds = blob.Bounds;

            if (blob.Area < 240 || bounds.Width < 14 || bounds.Height < 11)
            {
                continue;
            }

            // Controls are short; a tall blob is a panel or the page background.
            if (bounds.Height > 140 || bounds.Width > 900)
            {
                continue;
            }

            if (blob.Area > 0.35 * frameArea)
            {
                continue;
            }

            // Controls contain text and icons, so a solid fill ratio is not a
            // valid test; a solid border is. A glyph or a text line lacks it.
            if (blob.FillRatio < 0.45 || blob.BorderCoverage < 0.85)
            {
                continue;
            }

            if (blob.Aspect < 0.15 || blob.Aspect > 12)
            {
                continue;
            }

            bool rounded = CornersDiffer(frame.Color, bounds);
            double score = 0.5 + Math.Min(0.2, (blob.FillRatio - 0.45) * 0.4) + (rounded ? 0.05 : 0);

            results.Add(new VisualCandidate(
                bounds,
                rounded ? VisualShape.RoundedRectangle : VisualShape.Rectangle,
                VisualDetector.FillRegion,
                Math.Min(1, score)));
        }

        return results;
    }

    /// <summary>Radio buttons, round toggles and filled status dots.</summary>
    public static List<VisualCandidate> Circles(DetectionFrame frame)
    {
        var results = new List<VisualCandidate>();

        var binaries = CirclePolarities(frame);

        foreach (var binary in binaries)
        {
            foreach (var blob in Blobs.FromBinary(binary))
            {
                var bounds = blob.Bounds;

                if (bounds.Width < 9 || bounds.Height < 9 || bounds.Width > 72 || bounds.Height > 72)
                {
                    continue;
                }

                double fill = blob.FillRatio;

                // A disc is ~0.785 of its box, a 2 px ring is ~0.3, a square is ~1.0.
                if (fill < 0.25 || fill > 0.9)
                {
                    continue;
                }

                double aspect = blob.Aspect;

                if (aspect < 0.75 || aspect > 1.33)
                {
                    continue;
                }

                if (!CornersAreBackground(binary, bounds))
                {
                    continue;
                }

                double roundness = 1 - Math.Abs(aspect - 1);
                double score = 0.5 + (0.25 * roundness) + (0.15 * Math.Min(1, fill / 0.785));
                results.Add(new VisualCandidate(bounds, VisualShape.Circle, VisualDetector.Circle, Math.Min(1, score)));
            }
        }

        return results;
    }

    /// <summary>Underlined links: a thin horizontal rule under text-like edges.</summary>
    public static List<VisualCandidate> Underlines(DetectionFrame frame)
    {
        var results = new List<VisualCandidate>();

        foreach (var segment in frame.Horizontal)
        {
            if (segment.Length < 24 || segment.Position < 10)
            {
                continue;
            }

            Rectangle? text = null;

            foreach (var line in frame.TextLines)
            {
                if (line.Bottom < segment.Position - 5 || line.Bottom > segment.Position + 1)
                {
                    continue;
                }

                if (line.Width < 0.5 * segment.Length)
                {
                    continue;
                }

                if (Math.Min(line.Right, segment.End) - Math.Max(line.Left, segment.Start) < 0.6 * line.Width)
                {
                    continue;
                }

                text = text is null ? line : Rectangle.Union(text.Value, line);
            }

            if (text is null)
            {
                continue;
            }

            var bounds = Rectangle.FromLTRB(
                Math.Min(text.Value.Left, segment.Start),
                text.Value.Top,
                Math.Max(text.Value.Right, segment.End),
                segment.Position + 1);

            if (bounds.Width < 24)
            {
                continue;
            }

            results.Add(new VisualCandidate(bounds, VisualShape.Underline, VisualDetector.Underline, 0.62));
        }

        return results;
    }

    /// <summary>
    /// Controls contrast with their background: dark controls on a light
    /// surface, light controls on a dark one. Checking both polarities on every
    /// frame doubles the labelling cost for no gain on a uniform theme, so the
    /// background decides — except on small frames where both are cheap.
    /// </summary>
    private static IReadOnlyList<GrayImage> CirclePolarities(DetectionFrame frame)
    {
        bool both = (long)frame.Width * frame.Height <= 600_000;
        bool darkControls = frame.Gray.Mean(0, 0, frame.Width, frame.Height) >= 128;

        if (both)
        {
            return [frame.DarkBinary, frame.LightBinary];
        }

        return darkControls ? [frame.DarkBinary] : [frame.LightBinary];
    }

    /// <summary>
    /// Thin full-height columns whose tone differs from the container's own fill.
    /// A 1 px rule antialiases into a gradient too weak for the edge pass after
    /// smoothing, but it is still a colour discontinuity across the whole height.
    /// </summary>
    private static List<int> ColorDividers(DetectionFrame frame, Rectangle bounds)
    {
        var positions = new List<int>();
        var image = frame.Color;
        int top = bounds.Top + 3;
        int bottom = bounds.Bottom - 3;
        int left = bounds.Left + 4;
        int right = bounds.Right - 4;

        if (bottom - top < 8 || right - left < 20)
        {
            return positions;
        }

        // The container's own fill is the majority tone of its top edge.
        var samples = new List<int>();

        for (int x = left; x < right; x++)
        {
            samples.Add(image.Luma(x, bounds.Top + 2));
        }

        samples.Sort();
        int baseline = samples[samples.Count / 2];
        int span = bottom - top;
        int minimum = (int)(span * 0.7);

        // Collect adjacent columns that differ along most of the height, then
        // keep only thin bands: a wide one is a segment fill, not a divider.
        int runStart = -1;

        for (int x = left; x <= right; x++)
        {
            bool divider = false;

            if (x < right)
            {
                int differing = 0;

                for (int y = top; y < bottom; y++)
                {
                    if (Math.Abs(image.Luma(x, y) - baseline) > 14)
                    {
                        differing++;
                    }
                }

                divider = differing >= minimum;
            }

            if (divider && runStart < 0)
            {
                runStart = x;
            }
            else if (!divider && runStart >= 0)
            {
                if (x - runStart <= 6)
                {
                    positions.Add(runStart + ((x - runStart) / 2));
                }

                runStart = -1;
            }
        }

        return positions;
    }

    /// <summary>
    /// Icon glyphs: compact blobs of ink in the foreground polarity.
    ///
    /// Only the polarity opposite the background is used, and the mask is
    /// dilated before labelling. A glyph is drawn as separate strokes — a menu
    /// icon is three 2 px bars, a gear is a ring plus six dots — so labelling the
    /// raw mask produced fragments instead of one icon.
    /// </summary>
    public static List<VisualCandidate> Icons(DetectionFrame frame)
    {
        var results = new List<VisualCandidate>();
        bool darkInk = frame.Gray.Mean(0, 0, frame.Width, frame.Height) >= 128;
        var ink = darkInk ? frame.DarkBinary : frame.LightBinary;
        var grouped = ink.Dilate(IconGrouping);

        foreach (var blob in Blobs.FromBinary(grouped))
        {
            var bounds = Rectangle.Inflate(blob.Bounds, -IconGrouping, -IconGrouping);

            if (bounds.Width < 10 || bounds.Height < 10 || bounds.Width > 72 || bounds.Height > 72)
            {
                continue;
            }

            int area = InkArea(ink, bounds);
            double aspect = bounds.Width / (double)bounds.Height;

            if (aspect < 0.45 || aspect > 2.2)
            {
                continue;
            }

            if (area < 18)
            {
                continue;
            }

            double density = area / (double)(bounds.Width * bounds.Height);

            if (density < 0.05)
            {
                continue;
            }

            if (density > 0.93 && aspect is > 0.85 and < 1.2)
            {
                // Solid square: a filled chip, not a glyph.
                continue;
            }

            results.Add(new VisualCandidate(
                bounds,
                VisualShape.Icon,
                VisualDetector.Icon,
                Math.Min(1, 0.55 + (density * 0.4))));
        }

        return results;
    }

    /// <summary>
    /// Dilation applied before labelling so one glyph becomes one blob. One pixel
    /// joins a glyph's own strokes (a menu icon's bars are 2 px apart) without
    /// bridging the 4 px gap between two neighbouring toolbar icons.
    /// </summary>
    private const int IconGrouping = 1;

    private static int InkArea(GrayImage ink, Rectangle bounds)
    {
        int count = 0;

        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                if (ink.At(x, y) >= 128)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static Rectangle Inner(Rectangle bounds, int pixels)
    {
        var inner = Rectangle.Inflate(bounds, -pixels, -pixels);
        return inner.Width <= 0 || inner.Height <= 0 ? Rectangle.Empty : inner;
    }

    private static Rectangle Inner(Rectangle bounds, double fraction)
    {
        int dx = (int)Math.Round(bounds.Width * fraction);
        int dy = (int)Math.Round(bounds.Height * fraction);
        var inner = Rectangle.Inflate(bounds, -dx, -dy);
        return inner.Width <= 0 || inner.Height <= 0 ? Rectangle.Empty : inner;
    }

    private static bool TryFindSide(
        Dictionary<int, List<EdgeSegment>> verticalsByX,
        int x,
        int top,
        int bottom,
        out int position)
    {
        position = 0;

        for (int dx = -3; dx <= 3; dx++)
        {
            if (!verticalsByX.TryGetValue(x + dx, out var candidates))
            {
                continue;
            }

            foreach (var segment in candidates)
            {
                if (segment.Start <= top + 4 && segment.End >= bottom - 4)
                {
                    position = segment.Position;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CornersAreBackground(GrayImage binary, Rectangle bounds)
    {
        return binary.At(bounds.Left, bounds.Top) < 128 &&
               binary.At(bounds.Right - 1, bounds.Top) < 128 &&
               binary.At(bounds.Left, bounds.Bottom - 1) < 128 &&
               binary.At(bounds.Right - 1, bounds.Bottom - 1) < 128;
    }

    /// <summary>True when the box corners do not share the blob's colour, i.e. rounded.</summary>
    private static bool CornersDiffer(Image32 image, Rectangle bounds)
    {
        int cx = bounds.Left + (bounds.Width / 2);
        int cy = bounds.Top + (bounds.Height / 2);
        int centerLuma = image.Luma(cx, cy);

        foreach (var (x, y) in new[]
                 {
                     (bounds.Left, bounds.Top),
                     (bounds.Right - 1, bounds.Top),
                     (bounds.Left, bounds.Bottom - 1),
                     (bounds.Right - 1, bounds.Bottom - 1)
                 })
        {
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
            {
                return false;
            }

            if (Math.Abs(image.Luma(x, y) - centerLuma) > 24)
            {
                return true;
            }
        }

        return false;
    }
}
