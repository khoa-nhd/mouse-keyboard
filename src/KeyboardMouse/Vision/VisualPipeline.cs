using System.Diagnostics;
using System.Drawing;

namespace KeyboardMouse.Vision;

public sealed record VisualDetectionOptions
{
    public int MaxCandidates { get; init; } = 80;

    public double MinScore { get; init; } = 0.5;

    /// <summary>Cost ceiling; large screens are downscaled to stay inside it.</summary>
    public int MaxProcessingPixels { get; init; } = 900_000;

    public bool MultiScale { get; init; } = true;

    public bool AssociateText { get; init; } = true;

    public bool RefineSmallControls { get; init; } = true;

    public int MinSize { get; init; } = 8;

    public int MaxSize { get; init; } = 1200;
}

public sealed record VisualDetectionResult(
    IReadOnlyList<VisualCandidate> Candidates,
    IReadOnlyList<ControlCandidate> Controls,
    IReadOnlyList<double> Scales,
    long ElapsedMilliseconds)
{
    /// <summary>
    /// Controls the classifier believes are clickable and whose geometry checks
    /// out, ranked by evidence discounted by geometric certainty.
    /// </summary>
    public IReadOnlyList<ControlCandidate> Interactive => Controls
        .Where(control => control.IsOfferable)
        .OrderByDescending(control => control.Rank)
        .ThenByDescending(control => control.Area)
        .ToArray();
}

/// <summary>
/// Multi-scale orchestration: choose scales from the capture size instead of
/// always using native resolution, run the detector set at each scale, map
/// every box back to source pixels, then merge and associate.
/// </summary>
public static class VisualPipeline
{
    public static VisualDetectionResult Detect(Image32 screen, VisualDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        options ??= new VisualDetectionOptions();

        var stopwatch = Stopwatch.StartNew();
        var scales = ChooseScales(screen, options);
        var raw = new List<VisualCandidate>(256);
        var textLines = new List<Rectangle>(64);

        foreach (double scale in scales)
        {
            var frame = BuildFrame(screen, scale);

            if (frame is null)
            {
                continue;
            }

            var found = new List<VisualCandidate>(64);
            VisualDetectors.RunAll(frame, found);
            var lines = frame.TextLines;

            foreach (var candidate in found)
            {
                // On an upscaled frame letter shapes are large enough to pass the
                // size tests, and they land inside a wide line. Keeping the strict
                // rule there removes them without touching native-scale toolbar
                // icons, which is where the adjacency test matters.
                bool glyphNoise = frame.Factor > 1.5
                    ? IsLetterInLine(candidate, lines)
                    : IsGlyphNoise(candidate, lines, found);

                if (glyphNoise)
                {
                    continue;
                }

                raw.Add(candidate with { Bounds = frame.ToSource(candidate.Bounds) });
            }

            foreach (var line in lines)
            {
                textLines.Add(frame.ToSource(line));
            }
        }

        if (options.RefineSmallControls)
        {
            raw.AddRange(RefineSmall(screen, raw, scales));
        }

        var merged = VisualMerger.Merge(raw, Math.Max(200, options.MaxCandidates * 4));

        var associated = options.AssociateText ? Associate(merged, textLines) : merged;

        var final = associated
            .Where(c => c.Score >= options.MinScore)
            .Where(c => c.Bounds.Width >= options.MinSize && c.Bounds.Height >= options.MinSize)
            .Where(c => c.Bounds.Width <= options.MaxSize && c.Bounds.Height <= options.MaxSize)
            .Where(c => c.Bounds.Width <= screen.Width && c.Bounds.Height <= screen.Height)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Area)
            .Take(options.MaxCandidates)
            .ToArray();

        var controls = ControlClassifier.Classify(screen, final, textLines);
        ControlClassifier.AddTextOnlyControls(screen, textLines, controls);

        // Geometry last: it needs the other controls to know where not to put a
        // click point, and it is the layer that decides whether a box is usable.
        var measured = GeometryEstimator.Attach(screen, controls);

        var typed = measured
            .OrderByDescending(control => control.Rank)
            .ThenByDescending(control => control.Area)
            .ToArray();

        stopwatch.Stop();
        return new VisualDetectionResult(final, typed, scales, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Downscale when the capture is larger than the cost ceiling, add the
    /// native pass only when that is still affordable, and add an upscaled pass
    /// for small captures where 10-16 px controls are one or two antialiased
    /// pixels at native size. A pass is only added when it fits under the same
    /// ceiling, so the total cost stays bounded instead of quadruping.
    /// </summary>
    public static List<double> ChooseScales(Image32 screen, VisualDetectionOptions options)
    {
        long pixels = (long)screen.Width * screen.Height;
        double baseScale = 1.0;

        if (pixels > options.MaxProcessingPixels)
        {
            baseScale = Math.Max(0.25, Math.Sqrt(options.MaxProcessingPixels / (double)pixels));

            // Rounding a 0.659 scale up to whole pixels can still overshoot the
            // ceiling; step down until the built frame really fits.
            while (baseScale > 0.25 && ProcessedPixels(screen, baseScale) > options.MaxProcessingPixels)
            {
                baseScale -= 0.005;
            }
        }

        var scales = new List<double> { Math.Round(baseScale, 3) };

        if (options.MultiScale)
        {
            if (baseScale < 0.99 && baseScale >= 0.85 && pixels <= 4L * options.MaxProcessingPixels)
            {
                scales.Add(1.0);
            }

            if (pixels * 4 <= options.MaxProcessingPixels)
            {
                scales.Add(2.0);
            }
        }

        return scales.Distinct().Where(scale => scale > 0).ToList();
    }

    private static long ProcessedPixels(Image32 screen, double scale)
    {
        long width = Math.Max(24, (int)Math.Round(screen.Width * scale));
        long height = Math.Max(24, (int)Math.Round(screen.Height * scale));
        return width * height;
    }

    /// <summary>
    /// Rejects shapes the size of a letter that sit inside a word. Without this,
    /// the "o" in "OK" becomes a circle and the merge step then prefers it over
    /// the button that contains the text.
    ///
    /// Sitting in a wide line is not enough to call something a letter: a row of
    /// toolbar icons is also detected as one wide line. What separates the two is
    /// adjacency — letters in a word are a few pixels apart, icons are not.
    /// </summary>
    private static bool IsGlyphNoise(
        VisualCandidate candidate,
        List<Rectangle> lines,
        IReadOnlyList<VisualCandidate> siblings)
    {
        if (candidate.Shape is not (VisualShape.Circle or VisualShape.Icon) || candidate.Bounds.Height > 22)
        {
            return false;
        }

        foreach (var line in lines)
        {
            if (line.Width < 1.8 * candidate.Bounds.Width || !TextLayout.ContainsMostly(line, candidate.Bounds))
            {
                continue;
            }

            foreach (var sibling in siblings)
            {
                if (ReferenceEquals(sibling, candidate) || sibling.Bounds.Height is < 8 or > 24)
                {
                    continue;
                }

                int gap = HorizontalGap(candidate.Bounds, sibling.Bounds);

                if (gap >= 0 && gap <= 8 && TextLayout.ContainsMostly(line, sibling.Bounds))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Glyph inside a line much wider than itself: a letter, not an icon.</summary>
    private static bool IsLetterInLine(VisualCandidate candidate, List<Rectangle> lines)
    {
        if (candidate.Shape is not (VisualShape.Circle or VisualShape.Icon) || candidate.Bounds.Height > 40)
        {
            return false;
        }

        return lines.Any(line =>
            line.Width >= 1.8 * candidate.Bounds.Width &&
            TextLayout.ContainsMostly(line, candidate.Bounds));
    }

    private static int HorizontalGap(Rectangle a, Rectangle b)
    {
        if (a.Right < b.Left) return b.Left - a.Right;
        return b.Right < a.Left ? a.Left - b.Right : 0;
    }

    private static DetectionFrame? BuildFrame(Image32 screen, double scale)
    {
        int width = Math.Max(24, (int)Math.Round(screen.Width * scale));
        int height = Math.Max(24, (int)Math.Round(screen.Height * scale));

        if (width == screen.Width && height == screen.Height)
        {
            return new DetectionFrame(screen, 1.0);
        }

        return new DetectionFrame(screen.Resize(width, height), (double)width / screen.Width);
    }

    /// <summary>
    /// Region-based high-resolution pass: a small candidate is re-examined in a
    /// 3x crop, which resolves shapes that are a few pixels wide at capture
    /// scale. Only the smallest candidates are refined to keep the cost flat.
    /// </summary>
    private static IEnumerable<VisualCandidate> RefineSmall(
        Image32 screen,
        List<VisualCandidate> raw,
        List<double> scales)
    {
        var refined = new List<VisualCandidate>();

        if (scales.Count == 0 || scales.Max() >= 1.9)
        {
            return refined;
        }

        var small = raw
            .Where(c => Math.Min(c.Bounds.Width, c.Bounds.Height) < 26)
            .OrderBy(c => c.Area)
            .Take(16);

        foreach (var candidate in small)
        {
            var area = Rectangle.Intersect(
                Rectangle.Inflate(candidate.Bounds, 8, 8),
                new Rectangle(0, 0, screen.Width, screen.Height));

            if (area.Width < 6 || area.Height < 6 || area.Width > 320 || area.Height > 320)
            {
                continue;
            }

            const double upscale = 3.0;
            var crop = screen.Crop(area);
            var resized = crop.Resize(
                Math.Max(8, (int)Math.Round(crop.Width * upscale)),
                Math.Max(8, (int)Math.Round(crop.Height * upscale)));

            var frame = new DetectionFrame(resized, upscale);
            var found = new List<VisualCandidate>();
            found.AddRange(VisualDetectors.Circles(frame));
            found.AddRange(VisualDetectors.Icons(frame));

            foreach (var item in found)
            {
                var bounds = item.Bounds;
                int left = area.Left + (int)Math.Floor(bounds.Left / upscale);
                int top = area.Top + (int)Math.Floor(bounds.Top / upscale);
                int right = area.Left + (int)Math.Ceiling(bounds.Right / upscale);
                int bottom = area.Top + (int)Math.Ceiling(bounds.Bottom / upscale);

                var mapped = Rectangle.FromLTRB(left, top, Math.Max(left + 2, right), Math.Max(top + 2, bottom));

                // A refinement refines one candidate; it must not return a box
                // larger than that candidate. The crop is bigger than the target,
                // and an unconstrained result could swallow the whole control —
                // an underlined link was being replaced by the letters inside it.
                if (!Rectangle.Inflate(candidate.Bounds, 2, 2).Contains(mapped))
                {
                    continue;
                }

                refined.Add(item with
                {
                    Bounds = mapped,
                    Score = Math.Min(1, item.Score + 0.05)
                });
            }
        }

        return refined;
    }

    /// <summary>
    /// Ties shapes to text without reading it: text inside a shape confirms a
    /// control, a label under an icon extends the icon's hit area, and a
    /// glyph-sized circle inside a text line is dropped as a false positive.
    /// </summary>
    private static List<VisualCandidate> Associate(
        IReadOnlyList<VisualCandidate> candidates,
        List<Rectangle> textLines)
    {
        if (textLines.Count == 0)
        {
            return candidates.ToList();
        }

        var results = new List<VisualCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            bool glyph = candidate.Bounds.Width < 20 &&
                         textLines.Any(line => TextLayout.ContainsMostly(line, candidate.Bounds));

            if (glyph && candidate.Shape is VisualShape.Circle or VisualShape.Icon)
            {
                continue;
            }

            var updated = candidate;

            if (textLines.Any(line => TextLayout.ContainsMostly(candidate.Bounds, line)))
            {
                updated = updated with { Score = Math.Min(1, updated.Score + 0.08) };
            }

            if (candidate.Shape == VisualShape.Icon)
            {
                foreach (var line in textLines)
                {
                    if (!TextLayout.IsLabelBelow(candidate.Bounds, line))
                    {
                        continue;
                    }

                    updated = updated with
                    {
                        Bounds = Rectangle.Union(candidate.Bounds, line),
                        Detector = VisualDetector.TextAssociation,
                        Score = Math.Min(1, updated.Score + 0.05)
                    };
                    break;
                }
            }

            results.Add(updated);
        }

        return results;
    }
}
