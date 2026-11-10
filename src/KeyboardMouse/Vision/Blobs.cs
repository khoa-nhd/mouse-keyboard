using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>Connected region of foreground pixels with its bounding box.</summary>
public readonly record struct Blob(int Label, Rectangle Bounds, int Area, double BorderCoverage = 1.0)
{
    public double FillRatio => Bounds.Width <= 0 || Bounds.Height <= 0
        ? 0
        : (double)Area / (Bounds.Width * (double)Bounds.Height);

    public double Aspect => Bounds.Height <= 0 ? double.MaxValue : (double)Bounds.Width / Bounds.Height;

    public Point Center => new(
        Bounds.Left + (Bounds.Width / 2),
        Bounds.Top + (Bounds.Height / 2));
}

/// <summary>
/// Connected component labelling. This is the workhorse behind fill-region,
/// icon, circle and text-band detection: one labelling pass is shared instead
/// of each detector re-flooding the image.
/// </summary>
public static class Blobs
{
    public static List<Blob> FromBinary(GrayImage binary, int threshold = 128, bool eightConnected = true)
    {
        return Label(binary.Width, binary.Height, (x, y) => binary[x, y] >= threshold, eightConnected);
    }

    /// <summary>
    /// Segments by quantized colour. Flat-filled controls (buttons, cards, tab
    /// strips, text fields) become one component each, which separates adjacent
    /// controls without relying on their borders.
    /// </summary>
    public static List<Blob> FromColorKeys(Image32 image, int channelBits = 5)
    {
        int bits = Math.Clamp(channelBits, 1, 8);
        int shift = 8 - bits;
        var keys = new int[image.Width * image.Height];

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = (y * image.Width) + x;

                // Five bits per channel keeps a #F0F0F0 button distinct from a
                // #FFFFFF page; four bits rounded both to the same bucket and the
                // tile disappeared into the background blob.
                keys[i] = ((image.Red(x, y) >> shift) << (2 * bits)) |
                          ((image.Green(x, y) >> shift) << bits) |
                          (image.Blue(x, y) >> shift);
            }
        }

        // With an identity array the predicate is never consulted; colour
        // equality alone decides membership.
        return Label(image.Width, image.Height, (_, _) => true, false, keys);
    }

    private static List<Blob> Label(
        int width,
        int height,
        Func<int, int, bool> isForeground,
        bool eightConnected,
        int[]? identity = null)
    {
        var labels = new int[width * height];
        var stack = new Stack<int>(1024);
        var bounds = new List<Rectangle>();
        var areas = new List<int>();
        var result = new List<Blob>();
        int next = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int seed = (y * width) + x;

                if (labels[seed] != 0 || !isForeground(x, y))
                {
                    continue;
                }

                next++;
                labels[seed] = next;
                stack.Push(seed);

                int left = x, right = x, top = y, bottom = y, area = 0;
                int key = identity is null ? 0 : identity[seed];

                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int px = index % width;
                    int py = index / width;
                    area++;

                    if (px < left) left = px;
                    if (px > right) right = px;
                    if (py < top) top = py;
                    if (py > bottom) bottom = py;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }

                            if (!eightConnected && dx != 0 && dy != 0)
                            {
                                continue;
                            }

                            int nx = px + dx;
                            int ny = py + dy;

                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            {
                                continue;
                            }

                            int neighbour = (ny * width) + nx;

                            if (labels[neighbour] != 0)
                            {
                                continue;
                            }

                            if (identity is not null)
                            {
                                if (identity[neighbour] != key)
                                {
                                    continue;
                                }
                            }
                            else if (!isForeground(nx, ny))
                            {
                                continue;
                            }

                            labels[neighbour] = next;
                            stack.Push(neighbour);
                        }
                    }
                }

                bounds.Add(Rectangle.FromLTRB(left, top, right + 1, bottom + 1));
                areas.Add(area);
            }
        }

        int background = 0;
        int largest = 0;

        for (int i = 0; i < areas.Count; i++)
        {
            if (areas[i] > largest)
            {
                largest = areas[i];
                background = i + 1;
            }
        }

        for (int i = 0; i < bounds.Count; i++)
        {
            result.Add(new Blob(
                i + 1,
                bounds[i],
                areas[i],
                BorderCoverage(labels, bounds[i], background, width)));
        }

        return result;
    }

    /// <summary>
    /// Smallest share of the four bbox sides that is opaque, measured over the
    /// middle of each side. A control keeps a solid edge there even when rounded
    /// corners cut the ends or a child control covers part of it, while a glyph or
    /// a text line leaves the middle of its edge as page background.
    /// </summary>
    private static double BorderCoverage(int[] labels, Rectangle bounds, int background, int width)
    {
        int insetX = Math.Max(1, (int)(bounds.Width * 0.15));
        int insetY = Math.Max(1, (int)(bounds.Height * 0.15));
        int x0 = bounds.Left + insetX;
        int x1 = bounds.Right - insetX;
        int y0 = bounds.Top + insetY;
        int y1 = bounds.Bottom - insetY;

        if (x1 <= x0 || y1 <= y0)
        {
            return 0;
        }

        int top = 0, bottom = 0, leftSide = 0, rightSide = 0;

        for (int x = x0; x < x1; x++)
        {
            if (IsOpaque(labels[(bounds.Top * width) + x], background)) top++;
            if (IsOpaque(labels[((bounds.Bottom - 1) * width) + x], background)) bottom++;
        }

        for (int y = y0; y < y1; y++)
        {
            if (IsOpaque(labels[(y * width) + bounds.Left], background)) leftSide++;
            if (IsOpaque(labels[(y * width) + bounds.Right - 1], background)) rightSide++;
        }

        double topShare = (double)top / (x1 - x0);
        double bottomShare = (double)bottom / (x1 - x0);
        double leftShare = (double)leftSide / (y1 - y0);
        double rightShare = (double)rightSide / (y1 - y0);

        return Math.Min(Math.Min(topShare, bottomShare), Math.Min(leftShare, rightShare));
    }

    private static bool IsOpaque(int label, int background)
    {
        return label != 0 && label != background;
    }
}
