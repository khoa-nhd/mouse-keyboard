using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using KeyboardMouse.Core;
using KeyboardMouse.Native;

namespace KeyboardMouse.Core;

/// <summary>
/// Default implementation of <see cref="IScreenCaptureService"/> using GDI+
/// BitBlt.  Captures are always in the canonical physical-pixel space.
/// </summary>
internal sealed class ScreenCaptureService : IScreenCaptureService
{
    private readonly object _disposeGate = new();
    private bool _disposed;

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
    }

    public CaptureResult CaptureForegroundWindow(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IntPtr hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("No foreground window available for capture.");
        }

        RECT rect = default;
        if (!User32.GetWindowRect(hwnd, out rect))
        {
            throw new InvalidOperationException("Could not retrieve the foreground window rectangle.");
        }

        Rectangle desktopRegion = ToRectangle(rect);

        // The screen DC is required: the source coordinates below are desktop
        // coordinates, while a window DC origin is the window's client area.
        var desktopDc = User32.GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not obtain the desktop device context.");
        }

        try
        {
            IntPtr srcDc = Gdi32.CreateCompatibleDC(desktopDc);
            if (srcDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create a compatible device context.");
            }

            try
            {
                int width = desktopRegion.Width;
                int height = desktopRegion.Height;

                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = Gdi32.DIB_RGB_COLORS
                    }
                };

                IntPtr hBitmap = Gdi32.CreateDIBSection(
                    desktopDc,
                    ref bmi,
                    Gdi32.DIB_RGB_COLORS,
                    out IntPtr ppvBits,
                    IntPtr.Zero,
                    0);

                if (hBitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create a DIB section for the capture.");
                }

                try
                {
                    IntPtr oldObj = Gdi32.SelectObject(srcDc, hBitmap);
                    if (oldObj == IntPtr.Zero)
                    {
                        Gdi32.DeleteObject(hBitmap);
                        throw new InvalidOperationException("Could not select the capture bitmap into the DC.");
                    }

                    try
                    {
                        bool success = Gdi32.BitBlt(
                            srcDc,
                            0, 0,
                            width, height,
                            desktopDc,
                            desktopRegion.Left, desktopRegion.Top,
                            Gdi32.SRCCOPY);

                        if (!success)
                        {
                            throw new InvalidOperationException(
                                $"BitBlt failed with error code {Marshal.GetLastWin32Error()}.");
                        }

                        Bitmap bitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, ppvBits);

                        Bitmap snapshot = new Bitmap(bitmap);
                        bitmap.Dispose();

                        return new CaptureResult
                        {
                            Bitmap = snapshot,
                            Region = TargetBounds.FromRectangle(desktopRegion),
                            DpiScale = 1.0,
                            WindowHandle = hwnd
                        };
                    }
                    finally
                    {
                        Gdi32.SelectObject(srcDc, oldObj);
                    }
                }
                finally
                {
                    Gdi32.DeleteObject(hBitmap);
                }
            }
            finally
            {
                Gdi32.DeleteDC(srcDc);
            }
        }
        finally
        {
            User32.ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    public CaptureResult CaptureMonitor(MonitorInfo monitor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Rectangle desktopRegion = monitor.Bounds;

        var desktopDc = User32.GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not obtain the desktop device context.");
        }

        try
        {
            IntPtr srcDc = Gdi32.CreateCompatibleDC(desktopDc);
            if (srcDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create a compatible device context.");
            }

            try
            {
                int width = desktopRegion.Width;
                int height = desktopRegion.Height;

                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = Gdi32.DIB_RGB_COLORS
                    }
                };

                IntPtr hBitmap = Gdi32.CreateDIBSection(
                    desktopDc,
                    ref bmi,
                    Gdi32.DIB_RGB_COLORS,
                    out IntPtr ppvBits,
                    IntPtr.Zero,
                    0);

                if (hBitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create a DIB section for the capture.");
                }

                try
                {
                    IntPtr oldObj = Gdi32.SelectObject(srcDc, hBitmap);
                    if (oldObj == IntPtr.Zero)
                    {
                        Gdi32.DeleteObject(hBitmap);
                        throw new InvalidOperationException("Could not select the capture bitmap into the DC.");
                    }

                    try
                    {
                        bool success = Gdi32.BitBlt(
                            srcDc,
                            0, 0,
                            width, height,
                            desktopDc,
                            desktopRegion.Left, desktopRegion.Top,
                            Gdi32.SRCCOPY);

                        if (!success)
                        {
                            throw new InvalidOperationException(
                                $"BitBlt failed with error code {Marshal.GetLastWin32Error()}.");
                        }

                        Bitmap bitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, ppvBits);
                        Bitmap snapshot = new Bitmap(bitmap);
                        bitmap.Dispose();

                        return new CaptureResult
                        {
                            Bitmap = snapshot,
                            Region = TargetBounds.FromRectangle(desktopRegion),
                            // BitBlt captured native physical pixels; monitor.Scale
                            // describes DPI, not a resampling factor applied here.
                            DpiScale = 1.0,
                            WindowHandle = null,
                            MonitorHandle = monitor.Handle
                        };
                    }
                    finally
                    {
                        Gdi32.SelectObject(srcDc, oldObj);
                    }
                }
                finally
                {
                    Gdi32.DeleteObject(hBitmap);
                }
            }
            finally
            {
                Gdi32.DeleteDC(srcDc);
            }
        }
        finally
        {
            User32.ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    public CaptureResult CaptureRegion(TargetBounds region, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Rectangle virtualScreen = SystemInformation.VirtualScreen;
        Rectangle clipped = Rectangle.Intersect(
            new Rectangle(region.Left, region.Top, region.Width, region.Height),
            new Rectangle(virtualScreen.X, virtualScreen.Y, virtualScreen.Width, virtualScreen.Height));

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                "The requested capture region does not intersect the virtual desktop.");
        }

        Rectangle desktopRegion = new Rectangle(
            clipped.X - virtualScreen.X,
            clipped.Y - virtualScreen.Y,
            clipped.Width,
            clipped.Height);

        desktopRegion.Offset(virtualScreen.X, virtualScreen.Y);

        var desktopDc = User32.GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not obtain the desktop device context.");
        }

        try
        {
            IntPtr srcDc = Gdi32.CreateCompatibleDC(desktopDc);
            if (srcDc == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create a compatible device context.");
            }

            try
            {
                int width = desktopRegion.Width;
                int height = desktopRegion.Height;

                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = Gdi32.DIB_RGB_COLORS
                    }
                };

                IntPtr hBitmap = Gdi32.CreateDIBSection(
                    desktopDc,
                    ref bmi,
                    Gdi32.DIB_RGB_COLORS,
                    out IntPtr ppvBits,
                    IntPtr.Zero,
                    0);

                if (hBitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create a DIB section for the capture.");
                }

                try
                {
                    IntPtr oldObj = Gdi32.SelectObject(srcDc, hBitmap);
                    if (oldObj == IntPtr.Zero)
                    {
                        Gdi32.DeleteObject(hBitmap);
                        throw new InvalidOperationException("Could not select the capture bitmap into the DC.");
                    }

                    try
                    {
                        bool success = Gdi32.BitBlt(
                            srcDc,
                            0, 0,
                            width, height,
                            desktopDc,
                            desktopRegion.Left, desktopRegion.Top,
                            Gdi32.SRCCOPY);

                        if (!success)
                        {
                            throw new InvalidOperationException(
                                $"BitBlt failed with error code {Marshal.GetLastWin32Error()}.");
                        }

                        Bitmap bitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, ppvBits);
                        Bitmap snapshot = new Bitmap(bitmap);
                        bitmap.Dispose();

                        return new CaptureResult
                        {
                            Bitmap = snapshot,
                            Region = TargetBounds.FromRectangle(desktopRegion),
                            DpiScale = 1.0,
                            WindowHandle = null
                        };
                    }
                    finally
                    {
                        Gdi32.SelectObject(srcDc, oldObj);
                    }
                }
                finally
                {
                    Gdi32.DeleteObject(hBitmap);
                }
            }
            finally
            {
                Gdi32.DeleteDC(srcDc);
            }
        }
        finally
        {
            User32.ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    private static Rectangle ToRectangle(RECT rect) => new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
}