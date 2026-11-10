using System.Drawing;
using System.Drawing.Imaging;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Vision;

/// <summary>
/// Copies a screen rectangle into a pixel buffer. Failures are non-fatal: the
/// UIA scan still produces hints when capture is unavailable (locked session,
/// protected surface, DWM composition change).
/// </summary>
public static class ScreenCapture
{
    public const long MaxPixels = 16_000_000;

    public static Image32? Capture(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        if ((long)area.Width * area.Height > MaxPixels)
        {
            return null;
        }

        try
        {
            using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            }

            return Image32.FromBitmap(bitmap);
        }
        catch (Exception ex)
        {
            Log.Error("Screen capture for visual detection failed", ex);
            return null;
        }
    }
}
