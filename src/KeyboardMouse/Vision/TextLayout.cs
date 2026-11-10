using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>
/// Groups glyph-sized edge blobs into text lines. This is deliberately not OCR:
/// the pipeline only needs to know *where* text is so shapes can be associated
/// with it (text inside a button, a label under a toolbar icon). Recognition is
/// a separate concern and this build has no OCR engine.
/// </summary>
public static class TextLayout
{
    public const int MinGlyphHeight = 5;
    public const int MaxGlyphHeight = 30;

    public static List<Rectangle> FindTextLines(DetectionFrame frame)
    {
        var bands = new List<Rectangle>(64);

        foreach (var blob in frame.EdgeBlobs)
        {
            var bounds = blob.Bounds;

            if (bounds.Height < MinGlyphHeight || bounds.Height > MaxGlyphHeight)
            {
                continue;
            }

            if (bounds.Width < 3 || bounds.Width > 400)
            {
                continue;
            }

            bands.Add(bounds);
        }

        // Merge glyphs into lines. A handful of passes converges; each pass only
        // joins boxes that already share most of their vertical extent.
        for (int pass = 0; pass < 6; pass++)
        {
            bool merged = false;

            for (int i = 0; i < bands.Count; i++)
            {
                for (int j = bands.Count - 1; j > i; j--)
                {
                    if (!CanJoin(bands[i], bands[j]))
                    {
                        continue;
                    }

                    bands[i] = Rectangle.Union(bands[i], bands[j]);
                    bands.RemoveAt(j);
                    merged = true;
                }
            }

            if (!merged)
            {
                break;
            }
        }

        var lines = new List<Rectangle>(bands.Count);

        foreach (var band in bands)
        {
            if (band.Width < 10 || band.Width < 1.4 * band.Height)
            {
                continue;
            }

            if (frame.Edges.Density(band) < 0.04)
            {
                continue;
            }

            lines.Add(band);
        }

        return lines;
    }

    public static bool ContainsMostly(Rectangle container, Rectangle inner)
    {
        var intersection = Rectangle.Intersect(container, inner);

        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return false;
        }

        double area = (double)inner.Width * inner.Height;
        return area > 0 && (intersection.Width * (double)intersection.Height) / area >= 0.6;
    }

    /// <summary>A label sitting under an icon, within the usual toolbar spacing.</summary>
    public static bool IsLabelBelow(Rectangle icon, Rectangle label, int maxGap = 24)
    {
        int gap = label.Top - icon.Bottom;

        if (gap < -2 || gap > maxGap)
        {
            return false;
        }

        int overlap = Math.Min(icon.Right, label.Right) - Math.Max(icon.Left, label.Left);
        int narrower = Math.Min(icon.Width, label.Width);

        return narrower > 0 && overlap >= 0.3 * narrower;
    }

    private static bool CanJoin(Rectangle a, Rectangle b)
    {
        int overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

        if (overlap < 0.5 * Math.Min(a.Height, b.Height))
        {
            return false;
        }

        int gap = Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right);
        return gap <= 7;
    }
}
