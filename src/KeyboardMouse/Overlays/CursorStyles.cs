using System.Drawing.Drawing2D;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Pointer indicator designs. Every style shows the same thing (the active
/// layer through its colour), but they suit different tastes: a small dot, a
/// presenter-style spotlight, a tinted arrow, a friendly buddy, and so on.
/// Chosen with settings.feedback.cursorStyle.
/// </summary>
public enum CursorStyle
{
    Dot,
    Halo,
    Spotlight,
    Reticle,
    Arrow,
    Comet,
    Orbit,
    Buddy
}

/// <summary>Parses and names <see cref="CursorStyle"/> values for the settings file and the UI.</summary>
public static class CursorStyleNames
{
    private static readonly (CursorStyle Style, string Id, string Name)[] Entries =
    {
        (CursorStyle.Dot, "dot", "Classic dot"),
        (CursorStyle.Halo, "halo", "Halo ring"),
        (CursorStyle.Spotlight, "spotlight", "Spotlight"),
        (CursorStyle.Reticle, "reticle", "Reticle"),
        (CursorStyle.Arrow, "arrow", "Arrow"),
        (CursorStyle.Comet, "comet", "Comet trail"),
        (CursorStyle.Orbit, "orbit", "Orbit"),
        (CursorStyle.Buddy, "buddy", "Buddy")
    };

    public static IReadOnlyList<CursorStyle> All { get; } = Entries.Select(e => e.Style).ToArray();

    public static string ToId(CursorStyle style)
    {
        foreach (var entry in Entries)
        {
            if (entry.Style == style)
            {
                return entry.Id;
            }
        }

        return "dot";
    }

    public static string DisplayName(CursorStyle style)
    {
        foreach (var entry in Entries)
        {
            if (entry.Style == style)
            {
                return entry.Name;
            }
        }

        return "Classic dot";
    }

    public static bool TryParse(string? value, out CursorStyle style)
    {
        style = CursorStyle.Dot;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                style = entry.Style;
                return true;
            }
        }

        return false;
    }
}

/// <summary>Where the indicator window sits relative to the pointer, and how big it is.</summary>
public readonly record struct CursorVisual(int OffsetX, int OffsetY, int Width, int Height);

/// <summary>
/// Paints the pointer indicator. Pure drawing: no state, no timers, so the
/// same code serves both the live overlay and the previews in the guide.
/// </summary>
public static class CursorPainter
{
    private static readonly Color Ink = Color.FromArgb(255, 24, 26, 32);

    /// <summary>Styles with motion need a redraw every tick even when the pointer rests.</summary>
    public static bool IsAnimated(CursorStyle style) =>
        style is CursorStyle.Spotlight or CursorStyle.Orbit or CursorStyle.Buddy;

    public static CursorVisual Measure(CursorStyle style, int size)
    {
        int s = Math.Clamp(size, 4, 48);

        switch (style)
        {
            case CursorStyle.Dot:
                return new CursorVisual(0, 0, s + 20, s + 20);

            case CursorStyle.Halo:
            {
                int half = (int)MathF.Ceiling((s * 0.9f) + 6f) + 8;
                return new CursorVisual(-half, -half, half * 2, half * 2);
            }

            case CursorStyle.Spotlight:
            {
                int half = (int)MathF.Ceiling((s * 3f) + 10f) + 6;
                return new CursorVisual(-half, -half, half * 2, half * 2);
            }

            case CursorStyle.Reticle:
            {
                int half = (int)MathF.Ceiling((s * 1.1f) + 6f) + 10;
                return new CursorVisual(-half, -half, half * 2, half * 2);
            }

            case CursorStyle.Arrow:
            {
                int w = (int)MathF.Ceiling(s * 1.5f) + 24;
                int h = (int)MathF.Ceiling(s * 2.2f) + 20;
                return new CursorVisual(0, 0, w, h);
            }

            case CursorStyle.Comet:
            {
                int half = (int)MathF.Ceiling((s * 2.6f) + 12f);
                return new CursorVisual(16 - half, 16 - half, half * 2, half * 2);
            }

            case CursorStyle.Orbit:
            {
                int half = (int)MathF.Ceiling((s * 1.1f) + 9f) + 8;
                int anchor = 16 + (s / 2);
                return new CursorVisual(anchor - half, anchor - half, half * 2, half * 2);
            }

            default:
            {
                int w = (int)MathF.Ceiling(s * 1.75f) + 26;
                int h = (int)MathF.Ceiling(s * 1.4f) + 26;
                return new CursorVisual(0, 0, w, h);
            }
        }
    }

    /// <summary>
    /// Draws the style onto a surface whose top-left is the window origin;
    /// <paramref name="pointer"/> is the cursor point in those local
    /// coordinates, <paramref name="trail"/> recent pointer samples (local too,
    /// oldest first), <paramref name="phase"/> seconds-ish for animation.
    /// </summary>
    public static void Paint(
        Graphics g,
        CursorStyle style,
        int size,
        Color color,
        float phase,
        PointF pointer,
        IReadOnlyList<PointF>? trail)
    {
        int s = Math.Clamp(size, 4, 48);

        switch (style)
        {
            case CursorStyle.Dot:
                PaintDot(g, pointer, s, color);
                break;
            case CursorStyle.Halo:
                PaintHalo(g, pointer, s, color, phase);
                break;
            case CursorStyle.Spotlight:
                PaintSpotlight(g, pointer, s, color, phase);
                break;
            case CursorStyle.Reticle:
                PaintReticle(g, pointer, s, color);
                break;
            case CursorStyle.Arrow:
                PaintArrow(g, pointer, s, color);
                break;
            case CursorStyle.Comet:
                PaintComet(g, pointer, s, color, trail);
                break;
            case CursorStyle.Orbit:
                PaintOrbit(g, pointer, s, color, phase);
                break;
            default:
                PaintBuddy(g, pointer, s, color, phase);
                break;
        }
    }

    private static void PaintDot(Graphics g, PointF pointer, int s, Color color)
    {
        var rect = new RectangleF(pointer.X + 16, pointer.Y + 16, s, s);

        using var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
        g.FillEllipse(shadow, rect.X + 1.5f, rect.Y + 1.5f, rect.Width, rect.Height);

        using var fill = new SolidBrush(color);
        g.FillEllipse(fill, rect);

        using var outline = new Pen(Color.FromArgb(235, 255, 255, 255), 1.2f);
        g.DrawEllipse(outline, rect);
    }

    private static void PaintHalo(Graphics g, PointF p, int s, Color color, float phase)
    {
        float r = (s * 0.9f) + 6f;
        var ring = new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);

        // Soft outer glow, then the crisp ring: a pointer highlighter look.
        using (var glow = new Pen(Color.FromArgb(28, color), MathF.Max(6f, s * 0.55f)))
        {
            g.DrawEllipse(glow, ring);
        }

        using (var soft = new Pen(Color.FromArgb(70, color), MathF.Max(3f, s * 0.22f)))
        {
            g.DrawEllipse(soft, ring);
        }

        using (var core = new Pen(color, MathF.Max(1.8f, s * 0.1f)))
        {
            g.DrawEllipse(core, ring);
        }

        // A slowly travelling highlight keeps the ring from looking printed on.
        float sweep = (phase * 140f) % 360f;
        using var shine = new Pen(Color.FromArgb(215, 255, 255, 255), MathF.Max(1.6f, s * 0.08f));
        g.DrawArc(shine, ring, sweep, 62f);

        using var inner = new SolidBrush(Color.FromArgb(200, color));
        g.FillEllipse(inner, p.X - 1.6f, p.Y - 1.6f, 3.2f, 3.2f);
    }

    private static void PaintSpotlight(Graphics g, PointF p, int s, Color color, float phase)
    {
        float breathe = 1f + (MathF.Sin(phase * 1.5f) * 0.035f);
        float r = ((s * 3f) + 10f) * breathe;
        var area = new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);

        using var path = new GraphicsPath();
        path.AddEllipse(area);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(165, color),
            SurroundColors = new[] { Color.FromArgb(0, color) },
            CenterPoint = p
        };
        g.FillEllipse(brush, area);

        using var edge = new Pen(Color.FromArgb(130, color), 1.5f);
        g.DrawEllipse(edge, area);

        using var core = new SolidBrush(Color.FromArgb(220, color));
        g.FillEllipse(core, p.X - 2.5f, p.Y - 2.5f, 5f, 5f);
    }

    private static void PaintReticle(Graphics g, PointF p, int s, Color color)
    {
        float r = (s * 1.1f) + 6f;
        float inner = r * 0.34f;

        using (var faint = new Pen(Color.FromArgb(70, color), 1f))
        {
            g.DrawEllipse(faint, p.X - r, p.Y - r, r * 2, r * 2);
        }

        // Four white-backed arms so the reticle reads on any wallpaper.
        using var halo = new Pen(Color.FromArgb(190, 255, 255, 255), 4.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var arm = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var directions = new (float dx, float dy)[]
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        foreach (var (dx, dy) in directions)
        {
            var a = new PointF(p.X + (dx * inner), p.Y + (dy * inner));
            var b = new PointF(p.X + (dx * r), p.Y + (dy * r));
            g.DrawLine(halo, a, b);
            g.DrawLine(arm, a, b);
        }

        // Hollow centre: never cover the pixel the pointer is aiming at.
        using var centreRing = new Pen(color, 1.8f);
        g.DrawEllipse(centreRing, p.X - inner * 0.55f, p.Y - inner * 0.55f, inner * 1.1f, inner * 1.1f);
    }

    private static void PaintArrow(Graphics g, PointF p, int s, Color color)
    {
        float scale = (s * 2.2f) / 16f;
        float tipX = p.X + 14f;
        float tipY = p.Y + 10f;

        var points = new[]
        {
            new PointF(tipX, tipY),
            new PointF(tipX, tipY + (14.6f * scale)),
            new PointF(tipX + (3.6f * scale), tipY + (11.2f * scale)),
            new PointF(tipX + (5.9f * scale), tipY + (15.9f * scale)),
            new PointF(tipX + (8.1f * scale), tipY + (14.8f * scale)),
            new PointF(tipX + (5.8f * scale), tipY + (10.2f * scale)),
            new PointF(tipX + (10.3f * scale), tipY + (10.2f * scale))
        };

        using var path = new GraphicsPath();
        path.AddPolygon(points);

        using (var shadowBrush = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
        using (var shadowPath = (GraphicsPath)path.Clone())
        {
            using var shift = new Matrix();
            shift.Translate(1.6f, 2.2f);
            shadowPath.Transform(shift);
            g.FillPath(shadowBrush, shadowPath);
        }

        using (var fill = new SolidBrush(color))
        {
            g.FillPath(fill, path);
        }

        using (var outline = new Pen(Color.FromArgb(240, 255, 255, 255), 1.7f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(outline, path);
        }
    }

    private static void PaintComet(Graphics g, PointF p, int s, Color color, IReadOnlyList<PointF>? trail)
    {
        const float HeadInset = 16f;
        var head = new PointF(p.X + HeadInset, p.Y + HeadInset);
        float reach = (s * 2.6f) + 12f;

        if (trail is { Count: > 1 })
        {
            for (int i = 0; i < trail.Count; i++)
            {
                var sample = trail[i];
                float dx = head.X - sample.X;
                float dy = head.Y - sample.Y;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance < 1f || distance > reach)
                {
                    continue;
                }

                float t = 1f - (distance / reach);
                float radius = (s * 0.14f) + (s * 0.42f * t);
                int alpha = (int)(120f * t * t);

                using var dot = new SolidBrush(Color.FromArgb(alpha, color));
                g.FillEllipse(dot, sample.X - radius, sample.Y - radius, radius * 2, radius * 2);
            }
        }

        using (var glow = new SolidBrush(Color.FromArgb(60, color)))
        {
            g.FillEllipse(glow, head.X - (s * 0.95f), head.Y - (s * 0.95f), s * 1.9f, s * 1.9f);
        }

        using (var core = new SolidBrush(color))
        {
            g.FillEllipse(core, head.X - (s * 0.5f), head.Y - (s * 0.5f), s, s);
        }

        using var outline = new Pen(Color.FromArgb(240, 255, 255, 255), 1.6f);
        g.DrawEllipse(outline, head.X - (s * 0.5f), head.Y - (s * 0.5f), s, s);
    }

    private static void PaintOrbit(Graphics g, PointF p, int s, Color color, float phase)
    {
        var anchor = new PointF(p.X + 16f + (s / 2f), p.Y + 16f + (s / 2f));
        float ringR = (s * 1.1f) + 9f;
        var ring = new RectangleF(anchor.X - ringR, anchor.Y - ringR, ringR * 2, ringR * 2);

        using (var orbit = new Pen(Color.FromArgb(60, color), 1.1f))
        {
            g.DrawEllipse(orbit, ring);
        }

        float angle = phase * 115f;
        DrawMoon(g, anchor, ringR, angle, (s * 0.26f) + 1.4f, color, 255);
        DrawMoon(g, anchor, ringR * 0.72f, angle + 210f, (s * 0.17f) + 1.1f, color, 210);

        float coreR = (s * 0.34f) + 1.6f;
        using (var glow = new SolidBrush(Color.FromArgb(70, color)))
        {
            g.FillEllipse(glow, anchor.X - (coreR * 2.1f), anchor.Y - (coreR * 2.1f), coreR * 4.2f, coreR * 4.2f);
        }

        using (var core = new SolidBrush(color))
        {
            g.FillEllipse(core, anchor.X - coreR, anchor.Y - coreR, coreR * 2, coreR * 2);
        }

        using var outline = new Pen(Color.FromArgb(235, 255, 255, 255), 1.3f);
        g.DrawEllipse(outline, anchor.X - coreR, anchor.Y - coreR, coreR * 2, coreR * 2);
    }

    private static void DrawMoon(Graphics g, PointF centre, float radius, float degrees, float moonRadius, Color color, int alpha)
    {
        double rad = degrees * Math.PI / 180.0;
        float x = centre.X + (float)(Math.Cos(rad) * radius);
        float y = centre.Y + (float)(Math.Sin(rad) * radius);

        using var fill = new SolidBrush(Color.FromArgb(alpha, color));
        g.FillEllipse(fill, x - moonRadius, y - moonRadius, moonRadius * 2, moonRadius * 2);

        using var outline = new Pen(Color.FromArgb(Math.Min(alpha, 220), 255, 255, 255), 1f);
        g.DrawEllipse(outline, x - moonRadius, y - moonRadius, moonRadius * 2, moonRadius * 2);
    }

    private static void PaintBuddy(Graphics g, PointF p, int s, Color color, float phase)
    {
        float w = s * 1.75f;
        float h = s * 1.4f;
        float bob = MathF.Sin(phase * 2.2f) * 1.4f;
        float x = p.X + 15f;
        float y = p.Y + 13f + bob;

        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            g.FillEllipse(shadow, x + 1.5f, y + h - 3f, w * 0.92f, 7f);
        }

        using var body = RoundedRect(new RectangleF(x, y, w, h), h * 0.48f);

        using (var fill = new SolidBrush(color))
        {
            g.FillPath(fill, body);
        }

        using (var outline = new Pen(Color.FromArgb(240, 255, 255, 255), 1.7f))
        {
            g.DrawPath(outline, body);
        }

        bool blinking = phase % 3.6f < 0.14f;
        float eyeW = s * 0.44f;
        float eyeH = s * 0.5f;
        float eyeY = y + (h * 0.34f);

        foreach (float fraction in new[] { 0.3f, 0.62f })
        {
            float eyeX = x + (w * fraction);

            if (blinking)
            {
                using var lid = new Pen(Ink, MathF.Max(1.4f, s * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(lid, eyeX, eyeY + (eyeH * 0.45f), eyeX + eyeW, eyeY + (eyeH * 0.45f));
                continue;
            }

            using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            {
                g.FillEllipse(white, eyeX, eyeY, eyeW, eyeH);
            }

            float pupil = eyeH * 0.42f;
            using var pupilBrush = new SolidBrush(Ink);
            g.FillEllipse(
                pupilBrush,
                eyeX + ((eyeW - pupil) / 2f) + (eyeW * 0.14f),
                eyeY + ((eyeH - pupil) / 2f) + (eyeH * 0.06f),
                pupil,
                pupil);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        float d = r * 2f;

        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
