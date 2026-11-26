using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// The 3 x 3 grid in the themes other than classic (which keeps its original
/// code in <see cref="GridOverlay"/>). Paints into any Graphics, so a preview
/// can render the same pixels the overlay shows.
/// </summary>
public static class RecursiveGridPainter
{
    private static readonly Color Blue = Color.FromArgb(47, 107, 255);
    private static readonly Color GlowBlue = Color.FromArgb(127, 178, 255);
    private static readonly Color Ink = Color.FromArgb(7, 10, 18);

    /// <param name="screen">The monitor, in surface coordinates.</param>
    /// <param name="active">The current level's rectangle, in surface coordinates.</param>
    /// <param name="target">Where the pointer will land, in surface coordinates.</param>
    public static void Draw(
        Graphics g,
        Rectangle screen,
        Rectangle active,
        Point target,
        GridOverlayState state,
        float scale,
        GridTheme theme)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        int outsideAlpha = theme switch
        {
            GridTheme.Frosted => 77,
            GridTheme.Spotlight => 158,
            GridTheme.Thin => 0,
            GridTheme.Cards => 115,
            GridTheme.Bubble => 90,
            GridTheme.Keyboard => 77,
            GridTheme.Glow => 179,
            _ => 96
        };

        if (outsideAlpha > 0)
        {
            using var outside = new SolidBrush(Color.FromArgb(outsideAlpha, Ink));
            FillOutside(g, outside, screen, active);
        }

        // Plain white keys need a darker floor than a bright page gives them,
        // also on the first level where the selection is the whole screen.
        int insideAlpha = theme switch
        {
            GridTheme.Keyboard => 80,
            GridTheme.Glow => 115,
            _ => 0
        };

        if (insideAlpha > 0)
        {
            using var inside = new SolidBrush(Color.FromArgb(insideAlpha, Ink));
            g.FillRectangle(inside, active);
        }

        int count = state.Columns * state.Rows;
        var sample = GridMath.SubRect(active, 0, state.Columns, state.Rows);
        float keySize = Math.Clamp(
            Math.Min(sample.Width * 0.30f, sample.Height * 0.42f),
            11f,
            Math.Max(11f, 64f * scale));

        if (theme == GridTheme.Glow)
        {
            DrawGlow(g, active, scale);
        }

        for (int i = 0; i < count; i++)
        {
            var cell = GridMath.SubRect(active, i, state.Columns, state.Rows);
            string key = i < state.Labels.Count ? state.Labels[i] : string.Empty;
            DrawCell(g, cell, key, keySize, scale, theme);
        }

        DrawFrame(g, active, scale, theme);

        if (theme == GridTheme.Keyboard)
        {
            DrawKeypad(g, active, target, state, scale);
        }

        if (theme is GridTheme.Frosted or GridTheme.Thin)
        {
            DrawDots(g, active, state.Depth, state.MaxDepth, scale, theme == GridTheme.Thin);
        }
        else
        {
            DrawStep(g, active, state.Depth, state.MaxDepth, scale);
        }
    }

    private static void DrawCell(Graphics g, Rectangle cell, string key, float keySize, float scale, GridTheme theme)
    {
        float cx = cell.Left + (cell.Width / 2f);
        float cy = cell.Top + (cell.Height / 2f);
        string upper = key.ToUpperInvariant();

        switch (theme)
        {
            case GridTheme.Frosted:
            {
                using (var fill = new SolidBrush(Color.FromArgb(15, 255, 255, 255)))
                using (var line = new Pen(Color.FromArgb(140, 255, 255, 255), Math.Max(1f, scale)))
                {
                    g.FillRectangle(fill, cell);
                    g.DrawRectangle(line, cell);
                }

                if (upper.Length == 0)
                {
                    return;
                }

                float d = Math.Clamp(keySize * 1.35f, 26f * scale, 70f * scale);
                var chip = new RectangleF(cx - (d / 2f), cy - (d / 2f), d, d);
                using (var chipFill = new SolidBrush(Color.FromArgb(185, 22, 24, 29)))
                using (var chipLine = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, scale)))
                {
                    g.FillEllipse(chipFill, chip);
                    g.DrawEllipse(chipLine, chip);
                }

                using var font = new Font("Segoe UI", d * 0.48f, FontStyle.Bold, GraphicsUnit.Pixel);
                Centered(g, upper, font, Color.White, cx, cy);
                return;
            }

            case GridTheme.Spotlight:
            {
                using (var line = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, scale)))
                {
                    g.DrawRectangle(line, cell);
                }

                if (upper.Length > 0)
                {
                    Keycap(g, upper, cx, cy, Math.Clamp(keySize * 0.6f, 12f, 30f * scale), scale);
                }

                return;
            }

            case GridTheme.Thin:
            {
                using (var shadow = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(2.5f, 2.5f * scale)))
                using (var dash = new Pen(Color.FromArgb(225, 255, 255, 255), Math.Max(1f, 1.2f * scale)))
                {
                    dash.DashPattern = [5f, 4f];
                    g.DrawRectangle(shadow, cell);
                    g.DrawRectangle(dash, cell);
                }

                if (upper.Length > 0)
                {
                    Outlined(g, upper, cx, cy, keySize, scale);
                }

                return;
            }

            case GridTheme.Cards:
            {
                float gap = 3f * scale;
                var card = RectangleF.Inflate(cell, -gap, -gap);
                using (var path = HintLabelPainter.Rounded(card, 8f * scale, 8f * scale))
                using (var fill = new SolidBrush(Color.FromArgb(107, 17, 20, 28)))
                using (var line = new Pen(Color.FromArgb(64, 255, 255, 255), Math.Max(1f, scale)))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(line, path);
                }

                if (upper.Length > 0)
                {
                    using var font = new Font("Segoe UI Semibold", keySize * 0.9f, FontStyle.Regular, GraphicsUnit.Pixel);
                    Centered(g, upper, font, Color.White, cx, cy);
                }

                return;
            }

            case GridTheme.Bubble:
            {
                using (var line = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, scale)))
                {
                    g.DrawRectangle(line, cell);
                }

                if (upper.Length > 0)
                {
                    Bubble(g, upper, cx, cy, Math.Clamp(keySize * 0.62f, 12f, 32f * scale), scale);
                }

                return;
            }

            case GridTheme.Keyboard:
            {
                using (var line = new Pen(Color.FromArgb(115, 255, 255, 255), Math.Max(1f, scale)))
                {
                    g.DrawRectangle(line, cell);
                }

                if (upper.Length > 0)
                {
                    using var font = new Font("Segoe UI Semibold", keySize * 0.75f, FontStyle.Regular, GraphicsUnit.Pixel);
                    Centered(g, upper, font, Color.FromArgb(190, 0, 0, 0), cx + scale, cy + (1.5f * scale));
                    Centered(g, upper, font, Color.FromArgb(242, 255, 255, 255), cx, cy);
                }

                return;
            }

            case GridTheme.Glow:
            {
                using (var line = new Pen(Color.FromArgb(90, GlowBlue), Math.Max(1f, scale)))
                {
                    g.DrawRectangle(line, cell);
                }

                if (upper.Length > 0)
                {
                    using var font = new Font("Segoe UI Semilight", keySize * 1.05f, FontStyle.Regular, GraphicsUnit.Pixel);
                    Centered(g, upper, font, Color.FromArgb(150, 0, 0, 0), cx + scale, cy + (1.5f * scale));
                    Centered(g, upper, font, Color.White, cx, cy);
                }

                return;
            }
        }
    }

    private static void DrawFrame(Graphics g, Rectangle active, float scale, GridTheme theme)
    {
        (Color color, float width, float radius)? frame = theme switch
        {
            GridTheme.Frosted => (Color.FromArgb(230, 255, 255, 255), 2f, 8f),
            GridTheme.Spotlight => (Blue, 2f, 6f),
            GridTheme.Thin => (Color.FromArgb(220, 17, 17, 17), 1.5f, 0f),
            GridTheme.Bubble => (Blue, 2f, 6f),
            GridTheme.Keyboard => (Color.FromArgb(200, 255, 255, 255), 1.5f, 6f),
            GridTheme.Glow => (GlowBlue, 2f, 6f),
            _ => null
        };

        if (frame is not { } f)
        {
            return;
        }

        using var path = HintLabelPainter.Rounded(active, f.radius * scale, f.radius * scale);

        if (theme == GridTheme.Thin)
        {
            using var light = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(3f, 3f * scale));
            g.DrawPath(light, path);
        }

        using var pen = new Pen(f.color, Math.Max(1.5f, f.width * scale));
        g.DrawPath(pen, path);
    }

    /// <summary>A soft blue halo built from widening, fading strokes.</summary>
    private static void DrawGlow(Graphics g, Rectangle active, float scale)
    {
        using var path = HintLabelPainter.Rounded(active, 6f * scale, 6f * scale);

        for (int ring = 6; ring >= 1; ring--)
        {
            using var pen = new Pen(Color.FromArgb(14 + ((6 - ring) * 6), GlowBlue), ring * 3f * scale)
            {
                LineJoin = LineJoin.Round
            };
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Small 3 x 3 key map in the corner; the key under the pointer is lit.</summary>
    private static void DrawKeypad(Graphics g, Rectangle active, Point target, GridOverlayState state, float scale)
    {
        float key = 18f * scale;
        float gap = 3f * scale;
        float pad = 6f * scale;
        float size = (key * 3) + (gap * 2) + (pad * 2);

        if (active.Width < size * 2.2f || active.Height < size * 2.2f)
        {
            return;
        }

        float x0 = active.Right - size - (10f * scale);
        float y0 = active.Bottom - size - (10f * scale);

        using (var panel = HintLabelPainter.Rounded(new RectangleF(x0, y0, size, size), 8f * scale, 8f * scale))
        using (var fill = new SolidBrush(Color.FromArgb(217, 17, 20, 28)))
        {
            g.FillPath(fill, panel);
        }

        int lit = -1;
        for (int i = 0; i < state.Columns * state.Rows; i++)
        {
            if (GridMath.SubRect(active, i, state.Columns, state.Rows).Contains(target))
            {
                lit = i;
                break;
            }
        }

        using var font = new Font("Segoe UI", 10f * scale, FontStyle.Bold, GraphicsUnit.Pixel);

        for (int i = 0; i < 9 && i < state.Labels.Count; i++)
        {
            float kx = x0 + pad + ((i % 3) * (key + gap));
            float ky = y0 + pad + ((i / 3) * (key + gap));
            bool on = i == lit;

            using (var cap = HintLabelPainter.Rounded(new RectangleF(kx, ky, key, key), 3f * scale, 3f * scale))
            using (var capFill = new SolidBrush(on ? Color.FromArgb(255, 216, 77) : Color.FromArgb(31, 255, 255, 255)))
            {
                g.FillPath(capFill, cap);
            }

            Centered(g, state.Labels[i].ToUpperInvariant(), font, on ? Color.FromArgb(17, 17, 17) : Color.White, kx + (key / 2f), ky + (key / 2f));
        }
    }

    /// <summary>"2 / 3": which level the grid is on.</summary>
    private static void DrawStep(Graphics g, Rectangle active, int depth, int maxDepth, float scale)
    {
        if (maxDepth < 2)
        {
            return;
        }

        string text = $"{Math.Min(depth + 1, maxDepth)} / {maxDepth}";
        using var font = new Font("Segoe UI Semibold", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        var size = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        float h = size.Height + (4f * scale);
        var pill = new RectangleF(active.Left + (8f * scale), active.Top + (8f * scale), size.Width + (16f * scale), h);

        using (var path = HintLabelPainter.Rounded(pill, h / 2f, h / 2f))
        using (var fill = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
        {
            g.FillPath(fill, path);
        }

        Centered(g, text, font, Color.White, pill.Left + (pill.Width / 2f), pill.Top + (pill.Height / 2f));
    }

    /// <summary>One dot per level, filled for the levels taken.</summary>
    private static void DrawDots(Graphics g, Rectangle active, int depth, int maxDepth, float scale, bool dark)
    {
        if (maxDepth < 2)
        {
            return;
        }

        float dot = Math.Max(3.5f, 3.5f * scale);
        float x = active.Left + (dot * 3f);
        float y = active.Top + (dot * 3f);
        var accent = dark ? Color.FromArgb(17, 17, 17) : Color.FromArgb(255, 216, 102);
        var outline = dark ? Color.FromArgb(17, 17, 17) : Color.White;

        using var filled = new SolidBrush(accent);
        using var empty = new Pen(outline, Math.Max(1f, scale));

        for (int i = 0; i < maxDepth; i++)
        {
            float cx = x + (i * dot * 3.4f);

            if (i <= depth)
            {
                g.FillEllipse(filled, cx - dot, y - dot, dot * 2f, dot * 2f);
            }
            else
            {
                g.DrawEllipse(empty, cx - dot, y - dot, dot * 2f, dot * 2f);
            }
        }
    }

    private static void Keycap(Graphics g, string text, float cx, float cy, float size, float scale)
    {
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        var measured = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        float w = Math.Max(measured.Width + (16f * scale), measured.Height + (10f * scale));
        float h = measured.Height + (6f * scale);
        float edge = 3f * scale;
        var face = new RectangleF(cx - (w / 2f), cy - ((h + edge) / 2f), w, h);

        using (var shadow = HintLabelPainter.Rounded(new RectangleF(face.X, face.Y + (2f * scale), w, h + edge), 5f * scale, 5f * scale))
        using (var shadowFill = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.FillPath(shadowFill, shadow);
        }

        using (var bottom = HintLabelPainter.Rounded(new RectangleF(face.X, face.Y, w, h + edge), 5f * scale, 5f * scale))
        using (var bottomFill = new SolidBrush(Color.FromArgb(138, 146, 156)))
        {
            g.FillPath(bottomFill, bottom);
        }

        using (var top = HintLabelPainter.Rounded(face, 5f * scale, 5f * scale))
        using (var topFill = new SolidBrush(Color.White))
        {
            g.FillPath(topFill, top);
        }

        Centered(g, text, font, Color.FromArgb(27, 31, 36), cx, face.Top + (h / 2f));
    }

    private static void Bubble(Graphics g, string text, float cx, float cy, float size, float scale)
    {
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        var measured = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        float w = measured.Width + (22f * scale);
        float h = measured.Height + (10f * scale);
        var box = new RectangleF(cx - (w / 2f), cy - (h / 2f), w, h);

        using (var shadow = HintLabelPainter.Rounded(new RectangleF(box.X, box.Y + (2f * scale), w, h), 8f * scale, 8f * scale))
        using (var shadowFill = new SolidBrush(Color.FromArgb(128, 20, 40, 120)))
        {
            g.FillPath(shadowFill, shadow);
        }

        using (var tail = new GraphicsPath())
        using (var fill = new SolidBrush(Blue))
        {
            float tx = box.Left + (10f * scale);
            tail.AddPolygon(
            [
                new PointF(tx, box.Bottom - 1),
                new PointF(tx + (11f * scale), box.Bottom - 1),
                new PointF(tx + (3f * scale), box.Bottom + (7f * scale))
            ]);
            g.FillPath(fill, tail);

            using var path = HintLabelPainter.Rounded(box, 8f * scale, 8f * scale);
            g.FillPath(fill, path);
        }

        Centered(g, text, font, Color.White, cx, cy);
    }

    /// <summary>White letters with a dark outline: readable on any background without a fill.</summary>
    private static void Outlined(Graphics g, string text, float cx, float cy, float size, float scale)
    {
        using var family = new FontFamily("Segoe UI");
        using var path = new GraphicsPath();
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        path.AddString(text, family, (int)FontStyle.Bold, size * 0.95f, new PointF(cx, cy), format);

        using var stroke = new Pen(Color.FromArgb(235, 17, 17, 17), Math.Max(2f, 2.4f * scale)) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(Color.White);
        g.DrawPath(stroke, path);
        g.FillPath(fill, path);
    }

    private static void Centered(Graphics g, string text, Font font, Color color, float cx, float cy)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, cx, cy, format);
    }

    private static void FillOutside(Graphics g, Brush brush, Rectangle outer, Rectangle inner)
    {
        if (inner.Top > outer.Top)
        {
            g.FillRectangle(brush, outer.Left, outer.Top, outer.Width, inner.Top - outer.Top);
        }

        if (inner.Bottom < outer.Bottom)
        {
            g.FillRectangle(brush, outer.Left, inner.Bottom, outer.Width, outer.Bottom - inner.Bottom);
        }

        if (inner.Left > outer.Left)
        {
            g.FillRectangle(brush, outer.Left, inner.Top, inner.Left - outer.Left, inner.Height);
        }

        if (inner.Right < outer.Right)
        {
            g.FillRectangle(brush, inner.Right, inner.Top, outer.Right - inner.Right, inner.Height);
        }
    }
}
