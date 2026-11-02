using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Frame around each monitor while mouse mode is on. Built from thin layered
/// strips rather than a full-screen bitmap, so a 4K display costs a few hundred
/// kilobytes instead of tens of megabytes. Optional via
/// settings.feedback.screenBorder; style, colour and thickness come from the
/// same section.
/// </summary>
public sealed class ScreenBorder : IDisposable
{
    private readonly List<BorderStrip> _strips = new();

    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private ScreenBorderStyle _style = ScreenBorderStyle.Frame;
    private Color _color = CursorColors.ToColor(CursorColors.DefaultHex);
    private int _thickness = 3;
    private bool _visible;
    private bool _built;

    /// <summary>Rebuilds the strips when the style, colour or thickness changes.</summary>
    public void Configure(ScreenBorderStyle style, Color color, int thickness)
    {
        int t = Math.Clamp(thickness, 2, 12);
        if (_built && _style == style && _color == color && _thickness == t)
        {
            return;
        }

        _style = style;
        _color = color;
        _thickness = t;
        Rebuild();
    }

    public void Refresh(Monitors monitors) => Refresh(monitors.All);

    public void Refresh(IReadOnlyList<MonitorInfo> monitors)
    {
        _monitors = monitors;
        Rebuild();
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        foreach (var strip in _strips)
        {
            if (visible)
            {
                strip.ShowOverlay();
            }
            else
            {
                strip.HideOverlay();
            }
        }
    }

    private void Rebuild()
    {
        foreach (var strip in _strips)
        {
            strip.Dispose();
        }

        _strips.Clear();
        _built = true;

        foreach (var monitor in _monitors)
        {
            var b = monitor.Bounds;
            if (b.Width <= 4 || b.Height <= 4)
            {
                continue;
            }

            switch (_style)
            {
                case ScreenBorderStyle.Glow:
                    BuildGlow(b);
                    break;
                case ScreenBorderStyle.Corners:
                    BuildCorners(b);
                    break;
                case ScreenBorderStyle.None:
                    // Deliberately no strips: the style is off, not hidden.
                    continue;
                default:
                    BuildFrame(b);
                    break;
            }
        }

        if (_visible)
        {
            foreach (var strip in _strips)
            {
                strip.ShowOverlay();
            }
        }
    }

    /// <summary>
    /// Solid frame. The side strips run the full height so every corner is
    /// drawn twice: a strip that gets covered by another topmost window (the
    /// taskbar, a tooltip) can no longer punch a hole in the frame.
    /// </summary>
    private void BuildFrame(Rectangle b)
    {
        int t = _thickness;

        Add(b.Left, b.Top, b.Width, t, (g, w, h) => PaintFrame(g, w, h, BorderEdge.Top));
        Add(b.Left, b.Bottom - t, b.Width, t, (g, w, h) => PaintFrame(g, w, h, BorderEdge.Bottom));
        Add(b.Left, b.Top, t, b.Height, (g, w, h) => PaintFrame(g, w, h, BorderEdge.Left));
        Add(b.Right - t, b.Top, t, b.Height, (g, w, h) => PaintFrame(g, w, h, BorderEdge.Right));
    }

    private void BuildGlow(Rectangle b)
    {
        int band = Math.Max(8, _thickness * 4);

        // The sides run the full height: stopping them short of the corners
        // left the glow with a visible gap where the two bands meet.
        Add(b.Left, b.Top, b.Width, band, (g, w, h) => PaintGlow(g, w, h, BorderEdge.Top));
        Add(b.Left, b.Bottom - band, b.Width, band, (g, w, h) => PaintGlow(g, w, h, BorderEdge.Bottom));
        Add(b.Left, b.Top, band, b.Height, (g, w, h) => PaintGlow(g, w, h, BorderEdge.Left));
        Add(b.Right - band, b.Top, band, b.Height, (g, w, h) => PaintGlow(g, w, h, BorderEdge.Right));
    }

    private void BuildCorners(Rectangle b)
    {
        int arm = Math.Clamp(_thickness * 7, 20, Math.Max(20, Math.Min(b.Width, b.Height) / 3));

        Add(b.Left, b.Top, arm, arm, (g, w, h) => PaintCorner(g, w, h, BorderCorner.TopLeft));
        Add(b.Right - arm, b.Top, arm, arm, (g, w, h) => PaintCorner(g, w, h, BorderCorner.TopRight));
        Add(b.Left, b.Bottom - arm, arm, arm, (g, w, h) => PaintCorner(g, w, h, BorderCorner.BottomLeft));
        Add(b.Right - arm, b.Bottom - arm, arm, arm, (g, w, h) => PaintCorner(g, w, h, BorderCorner.BottomRight));
    }

    private void Add(int x, int y, int width, int height, Action<Graphics, int, int> paint)
    {
        if (width < 1 || height < 1)
        {
            return;
        }

        var strip = new BorderStrip();
        strip.Render(width, height, x, y, (g) => paint(g, width, height));
        _strips.Add(strip);
    }

    private void PaintFrame(Graphics g, int w, int h, BorderEdge edge)
    {
        using (var solid = new SolidBrush(_color))
        {
            g.FillRectangle(solid, 0, 0, w, h);
        }

        // A single lighter pixel on the inside edge keeps the line crisp
        // against dark and light wallpapers alike.
        using var highlight = new SolidBrush(Lighten(_color, 0.45f));
        switch (edge)
        {
            case BorderEdge.Top:
                g.FillRectangle(highlight, 0, h - 1, w, 1);
                break;
            case BorderEdge.Bottom:
                g.FillRectangle(highlight, 0, 0, w, 1);
                break;
            case BorderEdge.Left:
                g.FillRectangle(highlight, w - 1, 0, 1, h);
                break;
            default:
                g.FillRectangle(highlight, 0, 0, 1, h);
                break;
        }
    }

    private void PaintGlow(Graphics g, int w, int h, BorderEdge edge)
    {
        var rect = new Rectangle(0, 0, w, h);
        float angle = edge switch
        {
            BorderEdge.Top => 90f,
            BorderEdge.Bottom => 270f,
            BorderEdge.Left => 0f,
            _ => 180f
        };

        using var gradient = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, angle)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(160, _color),
                    Color.FromArgb(96, _color),
                    Color.FromArgb(32, _color),
                    Color.FromArgb(0, _color)
                },
                Positions = new[] { 0f, 0.22f, 0.6f, 1f }
            }
        };

        g.FillRectangle(gradient, rect);
    }

    private void PaintCorner(Graphics g, int w, int h, BorderCorner corner)
    {
        float half = _thickness / 2f;
        using var pen = new Pen(_color, _thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        float left = half;
        float top = half;
        float right = w - half;
        float bottom = h - half;

        switch (corner)
        {
            case BorderCorner.TopLeft:
                g.DrawLine(pen, left, top, right, top);
                g.DrawLine(pen, left, top, left, bottom);
                break;
            case BorderCorner.TopRight:
                g.DrawLine(pen, left, top, right, top);
                g.DrawLine(pen, right, top, right, bottom);
                break;
            case BorderCorner.BottomLeft:
                g.DrawLine(pen, left, bottom, right, bottom);
                g.DrawLine(pen, left, top, left, bottom);
                break;
            default:
                g.DrawLine(pen, left, bottom, right, bottom);
                g.DrawLine(pen, right, top, right, bottom);
                break;
        }
    }

    private static Color Lighten(Color color, float amount) =>
        Color.FromArgb(
            color.A,
            (int)Math.Round(color.R + ((255 - color.R) * amount)),
            (int)Math.Round(color.G + ((255 - color.G) * amount)),
            (int)Math.Round(color.B + ((255 - color.B) * amount)));

    public void Dispose()
    {
        foreach (var strip in _strips)
        {
            strip.Dispose();
        }

        _strips.Clear();
    }

    private enum BorderEdge
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private enum BorderCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private sealed class BorderStrip : OverlayForm
    {
        public void Render(int width, int height, int x, int y, Action<Graphics> paint) =>
            RenderSurface(width, height, x, y, paint);
    }
}
