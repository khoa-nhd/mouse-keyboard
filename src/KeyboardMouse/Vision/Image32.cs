using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KeyboardMouse.Vision;

/// <summary>
/// A plain BGRA pixel buffer. The vision layer deliberately works on arrays
/// instead of <see cref="Bitmap"/> handles so detection can be unit tested with
/// synthetic images and so resizing does not depend on GDI+ interpolation.
/// </summary>
public sealed class Image32
{
    public Image32(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public Image32(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException("Pixel buffer must hold width*height*4 bytes.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte Blue(int x, int y) => Pixels[((y * Width) + x) * 4];

    public byte Green(int x, int y) => Pixels[(((y * Width) + x) * 4) + 1];

    public byte Red(int x, int y) => Pixels[(((y * Width) + x) * 4) + 2];

    public void Set(int x, int y, byte r, byte g, byte b)
    {
        int i = ((y * Width) + x) * 4;
        Pixels[i] = b;
        Pixels[i + 1] = g;
        Pixels[i + 2] = r;
        Pixels[i + 3] = 255;
    }

    /// <summary>Rec.601 luma, the same weights the grayscale pass uses.</summary>
    public int Luma(int x, int y)
    {
        int i = ((y * Width) + x) * 4;
        return ((Pixels[i] * 29) + (Pixels[i + 1] * 150) + (Pixels[i + 2] * 77)) >> 8;
    }

    public static Image32 FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var image = new Image32(bitmap.Width, bitmap.Height);
            int bytes = bitmap.Width * 4;

            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), image.Pixels, y * bytes, bytes);
            }

            return image;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public Bitmap ToBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, Width, Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int bytes = Width * 4;

            for (int y = 0; y < Height; y++)
            {
                Marshal.Copy(Pixels, y * bytes, IntPtr.Add(data.Scan0, y * data.Stride), bytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    public Image32 Crop(Rectangle area)
    {
        var clipped = Rectangle.Intersect(area, new Rectangle(0, 0, Width, Height));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new ArgumentException("Crop area does not overlap the image.", nameof(area));
        }

        var crop = new Image32(clipped.Width, clipped.Height);

        for (int y = 0; y < clipped.Height; y++)
        {
            int source = (((clipped.Top + y) * Width) + clipped.Left) * 4;
            Buffer.BlockCopy(Pixels, source, crop.Pixels, y * clipped.Width * 4, clipped.Width * 4);
        }

        return crop;
    }

    public byte[] ToGrayBytes()
    {
        var gray = new byte[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                gray[row + x] = (byte)Luma(x, y);
            }
        }

        return gray;
    }

    /// <summary>
    /// Area-averaging when shrinking (keeps thin strokes from aliasing away)
    /// and bilinear when growing.
    /// </summary>
    public Image32 Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");
        }

        if (width == Width && height == Height)
        {
            return this;
        }

        var target = new Image32(width, height);
        double scaleX = (double)Width / width;
        double scaleY = (double)Height / height;

        if (scaleX >= 1 && scaleY >= 1)
        {
            for (int y = 0; y < height; y++)
            {
                int y0 = (int)(y * scaleY);
                int y1 = Math.Min(Height, Math.Max(y0 + 1, (int)((y + 1) * scaleY)));

                for (int x = 0; x < width; x++)
                {
                    int x0 = (int)(x * scaleX);
                    int x1 = Math.Min(Width, Math.Max(x0 + 1, (int)((x + 1) * scaleX)));

                    long b = 0, g = 0, r = 0;
                    int count = 0;

                    for (int sy = y0; sy < y1; sy++)
                    {
                        for (int sx = x0; sx < x1; sx++)
                        {
                            int i = ((sy * Width) + sx) * 4;
                            b += Pixels[i];
                            g += Pixels[i + 1];
                            r += Pixels[i + 2];
                            count++;
                        }
                    }

                    target.Set(x, y, (byte)(r / count), (byte)(g / count), (byte)(b / count));
                }
            }

            return target;
        }

        for (int y = 0; y < height; y++)
        {
            double sy = ((y + 0.5) * scaleY) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, Height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, Height - 1);
            double fy = Math.Clamp(sy - y0, 0, 1);

            for (int x = 0; x < width; x++)
            {
                double sx = ((x + 0.5) * scaleX) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, Width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, Width - 1);
                double fx = Math.Clamp(sx - x0, 0, 1);

                int i00 = ((y0 * Width) + x0) * 4;
                int i01 = ((y0 * Width) + x1) * 4;
                int i10 = ((y1 * Width) + x0) * 4;
                int i11 = ((y1 * Width) + x1) * 4;

                double w00 = (1 - fx) * (1 - fy);
                double w01 = fx * (1 - fy);
                double w10 = (1 - fx) * fy;
                double w11 = fx * fy;

                double b = (Pixels[i00] * w00) + (Pixels[i01] * w01) + (Pixels[i10] * w10) + (Pixels[i11] * w11);
                double g = (Pixels[i00 + 1] * w00) + (Pixels[i01 + 1] * w01) + (Pixels[i10 + 1] * w10) + (Pixels[i11 + 1] * w11);
                double r = (Pixels[i00 + 2] * w00) + (Pixels[i01 + 2] * w01) + (Pixels[i10 + 2] * w10) + (Pixels[i11 + 2] * w11);

                target.Set(x, y, (byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
            }
        }

        return target;
    }
}
