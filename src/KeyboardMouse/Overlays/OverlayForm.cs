using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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

        if (_surface is null || _surface.Width != width || _surface.Height != height)
        {
            _surface?.Dispose();
            _surface = new Bitmap(width, height, PixelFormat.Format32bppArgb);
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
    }

    public void HideOverlay()
    {
        if (IsHandleCreated)
        {
            User32.ShowWindow(Handle, User32.SW_HIDE);
        }
    }

    private void PushSurface(int x, int y)
    {
        if (_surface is null)
        {
            return;
        }

        _ = Handle;

        IntPtr screenDc = Gdi32.GetDC(IntPtr.Zero);
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            memoryDc = Gdi32.CreateCompatibleDC(screenDc);
            hBitmap = _surface.GetHbitmap(Color.FromArgb(0));
            oldBitmap = Gdi32.SelectObject(memoryDc, hBitmap);

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
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    User32.ULW_ALPHA))
            {
                Debug.WriteLine($"[Overlay] UpdateLayeredWindow failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                Gdi32.SelectObject(memoryDc, oldBitmap);
            }

            if (hBitmap != IntPtr.Zero)
            {
                Gdi32.DeleteObject(hBitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                Gdi32.DeleteDC(memoryDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                Gdi32.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surface?.Dispose();
            _surface = null;
        }

        base.Dispose(disposing);
    }
}
