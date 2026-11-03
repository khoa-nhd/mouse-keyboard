using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using KeyboardMouse.Native;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Base for all feedback windows. Layered (per-pixel alpha), click-through,
/// never activates, never appears in the taskbar or Alt+Tab. The pointer and
/// keyboard focus stay exactly where the user put them.
/// </summary>
public abstract class OverlayForm : Form
{
    private Bitmap? _surface;
    private IntPtr _screenDc;
    private IntPtr _memoryDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;

    protected OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        Text = string.Empty;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= User32.WS_EX_LAYERED |
                          User32.WS_EX_TRANSPARENT |
                          User32.WS_EX_TOOLWINDOW |
                          User32.WS_EX_NOACTIVATE |
                          User32.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected void RenderSurface(int width, int height, int x, int y, Action<Graphics> draw)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (!EnsureSurface(width, height) || _surface is null)
        {
            return;
        }

        using (var graphics = Graphics.FromImage(_surface))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            draw(graphics);
        }

        PushSurface(x, y);
    }

    public void RenderSolid(int width, int height, int x, int y, Color color) =>
        RenderSurface(width, height, x, y, g => g.Clear(color));

    public void ShowOverlay()
    {
        _ = Handle;
        User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);

        // Re-assert the top spot: another topmost window shown since the last
        // time (a tooltip, the taskbar) can otherwise cover part of the shape.
        User32.SetWindowPos(
            Handle,
            User32.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    public void HideOverlay()
    {
        if (IsHandleCreated)
        {
            User32.ShowWindow(Handle, User32.SW_HIDE);
        }
    }

    /// <summary>
    /// Reuses one top-down 32bpp DIB and one memory DC for the lifetime of the
    /// surface. Recreating and copying a 700x700 bitmap every frame was the
    /// main source of GDI churn and stutter in the animated comet overlay.
    /// </summary>
    private bool EnsureSurface(int width, int height)
    {
        if (_surface is not null && _surface.Width == width && _surface.Height == height)
        {
            return true;
        }

        ReleaseSurface();

        _screenDc = Gdi32.GetDC(IntPtr.Zero);
        if (_screenDc == IntPtr.Zero)
        {
            return false;
        }

        _memoryDc = Gdi32.CreateCompatibleDC(_screenDc);
        if (_memoryDc == IntPtr.Zero)
        {
            ReleaseSurface();
            return false;
        }

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down: row 0 is the top of the image
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Gdi32.BI_RGB,
                biSizeImage = (uint)(width * height * 4)
            }
        };

        _dib = Gdi32.CreateDIBSection(
            _screenDc,
            ref info,
            Gdi32.DIB_RGB_COLORS,
            out IntPtr bits,
            IntPtr.Zero,
            0);

        if (_dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            ReleaseSurface();
            return false;
        }

        _oldBitmap = Gdi32.SelectObject(_memoryDc, _dib);
        if (_oldBitmap == IntPtr.Zero)
        {
            ReleaseSurface();
            return false;
        }

        // Wrapping the DIB bits keeps GDI+ drawing directly into the bitmap
        // that UpdateLayeredWindow later composites: no per-frame HBitmap copy.
        _surface = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        return true;
    }

    private void ReleaseSurface()
    {
        _surface?.Dispose();
        _surface = null;

        if (_memoryDc != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
            {
                Gdi32.SelectObject(_memoryDc, _oldBitmap);
                _oldBitmap = IntPtr.Zero;
            }

            if (_dib != IntPtr.Zero)
            {
                Gdi32.DeleteObject(_dib);
                _dib = IntPtr.Zero;
            }

            Gdi32.DeleteDC(_memoryDc);
            _memoryDc = IntPtr.Zero;
        }

        if (_screenDc != IntPtr.Zero)
        {
            Gdi32.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }
    }

    private void PushSurface(int x, int y)
    {
        if (_surface is null || _memoryDc == IntPtr.Zero || _screenDc == IntPtr.Zero)
        {
            return;
        }

        _ = Handle;

        var destination = new POINT(x, y);
        var source = new POINT(0, 0);
        var size = new SIZE(_surface.Width, _surface.Height);
        var blend = new BLENDFUNCTION
        {
            BlendOp = Gdi32.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = Gdi32.AC_SRC_ALPHA
        };

        if (!User32.UpdateLayeredWindow(
                Handle,
                _screenDc,
                ref destination,
                ref size,
                _memoryDc,
                ref source,
                0,
                ref blend,
                User32.ULW_ALPHA))
        {
            Debug.WriteLine($"[Overlay] UpdateLayeredWindow failed: {Marshal.GetLastWin32Error()}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseSurface();
        }

        base.Dispose(disposing);
    }
}
