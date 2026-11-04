using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using KeyboardMouse.Core;

namespace KeyboardMouse.Capture;

/// <summary>
/// Read-only BGRA pixel snapshot of one capture frame. Copying the frame once
/// is cheaper than locking a bitmap and marshalling rows for every candidate.
/// The buffer is process-local and discarded when the scan ends.
/// </summary>
internal sealed class CvPixelSource
{
    private CvPixelSource(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra { get; }

    public Rectangle Frame => new(0, 0, Width, Height);

    public static CvPixelSource Create(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        Bitmap? converted = null;
        Bitmap source = bitmap;

        if (bitmap.PixelFormat != PixelFormat.Format32bppArgb &&
            bitmap.PixelFormat != PixelFormat.Format32bppPArgb &&
            bitmap.PixelFormat != PixelFormat.Format32bppRgb)
        {
            converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(converted))
            {
                graphics.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }

            source = converted;
        }

        try
        {
            var frame = new Rectangle(0, 0, source.Width, source.Height);
            BitmapData data = source.LockBits(frame, ImageLockMode.ReadOnly, source.PixelFormat);

            try
            {
                int stride = Math.Abs(data.Stride);
                bool bottomUp = data.Stride < 0;
                var row = new byte[stride];
                int rowBytes = source.Width * 4;
                var pixels = new byte[source.Width * source.Height * 4];

                for (int y = 0; y < source.Height; y++)
                {
                    int sourceY = bottomUp ? source.Height - 1 - y : y;
                    IntPtr pointer = IntPtr.Add(data.Scan0, sourceY * stride);
                    Marshal.Copy(pointer, row, 0, stride);
                    Buffer.BlockCopy(row, 0, pixels, y * rowBytes, rowBytes);
                }

                return new CvPixelSource(source.Width, source.Height, pixels);
            }
            finally
            {
                source.UnlockBits(data);
            }
        }
        finally
        {
            converted?.Dispose();
        }
    }

    public (int Blue, int Green, int Red) GetPixel(int x, int y)
    {
        int offset = ((Math.Clamp(y, 0, Height - 1) * Width) + Math.Clamp(x, 0, Width - 1)) * 4;
        return (Bgra[offset], Bgra[offset + 1], Bgra[offset + 2]);
    }
}

/// <summary>Pixel-level geometry result before the final type/size validation.</summary>
internal readonly record struct CvGeometryResult(
    Rectangle Bounds,
    Point ClickPoint,
    double Confidence,
    TargetGeometry Geometry,
    Rectangle? InkBounds,
    bool UsedFallback);

/// <summary>Selected point and shape quality for one binary mask.</summary>
internal readonly record struct MaskInteriorPoint(
    Point Point,
    double BoundaryDistance,
    bool InsideMask);

/// <summary>
/// Distance-transform click-point selection. A point near the middle of the
/// largest inscribed area stays inside rounded, triangular or otherwise
/// irregular controls even when the bounding-box centre does not.
/// </summary>
internal static class MaskGeometry
{
    private const int OrthogonalCost = 2;
    private const int DiagonalCost = 3;
    private const int Infinity = 1 << 20;

    public static MaskInteriorPoint SelectInteriorPoint(
        bool[] mask,
        int width,
        int height,
        Rectangle region,
        IReadOnlyList<Point> preferred)
    {
        if (mask.Length != width * height || width <= 0 || height <= 0)
        {
            return new MaskInteriorPoint(Point.Empty, 0, false);
        }

        region = Rectangle.Intersect(region, new Rectangle(0, 0, width, height));
        if (region.Width <= 0 || region.Height <= 0)
        {
            return new MaskInteriorPoint(Point.Empty, 0, false);
        }

        int[] distance = BuildDistanceTransform(mask, width, height);
        int maxDistance = 0;

        for (int y = region.Top; y < region.Bottom; y++)
        {
            int row = y * width;
            for (int x = region.Left; x < region.Right; x++)
            {
                if (mask[row + x])
                {
                    maxDistance = Math.Max(maxDistance, distance[row + x]);
                }
            }
        }

        if (maxDistance <= 0)
        {
            Point fallback = FindNearestPreferred(region, preferred);
            return new MaskInteriorPoint(
                fallback,
                0,
                IsInside(mask, width, height, fallback));
        }

        int minimum = Math.Max(OrthogonalCost, maxDistance * 3 / 4);
        Point best = Point.Empty;
        int bestPreferredDistance = int.MaxValue;
        int bestDistance = -1;

        for (int y = region.Top; y < region.Bottom; y++)
        {
            int row = y * width;
            for (int x = region.Left; x < region.Right; x++)
            {
                int value = distance[row + x];
                if (!mask[row + x] || value < minimum)
                {
                    continue;
                }

                int preferredDistance = int.MaxValue;
                for (int i = 0; i < preferred.Count; i++)
                {
                    Point candidate = preferred[i];
                    int d = Math.Abs(candidate.X - x) + Math.Abs(candidate.Y - y);
                    preferredDistance = Math.Min(preferredDistance, d);
                }

                if (value > bestDistance ||
                    (value == bestDistance && preferredDistance < bestPreferredDistance))
                {
                    best = new Point(x, y);
                    bestDistance = value;
                    bestPreferredDistance = preferredDistance;
                }
            }
        }

        if (bestDistance < 0)
        {
            best = FindNearestPreferred(region, preferred);
        }

        return new MaskInteriorPoint(
            best,
            bestDistance / 2.0,
            IsInside(mask, width, height, best));
    }

    private static int[] BuildDistanceTransform(bool[] mask, int width, int height)
    {
        var distance = new int[mask.Length];

        for (int i = 0; i < mask.Length; i++)
        {
            distance[i] = mask[i] ? Infinity : 0;
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                int index = row + x;
                if (distance[index] <= 0)
                {
                    continue;
                }

                int value = distance[index];

                if (x > 0)
                {
                    value = Math.Min(value, distance[index - 1] + OrthogonalCost);
                }

                if (y > 0)
                {
                    value = Math.Min(value, distance[index - width] + OrthogonalCost);

                    if (x > 0)
                    {
                        value = Math.Min(value, distance[index - width - 1] + DiagonalCost);
                    }

                    if (x + 1 < width)
                    {
                        value = Math.Min(value, distance[index - width + 1] + DiagonalCost);
                    }
                }

                distance[index] = value;
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            int row = y * width;

            for (int x = width - 1; x >= 0; x--)
            {
                int index = row + x;
                if (distance[index] <= 0)
                {
                    continue;
                }

                int value = distance[index];

                if (x + 1 < width)
                {
                    value = Math.Min(value, distance[index + 1] + OrthogonalCost);
                }

                if (y + 1 < height)
                {
                    value = Math.Min(value, distance[index + width] + OrthogonalCost);

                    if (x + 1 < width)
                    {
                        value = Math.Min(value, distance[index + width + 1] + DiagonalCost);
                    }

                    if (x > 0)
                    {
                        value = Math.Min(value, distance[index + width - 1] + DiagonalCost);
                    }
                }

                distance[index] = value;
            }
        }

        return distance;
    }

    private static Point FindNearestPreferred(Rectangle region, IReadOnlyList<Point> preferred)
    {
        Point centre = new(
            region.Left + (region.Width / 2),
            region.Top + (region.Height / 2));

        if (preferred.Count == 0)
        {
            return centre;
        }

        Point best = preferred[0];
        int bestDistance = int.MaxValue;

        foreach (Point candidate in preferred)
        {
            int distance = Math.Abs(candidate.X - centre.X) + Math.Abs(candidate.Y - centre.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return new Point(
            Math.Clamp(best.X, region.Left, region.Right - 1),
            Math.Clamp(best.Y, region.Top, region.Bottom - 1));
    }

    private static bool IsInside(bool[] mask, int width, int height, Point point)
    {
        if ((uint)point.X >= (uint)width || (uint)point.Y >= (uint)height)
        {
            return false;
        }

        return mask[(point.Y * width) + point.X];
    }
}

/// <summary>
/// Refines a detector rectangle against actual pixels. Edge support, background
/// contrast, shape fill and shadow trimming produce a tight box, an interior
/// click point and a geometry confidence without touching the input bitmap.
/// </summary>
internal static class CvGeometryRefiner
{
    public static CvGeometryResult Refine(
        CvPixelSource source,
        Rectangle candidateBounds,
        TargetType type,
        Rectangle? textBounds,
        double detectionConfidence,
        double dpiScale = 1.0)
    {
        Rectangle frame = source.Frame;
        Rectangle candidate = Rectangle.Intersect(candidateBounds, frame);

        if (candidate.Width < 4 || candidate.Height < 4)
        {
            return Fallback(candidate, detectionConfidence, GeometryIssue.TooSmall);
        }

        int padding = Math.Clamp(Math.Min(candidate.Width, candidate.Height) / 5, 3, 10);
        Rectangle crop = Rectangle.Intersect(Inflate(candidate, padding), frame);

        if (crop.Width < 8 || crop.Height < 8)
        {
            return Fallback(candidate, detectionConfidence, GeometryIssue.AmbiguousShape);
        }

        var localCandidate = new Rectangle(
            candidate.X - crop.X,
            candidate.Y - crop.Y,
            candidate.Width,
            candidate.Height);

        var background = EstimateBackground(source, crop, localCandidate);
        if (background is null)
        {
            return Fallback(candidate, detectionConfidence, GeometryIssue.UsedFallback);
        }

        (int red, int green, int blue, double deviation) = background.Value;
        double threshold = Math.Clamp(10 + (deviation * 2.5), 10, 68);
        int width = crop.Width;
        int height = crop.Height;
        var foreground = new bool[width * height];
        var backgroundLike = new bool[width * height];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                (int pixelBlue, int pixelGreen, int pixelRed) =
                    source.GetPixel(crop.X + x, crop.Y + y);

                double distance = ColorDistance(
                    pixelRed,
                    pixelGreen,
                    pixelBlue,
                    red,
                    green,
                    blue);

                int index = row + x;
                backgroundLike[index] = distance <= threshold;
                foreground[index] = !backgroundLike[index];
            }
        }

        bool[] reachable = FloodBackground(backgroundLike, width, height);
        // The detector can be a few pixels loose. Search only the candidate
        // plus a small halo; this prevents a nearby control from being merged
        // into this target's component.
        int halo = Math.Max(2, padding / 2);
        Rectangle searchArea = Rectangle.Intersect(
            Inflate(localCandidate, halo),
            new Rectangle(0, 0, width, height));

        Component? component = SelectComponent(
            reachable,
            width,
            height,
            searchArea,
            localCandidate);

        if (component is null || component.Value.Area < 6)
        {
            return Fallback(candidate, detectionConfidence, GeometryIssue.UsedFallback);
        }

        Rectangle box = component.Value.Bounds;
        int componentArea = component.Value.Area;
        long boxArea = (long)box.Width * box.Height;

        if (boxArea <= 0 || componentArea * 100 < boxArea * 6)
        {
            return Fallback(candidate, detectionConfidence, GeometryIssue.AmbiguousShape);
        }

        double fillDensity = componentArea / (double)boxArea;
        bool shadowTrimmed = false;

        if (fillDensity >= 0.58)
        {
            Rectangle trimmed = TrimProjection(
                component.Value.Mask,
                width,
                box,
                padding);
            shadowTrimmed = trimmed != box;
            box = trimmed;
        }

        if (box.Width < 3 || box.Height < 3)
        {
            return Fallback(candidate, detectionConfidence, GeometryIssue.TooSmall);
        }

        int roundedCorners = CountBackgroundCorners(
            component.Value.Mask,
            reachable,
            width,
            height,
            box);
        bool rounded = roundedCorners >= 2;

        double edgeSupport = MeasureEdgeSupport(
            component.Value.Mask,
            reachable,
            width,
            height,
            box);

        double meanContrast = MeasureContrast(
            component.Value.Mask,
            source,
            crop,
            width,
            height,
            box,
            red,
            green,
            blue);
        double contrastNorm = Math.Clamp(meanContrast / 160.0, 0, 1);

        bool useSolidMask =
            fillDensity >= 0.45 ||
            type is TargetType.Button or TargetType.Edit or TargetType.ComboBox
                or TargetType.MenuItem or TargetType.TabItem;
        bool[] clickMask = BuildClickMask(
            component.Value.Mask,
            width,
            height,
            box,
            rounded,
            useSolidMask);

        Rectangle globalBox = new(
            crop.X + box.X,
            crop.Y + box.Y,
            box.Width,
            box.Height);

        Point clickPoint = SelectClickPoint(
            clickMask,
            width,
            height,
            box,
            globalBox,
            type,
            textBounds,
            out ClickPointStrategy strategy,
            out bool shapeAwayFromCentre);

        GeometryIssue issues = GeometryIssue.None;
        if (rounded)
        {
            issues |= GeometryIssue.RoundedCorners;
        }

        if (shadowTrimmed)
        {
            issues |= GeometryIssue.ShadowIncluded;
        }

        if (edgeSupport < 0.45)
        {
            issues |= GeometryIssue.WeakEdges;
        }

        if (contrastNorm < 0.15)
        {
            issues |= GeometryIssue.WeakEdges;
        }

        if (shapeAwayFromCentre)
        {
            issues |= GeometryIssue.AmbiguousShape;
        }

        double integrity = Math.Clamp(
            componentArea / Math.Max(1.0, localCandidate.Width * localCandidate.Height * 0.60),
            0,
            1);

        double confidence =
            0.30 +
            (0.30 * edgeSupport) +
            (0.15 * contrastNorm) +
            (0.15 * integrity) +
            (0.10 * Math.Clamp(detectionConfidence, 0, 1));

        if (shadowTrimmed)
        {
            confidence -= 0.03;
        }

        if (rounded)
        {
            confidence += 0.02;
        }

        if (fillDensity < 0.12)
        {
            confidence -= 0.10;
        }

        var geometry = new TargetGeometry
        {
            Confidence = Math.Clamp(confidence, 0.05, 0.99),
            ClickStrategy = strategy,
            Issues = issues,
            EdgeSupport = Math.Clamp(edgeSupport, 0, 1),
            BackgroundContrast = contrastNorm,
            HasRoundedCorners = rounded,
            IsClipped = globalBox.Left < frame.Left ||
                        globalBox.Top < frame.Top ||
                        globalBox.Right > frame.Right ||
                        globalBox.Bottom > frame.Bottom,
            UsedFallback = false
        };

        return new CvGeometryResult(
            globalBox,
            clickPoint,
            geometry.Confidence,
            geometry,
            globalBox,
            UsedFallback: false);
    }

    private static Point SelectClickPoint(
        bool[] mask,
        int width,
        int height,
        Rectangle box,
        Rectangle globalBox,
        TargetType type,
        Rectangle? textBounds,
        out ClickPointStrategy strategy,
        out bool shapeAwayFromCentre)
    {
        Point centre = new(
            box.Left + (box.Width / 2),
            box.Top + (box.Height / 2));

        var preferred = new List<Point>(4) { centre };
        var labels = new List<ClickPointStrategy>(4) { ClickPointStrategy.Center };

        if (textBounds is { } text &&
            box.Width > 0 &&
            box.Height > 0)
        {
            Point textCentre = new(
                text.Left + (text.Width / 2) - (globalBox.Left - box.Left),
                text.Top + (text.Height / 2) - (globalBox.Top - box.Top));

            if (box.Contains(textCentre))
            {
                preferred.Add(textCentre);
                labels.Add(ClickPointStrategy.TextCenter);
            }
        }

        Point centroid = MaskCentroid(mask, width, height, box);
        if (box.Contains(centroid))
        {
            preferred.Add(centroid);
            labels.Add(type == TargetType.Icon
                ? ClickPointStrategy.IconCenter
                : ClickPointStrategy.ShapeCenter);
        }

        MaskInteriorPoint selected = MaskGeometry.SelectInteriorPoint(
            mask,
            width,
            height,
            box,
            preferred);

        Point local = selected.Point;
        Point global = new(
            globalBox.Left + (local.X - box.Left),
            globalBox.Top + (local.Y - box.Top));

        int centreDistance =
            Math.Abs(local.X - centre.X) + Math.Abs(local.Y - centre.Y);
        bool centreInside = IsMaskSet(mask, width, height, centre);

        shapeAwayFromCentre = !centreInside ||
                              centreDistance > Math.Max(3, Math.Min(box.Width, box.Height) / 8);

        if (!centreInside)
        {
            strategy = type == TargetType.Icon
                ? ClickPointStrategy.IconCenter
                : ClickPointStrategy.ShapeCenter;
        }
        else if (centreDistance <= Math.Max(2, Math.Min(box.Width, box.Height) / 16))
        {
            strategy = ClickPointStrategy.SafeInterior;
        }
        else
        {
            int nearest = 0;
            int nearestDistance = int.MaxValue;

            for (int i = 0; i < preferred.Count; i++)
            {
                int distance =
                    Math.Abs(preferred[i].X - local.X) +
                    Math.Abs(preferred[i].Y - local.Y);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }

            strategy = labels[nearest];
        }

        return globalBox.Contains(global) ? global : globalBox.Location;
    }

    private static bool[] BuildClickMask(
        bool[] component,
        int width,
        int height,
        Rectangle box,
        bool rounded,
        bool useSolidMask)
    {
        var mask = new bool[width * height];

        if (useSolidMask)
        {
            int radius = rounded
                ? Math.Clamp(Math.Min(box.Width, box.Height) / 6, 2, Math.Min(box.Width, box.Height) / 2)
                : 0;

            for (int y = box.Top; y < box.Bottom; y++)
            {
                int row = y * width;

                for (int x = box.Left; x < box.Right; x++)
                {
                    if (radius > 0 && IsRoundedCornerOutside(box, x, y, radius))
                    {
                        continue;
                    }

                    mask[row + x] = true;
                }
            }

            return mask;
        }

        for (int y = box.Top; y < box.Bottom; y++)
        {
            int row = y * width;

            for (int x = box.Left; x < box.Right; x++)
            {
                mask[row + x] = component[row + x];
            }
        }

        return mask;
    }

    private static bool IsRoundedCornerOutside(Rectangle box, int x, int y, int radius)
    {
        int leftDistance = x - box.Left;
        int rightDistance = box.Right - 1 - x;
        int topDistance = y - box.Top;
        int bottomDistance = box.Bottom - 1 - y;

        int dx = Math.Min(leftDistance, rightDistance);
        int dy = Math.Min(topDistance, bottomDistance);

        if (dx >= radius || dy >= radius)
        {
            return false;
        }

        int ox = radius - dx;
        int oy = radius - dy;

        return (ox * ox) + (oy * oy) > radius * radius;
    }

    private static Point MaskCentroid(bool[] mask, int width, int height, Rectangle box)
    {
        long sumX = 0;
        long sumY = 0;
        long count = 0;

        for (int y = box.Top; y < box.Bottom; y++)
        {
            int row = y * width;

            for (int x = box.Left; x < box.Right; x++)
            {
                if (!mask[row + x])
                {
                    continue;
                }

                sumX += x;
                sumY += y;
                count++;
            }
        }

        if (count == 0)
        {
            return new Point(
                box.Left + (box.Width / 2),
                box.Top + (box.Height / 2));
        }

        return new Point(
            (int)(sumX / count),
            (int)(sumY / count));
    }

    private static bool IsMaskSet(bool[] mask, int width, int height, Point point)
    {
        return point.X >= 0 &&
               point.Y >= 0 &&
               point.X < width &&
               point.Y < height &&
               mask[(point.Y * width) + point.X];
    }

    private static Rectangle TrimProjection(
        bool[] mask,
        int width,
        Rectangle box,
        int padding)
    {
        var rowCounts = new int[box.Height];
        var columnCounts = new int[box.Width];

        for (int y = 0; y < box.Height; y++)
        {
            int row = (box.Top + y) * width;

            for (int x = 0; x < box.Width; x++)
            {
                if (!mask[row + box.Left + x])
                {
                    continue;
                }

                rowCounts[y]++;
                columnCounts[x]++;
            }
        }

        int maxRow = rowCounts.Length > 0 ? rowCounts.Max() : 0;
        int maxColumn = columnCounts.Length > 0 ? columnCounts.Max() : 0;

        if (maxRow <= 0 || maxColumn <= 0)
        {
            return box;
        }

        int rowThreshold = Math.Max(1, maxRow * 35 / 100);
        int columnThreshold = Math.Max(1, maxColumn * 35 / 100);
        int maxTrim = Math.Max(2, padding);

        int top = 0;
        while (top < rowCounts.Length && rowCounts[top] < rowThreshold && top <= maxTrim)
        {
            top++;
        }

        int bottom = rowCounts.Length - 1;
        while (bottom >= 0 &&
               rowCounts[bottom] < rowThreshold &&
               rowCounts.Length - 1 - bottom <= maxTrim)
        {
            bottom--;
        }

        int left = 0;
        while (left < columnCounts.Length && columnCounts[left] < columnThreshold && left <= maxTrim)
        {
            left++;
        }

        int right = columnCounts.Length - 1;
        while (right >= 0 &&
               columnCounts[right] < columnThreshold &&
               columnCounts.Length - 1 - right <= maxTrim)
        {
            right--;
        }

        int newWidth = right - left + 1;
        int newHeight = bottom - top + 1;

        if (newWidth < Math.Max(3, box.Width / 2) ||
            newHeight < Math.Max(3, box.Height / 2))
        {
            return box;
        }

        return new Rectangle(
            box.Left + left,
            box.Top + top,
            newWidth,
            newHeight);
    }

    private static int CountBackgroundCorners(
        bool[] component,
        bool[] reachable,
        int width,
        int height,
        Rectangle box)
    {
        int size = Math.Clamp(Math.Min(box.Width, box.Height) / 5, 2, 10);
        int corners = 0;

        corners += IsCornerBackground(component, reachable, width, height, box.Left, box.Top, size, 1, 1) ? 1 : 0;
        corners += IsCornerBackground(component, reachable, width, height, box.Right - size, box.Top, size, 1, -1) ? 1 : 0;
        corners += IsCornerBackground(component, reachable, width, height, box.Left, box.Bottom - size, size, -1, 1) ? 1 : 0;
        corners += IsCornerBackground(component, reachable, width, height, box.Right - size, box.Bottom - size, size, -1, -1) ? 1 : 0;

        Point centre = new(
            box.Left + (box.Width / 2),
            box.Top + (box.Height / 2));

        if (!IsMaskSet(component, width, height, centre))
        {
            return 0;
        }

        return corners;
    }

    private static bool IsCornerBackground(
        bool[] component,
        bool[] reachable,
        int width,
        int height,
        int startX,
        int startY,
        int size,
        int directionX,
        int directionY)
    {
        int background = 0;
        int total = 0;

        for (int y = 0; y < size; y++)
        {
            int py = startY + (y * directionY);

            for (int x = 0; x < size; x++)
            {
                int px = startX + (x * directionX);
                if ((uint)px >= (uint)width || (uint)py >= (uint)height)
                {
                    continue;
                }

                int index = (py * width) + px;
                total++;

                // A corner is empty only when it belongs to the background that
                // is connected to the outside. The same-colour interior of a
                // square button is unreachable and therefore not a rounding.
                if (!component[index] && reachable[index])
                {
                    background++;
                }
            }
        }

        return total > 0 && background * 100 >= total * 55;
    }

    private static double MeasureEdgeSupport(
        bool[] component,
        bool[] reachable,
        int width,
        int height,
        Rectangle box)
    {
        double total = 0;
        total += SideSupport(component, reachable, width, height, box, EdgeSide.Left);
        total += SideSupport(component, reachable, width, height, box, EdgeSide.Right);
        total += SideSupport(component, reachable, width, height, box, EdgeSide.Top);
        total += SideSupport(component, reachable, width, height, box, EdgeSide.Bottom);

        return total / 4.0;
    }

    private enum EdgeSide
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private static double SideSupport(
        bool[] component,
        bool[] reachable,
        int width,
        int height,
        Rectangle box,
        EdgeSide side)
    {
        bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
        int length = horizontal ? box.Width : box.Height;

        if (length <= 0)
        {
            return 0;
        }

        int start = length / 5;
        int end = length - start;
        int supported = 0;
        int samples = 0;

        for (int i = start; i < end; i++)
        {
            int x = horizontal ? box.Left + i : (side == EdgeSide.Left ? box.Left : box.Right - 1);
            int y = horizontal ? (side == EdgeSide.Top ? box.Top : box.Bottom - 1) : box.Top + i;

            int inside = (y * width) + x;
            int outsideX = x + (side == EdgeSide.Left ? -1 : side == EdgeSide.Right ? 1 : 0);
            int outsideY = y + (side == EdgeSide.Top ? -1 : side == EdgeSide.Bottom ? 1 : 0);
            int outside = (outsideY * width) + outsideX;

            samples++;

            if (component[inside] &&
                outsideX >= 0 &&
                outsideY >= 0 &&
                outsideX < width &&
                outsideY < height &&
                reachable[outside])
            {
                supported++;
            }
        }

        return samples > 0 ? supported / (double)samples : 0;
    }

    private static double MeasureContrast(
        bool[] component,
        CvPixelSource source,
        Rectangle crop,
        int width,
        int height,
        Rectangle box,
        int backgroundRed,
        int backgroundGreen,
        int backgroundBlue)
    {
        double sum = 0;
        int count = 0;

        for (int y = box.Top; y < Math.Min(box.Bottom, height); y++)
        {
            int row = y * width;

            for (int x = box.Left; x < Math.Min(box.Right, width); x++)
            {
                if (!component[row + x])
                {
                    continue;
                }

                (int blue, int green, int red) =
                    source.GetPixel(crop.X + x, crop.Y + y);

                sum += ColorDistance(
                    red,
                    green,
                    blue,
                    backgroundRed,
                    backgroundGreen,
                    backgroundBlue);
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }

    private static (int Red, int Green, int Blue, double Deviation)? EstimateBackground(
        CvPixelSource source,
        Rectangle crop,
        Rectangle candidate)
    {
        var samples = new List<(int Red, int Green, int Blue)>(256);

        // Prefer the ring immediately outside the candidate; it describes the
        // surface the control is drawn on rather than the control itself.
        for (int y = Math.Max(0, candidate.Top - 2); y < Math.Min(crop.Height, candidate.Bottom + 2); y++)
        {
            int globalY = crop.Y + y;

            for (int x = Math.Max(0, candidate.Left - 2); x < Math.Min(crop.Width, candidate.Right + 2); x++)
            {
                bool outside = x < candidate.Left ||
                               x >= candidate.Right ||
                               y < candidate.Top ||
                               y >= candidate.Bottom;

                if (!outside)
                {
                    continue;
                }

                AddSample(source, crop.X + x, globalY, samples);
            }
        }

        if (samples.Count < 8)
        {
            int border = Math.Max(1, Math.Min(2, Math.Min(crop.Width, crop.Height) / 4));

            for (int y = border; y < crop.Height - border; y++)
            {
                int globalY = crop.Y + y;

                for (int x = border; x < crop.Width - border; x++)
                {
                    if (y >= candidate.Top && y < candidate.Bottom &&
                        x >= candidate.Left && x < candidate.Right)
                    {
                        continue;
                    }

                    AddSample(source, crop.X + x, globalY, samples);
                }
            }
        }

        if (samples.Count == 0)
        {
            return null;
        }

        double red = Median(samples.Select(sample => sample.Red));
        double green = Median(samples.Select(sample => sample.Green));
        double blue = Median(samples.Select(sample => sample.Blue));

        double deviation = samples.Average(sample =>
            ColorDistance(sample.Red, sample.Green, sample.Blue, red, green, blue));

        return ((int)red, (int)green, (int)blue, deviation);
    }

    private static int Median(IEnumerable<int> values)
    {
        int[] ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return 0;
        }

        return ordered[ordered.Length / 2];
    }

    private static void AddSample(
        CvPixelSource source,
        int x,
        int y,
        List<(int Red, int Green, int Blue)> samples)
    {
        if ((uint)x >= (uint)source.Width || (uint)y >= (uint)source.Height)
        {
            return;
        }

        (int blue, int green, int red) = source.GetPixel(x, y);
        samples.Add((red, green, blue));
    }

    private static double ColorDistance(
        int redA,
        int greenA,
        int blueA,
        double redB,
        double greenB,
        double blueB)
    {
        double dr = redA - redB;
        double dg = greenA - greenB;
        double db = blueA - blueB;

        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static bool[] FloodBackground(bool[] background, int width, int height)
    {
        var reachable = new bool[background.Length];
        var stack = new int[background.Length];
        int top = 0;

        void Push(int index)
        {
            if (!background[index] || reachable[index])
            {
                return;
            }

            reachable[index] = true;
            stack[top++] = index;
        }

        for (int x = 0; x < width; x++)
        {
            Push(x);
            Push(((height - 1) * width) + x);
        }

        for (int y = 0; y < height; y++)
        {
            Push(y * width);
            Push((y * width) + width - 1);
        }

        while (top > 0)
        {
            int index = stack[--top];
            int x = index % width;
            int y = index / width;

            if (x > 0)
            {
                Push(index - 1);
            }

            if (x + 1 < width)
            {
                Push(index + 1);
            }

            if (y > 0)
            {
                Push(index - width);
            }

            if (y + 1 < height)
            {
                Push(index + width);
            }
        }

        return reachable;
    }

    private readonly record struct Component(Rectangle Bounds, int Area, bool[] Mask);

    private static Component? SelectComponent(
        bool[] reachable,
        int width,
        int height,
        Rectangle search,
        Rectangle candidate)
    {
        var visited = new bool[reachable.Length];
        var stack = new int[reachable.Length];
        Component? best = null;
        long bestScore = long.MinValue;
        int centreX = candidate.Left + (candidate.Width / 2);
        int centreY = candidate.Top + (candidate.Height / 2);

        for (int startY = search.Top; startY < search.Bottom; startY++)
        {
            int row = startY * width;

            for (int startX = search.Left; startX < search.Right; startX++)
            {
                int start = row + startX;

                if (reachable[start] || visited[start])
                {
                    continue;
                }

                int top = 0;
                stack[top++] = start;
                visited[start] = true;

                int minX = width;
                int minY = height;
                int maxX = 0;
                int maxY = 0;
                int area = 0;
                bool containsCentre = false;
                var componentMask = new bool[reachable.Length];

                while (top > 0)
                {
                    int index = stack[--top];
                    int x = index % width;
                    int y = index / width;

                    componentMask[index] = true;
                    area++;

                    if (x == centreX && y == centreY)
                    {
                        containsCentre = true;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);

                    TryVisit(index - 1, x > search.Left);
                    TryVisit(index + 1, x + 1 < search.Right);
                    TryVisit(index - width, y > search.Top);
                    TryVisit(index + width, y + 1 < search.Bottom);
                }

                if (area < 4)
                {
                    continue;
                }

                var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                long overlap = OverlapArea(bounds, candidate);

                // Prefer the component that explains the detector box, not a
                // small glyph that happens to sit at its centre. Centre
                // containment is only a tie-breaker, so an outlined button's
                // border or interior wins over the label inside it.
                long score =
                    (overlap * 10_000) +
                    (containsCentre ? 1_000 : 0) +
                    area;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Component(bounds, area, componentMask);
                }

                void TryVisit(int index, bool valid)
                {
                    if (!valid || reachable[index] || visited[index])
                    {
                        return;
                    }

                    visited[index] = true;
                    stack[top++] = index;
                }
            }
        }

        return best;
    }

    private static long OverlapArea(Rectangle a, Rectangle b)
    {
        Rectangle overlap = Rectangle.Intersect(a, b);
        return overlap.Width <= 0 || overlap.Height <= 0
            ? 0
            : (long)overlap.Width * overlap.Height;
    }

    private static CvGeometryResult Fallback(
        Rectangle bounds,
        double detectionConfidence,
        GeometryIssue issue)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            bounds = new Rectangle(bounds.X, bounds.Y, 1, 1);
        }

        Point centre = new(
            bounds.Left + (bounds.Width / 2),
            bounds.Top + (bounds.Height / 2));

        var geometry = new TargetGeometry
        {
            Confidence = Math.Clamp(0.20 + (0.10 * detectionConfidence), 0.05, 0.35),
            ClickStrategy = ClickPointStrategy.Center,
            Issues = issue | GeometryIssue.UsedFallback,
            EdgeSupport = 0,
            BackgroundContrast = 0,
            UsedFallback = true
        };

        return new CvGeometryResult(
            bounds,
            centre,
            geometry.Confidence,
            geometry,
            null,
            UsedFallback: true);
    }

    private static Rectangle Inflate(Rectangle rectangle, int amount) => new(
        rectangle.Left - amount,
        rectangle.Top - amount,
        rectangle.Width + (amount * 2),
        rectangle.Height + (amount * 2));
}
