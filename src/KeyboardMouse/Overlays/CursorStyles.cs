using System.Drawing.Drawing2D;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Pointer indicator designs. Every style shows the same thing (the active
/// layer through its colour), but they suit different tastes: a small dot, a
/// presenter-style halo, corner brackets, and so on. Chosen with
/// settings.feedback.cursorStyle.
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
    Brackets
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
        (CursorStyle.Brackets, "brackets", "Brackets")
    };

    /// <summary>Ids that used to name a style and now map onto another one.</summary>
    private static readonly (string Id, CursorStyle Style)[] Aliases =
    {
        ("buddy", CursorStyle.Brackets)
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

        foreach (var alias in Aliases)
        {
            if (string.Equals(alias.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                style = alias.Style;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The colour the indicator normally draws in. Stored in settings as six hex
/// digits without the leading '#', so the guide's colour picker and the
/// settings file agree.
/// </summary>
public static class CursorColors
{
    /// <summary>Mouse mode is on: the calm green the app has always used.</summary>
    public const string DefaultHex = "2ecc71";

    public const string DefaultScrollHex = "3498db";
    public const string DefaultPrecisionHex = "9b59b6";
    public const string DefaultTurboHex = "e74c3c";
    public const string DefaultDragLockHex = "e67e22";
    public const string DefaultAutoClickHex = "f1c40f";

    public static string Normalize(string? hex) => Normalize(hex, DefaultHex);

    public static string Normalize(string? hex, string fallback)
    {
        string text = (hex ?? string.Empty).Trim().TrimStart('#');
        if (text.Length == 6 && text.All(Uri.IsHexDigit))
        {
            return text.ToLowerInvariant();
        }

        return Normalize(fallback, DefaultHex);
    }

    public static Color ToColor(string? hex)
    {
        string text = Normalize(hex);
        return Color.FromArgb(
            255,
            Convert.ToInt32(text[..2], 16),
            Convert.ToInt32(text.Substring(2, 2), 16),
            Convert.ToInt32(text.Substring(4, 2), 16));
    }
}

/// <summary>The colours the indicator draws with, one per state.</summary>
public readonly record struct CursorPalette(
    Color Normal,
    Color Scroll,
    Color Precision,
    Color Turbo,
    Color DragLock,
    Color AutoClick)
{
    public static CursorPalette Default { get; } = new(
        CursorColors.ToColor(CursorColors.DefaultHex),
        CursorColors.ToColor(CursorColors.DefaultScrollHex),
        CursorColors.ToColor(CursorColors.DefaultPrecisionHex),
        CursorColors.ToColor(CursorColors.DefaultTurboHex),
        CursorColors.ToColor(CursorColors.DefaultDragLockHex),
        CursorColors.ToColor(CursorColors.DefaultAutoClickHex));
}

/// <summary>Where the indicator window sits relative to the pointer, and how big it is.</summary>
public readonly record struct CursorVisual(int OffsetX, int OffsetY, int Width, int Height);

/// <summary>One recent pointer sample, aged 0 (fresh) to 1 (spent) for the comet tail.</summary>
public readonly record struct TrailSample(PointF Point, float Age);

/// <summary>
/// Paints the pointer indicator. Pure drawing: no state, no timers, so the
/// same code serves both the live overlay and the previews in the guide.
/// </summary>
public static class CursorPainter
{
    private static readonly Color Ink = Color.FromArgb(255, 24, 26, 32);

    /// <summary>
    /// Extra gap between the pointer and the shapes that are drawn beside it,
    /// so the dot never sits right on top of the arrow. Centred styles (rings,
    /// reticle, brackets) are left exactly on the aim point.
    /// </summary>
    public const int PointerGapX = 15;

    public const int PointerGapY = 17;

    /// <summary>Top-left of the dot, measured from the pointer: close, but clear of the arrow.</summary>
    private const float DotInset = 12f;

    /// <summary>Where the arrow's tip sits, measured from the pointer.</summary>
    private const float ArrowInset = 22f;

    /// <summary>Where the comet's head sits inside its window, relative to the pointer.</summary>
    public const float CometHeadInset = 10f;

    /// <summary>How far behind the head the comet's tail reaches, in pixels.</summary>
    public static bool IsAnimated(CursorStyle style) =>
        style is CursorStyle.Spotlight or CursorStyle.Orbit or CursorStyle.Comet or CursorStyle.Halo;

    /// <summary>
    /// Offset added to the window origin. Only the shapes that sit beside the
    /// pointer get it; rings stay centred on the pixel being aimed at.
    /// </summary>
    public static void Gap(CursorStyle style, out int x, out int y)
    {
        if (style is CursorStyle.Dot or CursorStyle.Comet or CursorStyle.Orbit)
        {
            x = PointerGapX;
            y = PointerGapY;
            return;
        }

        x = 0;
        y = 0;
    }

    public static CursorVisual Measure(CursorStyle style, int size)
    {
        int s = Math.Clamp(size, 4, 48);

        switch (style)
        {
            case CursorStyle.Dot:
                return new CursorVisual(0, 0, s + 24, s + 24);

            case CursorStyle.Halo:
            {
                float glow = MathF.Max(7f, s * 0.7f);
                int half = (int)MathF.Ceiling((s * 0.95f) + 7f + (glow / 2f)) + 4;
                return new CursorVisual(-half, -half, half * 2, half * 2);
            }

            case CursorStyle.Spotlight:
            {
                int half = (int)MathF.Ceiling((s * 3.2f) + 12f) + 6;
                return new CursorVisual(-half, -half, half * 2, half * 2);
            }

            case CursorStyle.Reticle:
            {
                int half = (int)MathF.Ceiling((s * 1.1f) + 8f) + 5;
                return new CursorVisual(-half, -half, half * 2, half * 2);
            }

            case CursorStyle.Arrow:
            {
                float scale = ((s * 1.2f) + 5f) / 18f;
                int extent = (int)MathF.Ceiling(ArrowInset + (11.4f * scale)) + 6;
                return new CursorVisual(0, 0, extent, extent);
            }

            case CursorStyle.Comet:
            {
                int reach = (int)MathF.Ceiling((s * 12f) + 160f) + 16;
                int offset = (int)CometHeadInset - reach;
                return new CursorVisual(offset, offset, reach * 2, reach * 2);
            }

            case CursorStyle.Orbit:
            {
                int half = (int)MathF.Ceiling((s * 1.1f) + 9f) + 8;
                int anchor = 16 + (s / 2);
                return new CursorVisual(anchor - half, anchor - half, half * 2, half * 2);
            }

            default:
            {
                int half = (int)MathF.Ceiling((s * 1.25f) + 10f) + 7;
                return new CursorVisual(-half, -half, half * 2, half * 2);
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
        IReadOnlyList<TrailSample>? trail)
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
                PaintBrackets(g, pointer, s, color);
                break;
        }
    }

    private static void PaintDot(Graphics g, PointF pointer, int s, Color color)
    {
        var rect = new RectangleF(pointer.X + DotInset, pointer.Y + DotInset, s, s);

        using var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
        g.FillEllipse(shadow, rect.X + 1.5f, rect.Y + 1.5f, rect.Width, rect.Height);

        using var fill = new SolidBrush(color);
        g.FillEllipse(fill, rect);

        using var outline = new Pen(Color.FromArgb(235, 255, 255, 255), 1.2f);
        g.DrawEllipse(outline, rect);
    }

    private static void PaintHalo(Graphics g, PointF p, int s, Color color, float phase)
    {
        // Ring centred on the pointer, breathing just enough to feel alive.
        float breathe = 1f + (MathF.Sin(phase * 1.4f) * 0.02f);
        float r = ((s * 0.95f) + 7f) * breathe;
        var ring = new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);

        using (var glow = new Pen(Color.FromArgb(24, color), MathF.Max(7f, s * 0.7f)))
        {
            g.DrawEllipse(glow, ring);
        }

        using (var soft = new Pen(Color.FromArgb(66, color), MathF.Max(3.2f, s * 0.28f)))
        {
            g.DrawEllipse(soft, ring);
        }

        using (var core = new Pen(color, MathF.Max(1.8f, s * 0.11f)))
        {
            g.DrawEllipse(core, ring);
        }

        // A white hairline just inside keeps the ring readable on pale walls.
        float inner = r - MathF.Max(1.6f, s * 0.1f);
        using (var hairline = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
        {
            g.DrawEllipse(hairline, p.X - inner, p.Y - inner, inner * 2, inner * 2);
        }

        using var centre = new SolidBrush(Color.FromArgb(225, color));
        g.FillEllipse(centre, p.X - 2f, p.Y - 2f, 4f, 4f);
    }

    private static void PaintSpotlight(Graphics g, PointF p, int s, Color color, float phase)
    {
        // A soft pool of light, low alpha so the screen under it stays usable.
        float breathe = 1f + (MathF.Sin(phase * 1.1f) * 0.03f);
        float r = ((s * 3.2f) + 12f) * breathe;
        var area = new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);

        using var path = new GraphicsPath();
        path.AddEllipse(area);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(88, color),
            SurroundColors = new[] { Color.FromArgb(0, color) },
            CenterPoint = p
        };
        g.FillEllipse(brush, area);

        using var core = new SolidBrush(Color.FromArgb(235, color));
        g.FillEllipse(core, p.X - 2.2f, p.Y - 2.2f, 4.4f, 4.4f);
    }

    private static void PaintReticle(Graphics g, PointF p, int s, Color color)
    {
        float r = (s * 1.1f) + 8f;
        float inner = r * 0.38f;

        using var arm = new Pen(color, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        foreach (var (dx, dy) in new (float Dx, float Dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var a = new PointF(p.X + (dx * inner), p.Y + (dy * inner));
            var b = new PointF(p.X + (dx * r), p.Y + (dy * r));
            g.DrawLine(arm, a, b);
        }

        using var centre = new SolidBrush(color);
        g.FillEllipse(centre, p.X - 1.6f, p.Y - 1.6f, 3.2f, 3.2f);
    }

    private static void PaintArrow(Graphics g, PointF p, int s, Color color)
    {
        // The familiar pointer arrow, upright rather than tilted, parked
        // below-right so it points at the cursor instead of covering it.
        float scale = ((s * 1.2f) + 5f) / 18f;
        float x = p.X + ArrowInset;
        float y = p.Y + ArrowInset;

        PointF At(float dx, float dy) => new(x + (dx * scale), y + (dy * scale));

        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            At(0f, 0f),
            At(0f, 16.4f),
            At(4.1f, 12.6f),
            At(6.5f, 18f),
            At(9f, 16.9f),
            At(6.6f, 11.5f),
            At(11.4f, 11.5f)
        });

        using (var shadowBrush = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
        using (var shadowPath = (GraphicsPath)path.Clone())
        {
            using var shift = new Matrix();
            shift.Translate(1.3f, 1.8f);
            shadowPath.Transform(shift);
            g.FillPath(shadowBrush, shadowPath);
        }

        using (var fill = new SolidBrush(color))
        {
            g.FillPath(fill, path);
        }

        using (var outline = new Pen(Color.FromArgb(240, 255, 255, 255), 1.5f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(outline, path);
        }
    }

    private static void PaintComet(Graphics g, PointF p, int s, Color color, IReadOnlyList<TrailSample>? trail)
    {
        var head = new PointF(p.X + CometHeadInset, p.Y + CometHeadInset);
        float reach = (s * 12f) + 160f;

        if (trail is { Count: > 1 })
        {
            // Head first, then the samples newest to oldest: the tail is one
            // smooth, tapered stroke instead of a chain of blobs.
            var points = new List<TrailVertex> { new(head, 1f) };
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                var sample = trail[i];
                float dx = head.X - sample.Point.X;
                float dy = head.Y - sample.Point.Y;
                if (MathF.Sqrt((dx * dx) + (dy * dy)) > reach)
                {
                    break;
                }

                points.Add(new TrailVertex(sample.Point, Math.Clamp(1f - sample.Age, 0f, 1f)));
            }

            if (points.Count > 1)
            {
                DrawTrail(g, points, s, color);
            }
        }

        using (var glow = new SolidBrush(Color.FromArgb(30, color)))
        {
            g.FillEllipse(glow, head.X - (s * 0.72f), head.Y - (s * 0.72f), s * 1.44f, s * 1.44f);
        }

        using (var core = new SolidBrush(color))
        {
            g.FillEllipse(core, head.X - (s * 0.5f), head.Y - (s * 0.5f), s, s);
        }

        using var outline = new Pen(Color.FromArgb(240, 255, 255, 255), 1.6f);
        g.DrawEllipse(outline, head.X - (s * 0.5f), head.Y - (s * 0.5f), s, s);
    }

    /// <summary>
    /// Draws the comet's tail. The samples are smoothed with two rounds of
    /// Chaikin subdivision (which never overshoots the way a spline can), then
    /// stroked once with a width and alpha that fade towards the oldest end.
    /// </summary>
    private static void DrawTrail(Graphics g, List<TrailVertex> points, int s, Color color)
    {
        var smooth = Subdivide(Subdivide(points));
        using var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (int i = 1; i < smooth.Count; i++)
        {
            var a = smooth[i - 1];
            var b = smooth[i];

            float fade = (a.Fade + b.Fade) * 0.5f;
            float eased = fade * fade;

            int alpha = (int)(210f * eased);
            if (alpha < 4)
            {
                continue;
            }

            pen.Color = Color.FromArgb(alpha, color);
            pen.Width = 1.1f + (s * 0.46f * eased);
            g.DrawLine(pen, a.P, b.P);
        }
    }

    private static List<TrailVertex> Subdivide(List<TrailVertex> points)
    {
        if (points.Count < 3)
        {
            return points;
        }

        var result = new List<TrailVertex>((points.Count * 2) + 2) { points[0] };

        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            result.Add(TrailVertex.Lerp(a, b, 0.25f));
            result.Add(TrailVertex.Lerp(a, b, 0.75f));
        }

        result.Add(points[^1]);
        return result;
    }

    private readonly record struct TrailVertex(PointF P, float Fade)
    {
        public static TrailVertex Lerp(TrailVertex a, TrailVertex b, float t) =>
            new(
                new PointF(a.P.X + ((b.P.X - a.P.X) * t), a.P.Y + ((b.P.Y - a.P.Y) * t)),
                a.Fade + ((b.Fade - a.Fade) * t));
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

    private static void PaintBrackets(Graphics g, PointF p, int s, Color color)
    {
        // Four corner brackets: a selection frame around the aim point.
        float r = (s * 1.25f) + 10f;
        float arm = r * 0.5f;
        float width = MathF.Max(2f, s * 0.14f);

        using var under = new Pen(Color.FromArgb(80, 255, 255, 255), width + 1.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var ink = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        foreach (var (sx, sy) in new (float Sx, float Sy)[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var corner = new PointF(p.X + (sx * r), p.Y + (sy * r));
            var horizontal = new PointF(corner.X - (sx * arm), corner.Y);
            var vertical = new PointF(corner.X, corner.Y - (sy * arm));

            g.DrawLine(under, corner, horizontal);
            g.DrawLine(under, corner, vertical);
            g.DrawLine(ink, corner, horizontal);
            g.DrawLine(ink, corner, vertical);
        }
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
}
