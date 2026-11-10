using System.Drawing;

namespace KeyboardMouse.Vision;

/// <summary>
/// Single channel 8-bit image plus the convolution primitives the detectors
/// share: box blur, median, Sobel, Canny, Otsu, adaptive thresholding and
/// binary morphology. Everything is allocation-per-call on purpose — the scan
/// budget is measured in milliseconds, not frames per second, and predictable
/// ownership beats pooling.
/// </summary>
public sealed class GrayImage
{
    private long[]? _integral;

    public GrayImage(int width, int height)
        : this(width, height, new byte[width * height])
    {
    }

    public GrayImage(int width, int height, byte[] data)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        if (data.Length != width * height)
        {
            throw new ArgumentException("Gray buffer must hold width*height bytes.", nameof(data));
        }

        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Data { get; }

    public byte this[int x, int y] => Data[(y * Width) + x];

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public byte At(int x, int y) => Data[(Math.Clamp(y, 0, Height - 1) * Width) + Math.Clamp(x, 0, Width - 1)];

    public void Set(int x, int y, byte value)
    {
        Data[(y * Width) + x] = value;
        _integral = null;
    }

    /// <summary>Summed-area table, (Width+1)*(Height+1), row-major.</summary>
    public long[] Integral
    {
        get
        {
            if (_integral is not null)
            {
                return _integral;
            }

            int stride = Width + 1;
            var table = new long[stride * (Height + 1)];

            for (int y = 0; y < Height; y++)
            {
                long row = 0;

                for (int x = 0; x < Width; x++)
                {
                    row += Data[(y * Width) + x];
                    table[((y + 1) * stride) + x + 1] = table[(y * stride) + x + 1] + row;
                }
            }

            _integral = table;
            return table;
        }
    }

    public long Sum(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, Width);
        y0 = Math.Clamp(y0, 0, Height);
        x1 = Math.Clamp(x1, 0, Width);
        y1 = Math.Clamp(y1, 0, Height);

        if (x1 <= x0 || y1 <= y0)
        {
            return 0;
        }

        var s = Integral;
        int stride = Width + 1;

        return s[(y1 * stride) + x1] - s[(y0 * stride) + x1] - s[(y1 * stride) + x0] + s[(y0 * stride) + x0];
    }

    public double Mean(int x0, int y0, int x1, int y1)
    {
        long area = (long)Math.Max(0, Math.Min(x1, Width) - Math.Max(x0, 0)) *
                    Math.Max(0, Math.Min(y1, Height) - Math.Max(y0, 0));

        return area == 0 ? 0 : (double)Sum(x0, y0, x1, y1) / area;
    }

    public GrayImage Clone() => new(Width, Height, (byte[])Data.Clone());

    public static GrayImage FromImage(Image32 image) => new(image.Width, image.Height, image.ToGrayBytes());

    /// <summary>
    /// Separable running-sum box blur. The integral-table version asked for a
    /// clamped mean per pixel and dominated the whole scan; row and column
    /// prefix sums do the same work in two linear passes.
    /// </summary>
    public GrayImage BoxBlur(int radius)
    {
        if (radius <= 0)
        {
            return Clone();
        }

        int width = Width;
        int height = Height;
        var horizontal = new byte[Data.Length];
        var prefix = new long[width + 1];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            prefix[0] = 0;

            for (int x = 0; x < width; x++)
            {
                prefix[x + 1] = prefix[x] + Data[row + x];
            }

            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Max(0, x - radius);
                int x1 = Math.Min(width, x + radius + 1);
                horizontal[row + x] = (byte)((prefix[x1] - prefix[x0]) / (x1 - x0));
            }
        }

        var result = new GrayImage(width, height);
        var column = new long[height + 1];

        for (int x = 0; x < width; x++)
        {
            column[0] = 0;

            for (int y = 0; y < height; y++)
            {
                column[y + 1] = column[y] + horizontal[(y * width) + x];
            }

            for (int y = 0; y < height; y++)
            {
                int y0 = Math.Max(0, y - radius);
                int y1 = Math.Min(height, y + radius + 1);
                result.Data[(y * width) + x] = (byte)((column[y1] - column[y0]) / (y1 - y0));
            }
        }

        return result;
    }

    /// <summary>Three box passes; a cheap Gaussian stand-in used before edge detection.</summary>
    public GrayImage Smooth(int radius = 1) => BoxBlur(radius).BoxBlur(radius).BoxBlur(radius);

    public GrayImage Median3()
    {
        var result = new GrayImage(Width, Height);
        Span<byte> window = stackalloc byte[9];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int n = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        window[n++] = At(x + dx, y + dy);
                    }
                }

                window.Sort();
                result.Data[(y * Width) + x] = window[4];
            }
        }

        return result;
    }

    /// <summary>Contrast stretch between the 1st and 99th intensity percentiles.</summary>
    public GrayImage Normalize()
    {
        var histogram = new int[256];

        foreach (byte value in Data)
        {
            histogram[value]++;
        }

        int lowCount = (int)(Data.Length * 0.01);
        int highCount = (int)(Data.Length * 0.99);
        int cumulative = 0;
        int low = 0;
        int high = 255;

        for (int i = 0; i < 256; i++)
        {
            cumulative += histogram[i];

            if (cumulative >= lowCount && lowCount > 0 && low == 0)
            {
                low = i;
            }

            if (cumulative >= highCount)
            {
                high = i;
                break;
            }
        }

        if (high <= low)
        {
            return Clone();
        }

        var result = new GrayImage(Width, Height);
        double span = high - low;

        for (int i = 0; i < Data.Length; i++)
        {
            result.Data[i] = (byte)Math.Clamp(((Data[i] - low) * 255.0 / span), 0, 255);
        }

        return result;
    }

    /// <summary>Unsharp mask: local contrast without changing global brightness.</summary>
    public GrayImage Unsharp(int radius = 2, double amount = 0.8)
    {
        var blurred = BoxBlur(radius);
        var result = new GrayImage(Width, Height);

        for (int i = 0; i < Data.Length; i++)
        {
            double value = Data[i] + ((Data[i] - blurred.Data[i]) * amount);
            result.Data[i] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }

        return result;
    }

    public int OtsuThreshold()
    {
        var histogram = new int[256];

        foreach (byte value in Data)
        {
            histogram[value]++;
        }

        long total = Data.Length;
        double sum = 0;

        for (int i = 0; i < 256; i++)
        {
            sum += i * histogram[i];
        }

        double sumBackground = 0;
        long weightBackground = 0;
        double bestVariance = -1;
        int best = 127;

        for (int t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];

            if (weightBackground == 0)
            {
                continue;
            }

            long weightForeground = total - weightBackground;

            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += (double)t * histogram[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sum - sumBackground) / weightForeground;
            double variance = (double)weightBackground * weightForeground *
                              (meanBackground - meanForeground) * (meanBackground - meanForeground);

            if (variance > bestVariance)
            {
                bestVariance = variance;
                best = t;
            }
        }

        return best;
    }

    /// <summary>255 where the pixel is on the requested side of the threshold.</summary>
    public GrayImage Binarize(int threshold, bool darkIsForeground = true)
    {
        var result = new GrayImage(Width, Height);

        for (int i = 0; i < Data.Length; i++)
        {
            bool on = darkIsForeground ? Data[i] <= threshold : Data[i] >= threshold;
            result.Data[i] = on ? (byte)255 : (byte)0;
        }

        return result;
    }

    public GrayImage Invert()
    {
        var result = new GrayImage(Width, Height);

        for (int i = 0; i < Data.Length; i++)
        {
            result.Data[i] = (byte)(255 - Data[i]);
        }

        return result;
    }

    /// <summary>
    /// Local mean threshold. Robust against gradient and dark/light themes,
    /// unlike a single global cutoff.
    /// </summary>
    public GrayImage AdaptiveThreshold(int radius = 8, int offset = 6)
    {
        var result = new GrayImage(Width, Height);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                double mean = Mean(x - radius, y - radius, x + radius + 1, y + radius + 1);
                bool on = Data[(y * Width) + x] < mean - offset;
                result.Data[(y * Width) + x] = on ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    public void SobelGradients(out short[] gx, out short[] gy)
    {
        gx = new short[Data.Length];
        gy = new short[Data.Length];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int tl = At(x - 1, y - 1);
                int t = At(x, y - 1);
                int tr = At(x + 1, y - 1);
                int l = At(x - 1, y);
                int r = At(x + 1, y);
                int bl = At(x - 1, y + 1);
                int b = At(x, y + 1);
                int br = At(x + 1, y + 1);

                int dx = (tr + (2 * r) + br) - (tl + (2 * l) + bl);
                int dy = (bl + (2 * b) + br) - (tl + (2 * t) + tr);
                int index = (y * Width) + x;
                gx[index] = (short)Math.Clamp(dx, short.MinValue, short.MaxValue);
                gy[index] = (short)Math.Clamp(dy, short.MinValue, short.MaxValue);
            }
        }
    }

    public GrayImage Sobel()
    {
        SobelGradients(out short[] gx, out short[] gy);
        var result = new GrayImage(Width, Height);

        for (int i = 0; i < gx.Length; i++)
        {
            result.Data[i] = (byte)Math.Min(255, Math.Abs(gx[i]) + Math.Abs(gy[i]));
        }

        return result;
    }

    /// <summary>
    /// Canny-style edges: smooth, gradient magnitude, non-maximum suppression
    /// along the gradient axis, then hysteresis so weak antialiased borders
    /// survive only when they touch a strong edge.
    /// </summary>
    public GrayImage Canny(int low = 30, int high = 80)
    {
        int width = Width;
        int height = Height;
        var smooth = BoxBlur(1);
        var gray = smooth.Data;
        var candidate = new GrayImage(width, height);
        var magnitude = new short[gray.Length];
        var gx = new short[gray.Length];
        var gy = new short[gray.Length];

        // Sobel with precomputed clamped neighbours: the generic accessor called
        // Math.Clamp eight times per pixel and dominated the pass.
        for (int y = 0; y < height; y++)
        {
            int rowAbove = Math.Max(0, y - 1) * width;
            int row = y * width;
            int rowBelow = Math.Min(height - 1, y + 1) * width;

            for (int x = 0; x < width; x++)
            {
                int left = x > 0 ? x - 1 : 0;
                int right = x < width - 1 ? x + 1 : width - 1;
                int i = row + x;

                int dx = (gray[rowAbove + right] + (2 * gray[row + right]) + gray[rowBelow + right]) -
                         (gray[rowAbove + left] + (2 * gray[row + left]) + gray[rowBelow + left]);
                int dy = (gray[rowBelow + left] + (2 * gray[rowBelow + x]) + gray[rowBelow + right]) -
                         (gray[rowAbove + left] + (2 * gray[rowAbove + x]) + gray[rowAbove + right]);

                gx[i] = (short)dx;
                gy[i] = (short)dy;
                magnitude[i] = (short)Math.Min(short.MaxValue, Math.Abs(dx) + Math.Abs(dy));
            }
        }

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int i = (y * width) + x;
                int ax = Math.Abs(gx[i]);
                int ay = Math.Abs(gy[i]);
                int before;
                int after;

                if (ax * 2 <= ay)
                {
                    before = magnitude[i - width];
                    after = magnitude[i + width];
                }
                else if (ay * 2 <= ax)
                {
                    before = magnitude[i - 1];
                    after = magnitude[i + 1];
                }
                else if (gx[i] * gy[i] > 0)
                {
                    before = magnitude[i - width - 1];
                    after = magnitude[i + width + 1];
                }
                else
                {
                    before = magnitude[i - width + 1];
                    after = magnitude[i + width - 1];
                }

                int value = magnitude[i];

                if (value >= before && value >= after)
                {
                    candidate.Data[i] = (byte)Math.Min(255, value);
                }
            }
        }

        return candidate.Hysteresis(low, high);
    }

    /// <summary>Keeps weak pixels connected to strong ones; everything else becomes background.</summary>
    public GrayImage Hysteresis(int low, int high)
    {
        int width = Width;
        int height = Height;
        var result = new GrayImage(width, height);
        var stack = new int[Data.Length];
        int top = 0;

        for (int i = 0; i < Data.Length; i++)
        {
            if (Data[i] >= high)
            {
                result.Data[i] = 255;
                stack[top++] = i;
            }
        }

        while (top > 0)
        {
            int index = stack[--top];
            int x = index % width;
            int y = index / width;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;

                if (ny < 0 || ny >= height)
                {
                    continue;
                }

                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;

                    if (nx < 0 || nx >= width)
                    {
                        continue;
                    }

                    int neighbour = (ny * width) + nx;

                    if (result.Data[neighbour] == 0 && Data[neighbour] >= low)
                    {
                        result.Data[neighbour] = 255;
                        stack[top++] = neighbour;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Square structuring element, applied separably.</summary>
    public GrayImage Dilate(int radius = 1)
    {
        if (radius <= 0)
        {
            return Clone();
        }

        var horizontal = new GrayImage(Width, Height);
        var result = new GrayImage(Width, Height);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte best = 0;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    best = Math.Max(best, At(x + dx, y));
                }

                horizontal.Data[(y * Width) + x] = best;
            }
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte best = 0;

                for (int dy = -radius; dy <= radius; dy++)
                {
                    best = Math.Max(best, horizontal.At(x, y + dy));
                }

                result.Data[(y * Width) + x] = best;
            }
        }

        return result;
    }

    public GrayImage Erode(int radius = 1)
    {
        if (radius <= 0)
        {
            return Clone();
        }

        return Invert().Dilate(radius).Invert();
    }

    /// <summary>Fraction of pixels that are edge pixels inside the area.</summary>
    public double Density(Rectangle area)
    {
        int x0 = Math.Max(0, area.Left);
        int y0 = Math.Max(0, area.Top);
        int x1 = Math.Min(Width, area.Right);
        int y1 = Math.Min(Height, area.Bottom);

        if (x1 <= x0 || y1 <= y0)
        {
            return 0;
        }

        return Sum(x0, y0, x1, y1) / (255.0 * (x1 - x0) * (y1 - y0));
    }
}
