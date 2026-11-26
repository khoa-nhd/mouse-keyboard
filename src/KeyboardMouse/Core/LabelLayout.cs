using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Places hint labels so they do not cover each other. Every label prefers the
/// top-left corner of its target; when that spot is taken it tries the other
/// spots on the target, then steps beside it, and keeps the first free one.
/// Pure, so the placement is tested without drawing anything.
/// </summary>
public static class LabelLayout
{
    /// <summary>Space kept between two labels.</summary>
    public const float Gap = 2f;

    /// <param name="items">Each target with the size of its label, in reading order.</param>
    /// <param name="area">Where labels may go (the monitor, in the same coordinates).</param>
    /// <returns>Top-left corner of each label, in the order given.</returns>
    public static PointF[] Place(IReadOnlyList<(RectangleF Target, SizeF Size)> items, RectangleF area)
    {
        var placed = new List<RectangleF>(items.Count);
        var result = new PointF[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            var (target, size) = items[i];
            RectangleF best = RectangleF.Empty;
            float bestOverlap = float.MaxValue;

            foreach (var spot in Candidates(target, size))
            {
                var box = Clamp(new RectangleF(spot, size), area);
                float overlap = Overlap(box, placed);

                if (overlap < bestOverlap)
                {
                    best = box;
                    bestOverlap = overlap;

                    if (overlap <= 0)
                    {
                        break;
                    }
                }
            }

            placed.Add(best);
            result[i] = best.Location;
        }

        return result;
    }

    /// <summary>Spots to try, best first: the corner, the rest of the target, then beside it.</summary>
    private static IEnumerable<PointF> Candidates(RectangleF t, SizeF s)
    {
        float w = s.Width + Gap;
        float h = s.Height + Gap;

        yield return new PointF(t.Left, t.Top);
        yield return new PointF(t.Left + ((t.Width - s.Width) / 2f), t.Top + ((t.Height - s.Height) / 2f));
        yield return new PointF(t.Right - s.Width, t.Top);
        yield return new PointF(t.Left, t.Bottom - s.Height);
        yield return new PointF(t.Right - s.Width, t.Bottom - s.Height);

        for (int step = 1; step <= 3; step++)
        {
            yield return new PointF(t.Left + (step * w), t.Top);
            yield return new PointF(t.Left, t.Top + (step * h));
            yield return new PointF(t.Left - (step * w), t.Top);
            yield return new PointF(t.Left, t.Top - (step * h));
        }
    }

    private static RectangleF Clamp(RectangleF box, RectangleF area)
    {
        float x = Math.Clamp(box.X, area.Left + 2, Math.Max(area.Left + 2, area.Right - box.Width - 2));
        float y = Math.Clamp(box.Y, area.Top + 2, Math.Max(area.Top + 2, area.Bottom - box.Height - 2));
        return new RectangleF(x, y, box.Width, box.Height);
    }

    private static float Overlap(RectangleF box, List<RectangleF> placed)
    {
        var padded = RectangleF.Inflate(box, Gap / 2f, Gap / 2f);
        float total = 0;

        foreach (var other in placed)
        {
            var hit = RectangleF.Intersect(padded, other);

            if (hit.Width > 0 && hit.Height > 0)
            {
                total += hit.Width * hit.Height;
            }
        }

        return total;
    }
}
