using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Draws hint labels in the chosen theme. Kept apart from the window so a
/// preview or a test can paint the same pixels into a bitmap.
/// </summary>
public static class HintLabelPainter
{
    /// <summary>Alpha of a label that no longer matches what was typed.</summary>
    private const int FadedAlpha = 38;

    private static readonly Color Blue = Color.FromArgb(47, 107, 255);
    private static readonly Color DeepBlue = Color.FromArgb(30, 79, 216);

    /// <summary>Everything that differs between the label themes.</summary>
    private sealed record Look(
        Color Fill,
        Color Text,
        Color Typed,
        Color? Border = null,
        float BorderWidth = 1f,
        float Radius = 6f,
        float PadX = 8f,
        float PadY = 4f,
        Color? Shadow = null,
        Color? Tail = null,
        bool Pill = false,
        bool Uppercase = false,
        Color? KeyEdge = null,
        bool TagHole = false,
        float LeftRadius = -1f);

    private static Look LookFor(HintTheme theme) => theme switch
    {
        HintTheme.Bubble => new Look(
            Blue, Color.White, Color.FromArgb(115, 255, 255, 255),
            Radius: 7, Shadow: Color.FromArgb(115, 20, 40, 120), Tail: Blue),
        HintTheme.BlueOutline => new Look(
            Color.White, DeepBlue, Color.FromArgb(90, 30, 79, 216),
            Border: Blue, BorderWidth: 1.5f, Radius: 7, Shadow: Color.FromArgb(77, 0, 0, 0), Tail: Blue),
        HintTheme.Glass => new Look(
            Color.FromArgb(232, 255, 255, 255), Color.FromArgb(17, 17, 17), Blue,
            Border: Color.FromArgb(46, 0, 0, 0), Radius: 6, Shadow: Color.FromArgb(90, 0, 0, 0)),
        HintTheme.Badge => new Look(
            Color.FromArgb(17, 24, 39), Color.White, Color.FromArgb(255, 216, 77),
            Border: Color.FromArgb(255, 216, 77), BorderWidth: 2f, PadX: 6, PadY: 3, Pill: true,
            Shadow: Color.FromArgb(90, 0, 0, 0)),
        HintTheme.Tag => new Look(
            Color.FromArgb(255, 107, 74), Color.White, Color.FromArgb(128, 255, 255, 255),
            Radius: 7, LeftRadius: 3, PadX: 8, TagHole: true, Shadow: Color.FromArgb(90, 0, 0, 0)),
        HintTheme.Purple => new Look(
            Color.FromArgb(59, 42, 140), Color.FromArgb(244, 247, 161), Color.FromArgb(158, 140, 240),
            Radius: 6, Shadow: Color.FromArgb(100, 0, 0, 0)),
        HintTheme.Keycap => new Look(
            Color.White, Color.FromArgb(27, 31, 36), Blue,
            Border: Color.FromArgb(154, 161, 171), Radius: 5, PadX: 7, PadY: 2, Uppercase: true,
            KeyEdge: Color.FromArgb(125, 133, 144), Shadow: Color.FromArgb(90, 0, 0, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    /// <summary>
    /// Paints every label onto a surface that covers <paramref name="monitor"/>,
    /// whose top-left corner is the surface origin.
    /// </summary>
    public static void Draw(
        Graphics g,
        MonitorInfo monitor,
        IReadOnlyList<(ClickableTarget Target, string Label)> items,
        string typed,
        HintTheme theme)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (theme == HintTheme.Classic)
        {
            DrawClassic(g, monitor, items, typed);
            return;
        }

        // Grayscale antialiasing: ClearType fringes show on a layered surface.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var look = LookFor(theme);
        float scale = (float)monitor.Scale;
        float fontSize = Math.Clamp(13f * scale, 11f, 28f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        foreach (var (target, label) in items)
        {
            bool matches = typed.Length == 0 || label.StartsWith(typed, StringComparison.Ordinal);
            int typedLength = matches ? typed.Length : 0;
            string shown = look.Uppercase ? label.ToUpperInvariant() : label;

            var text = g.MeasureString(shown, font, int.MaxValue, format);
            float padX = look.PadX * scale;
            float padY = look.PadY * scale;
            float extraLeft = look.TagHole ? 4f * scale : 0f;
            float edge = look.KeyEdge is null ? 0f : 2f * scale;
            float height = text.Height + (padY * 2) + edge;
            float width = text.Width + (padX * 2) + extraLeft;

            if (look.Pill)
            {
                width = Math.Max(width, height);
            }

            float x = Math.Clamp(target.Bounds.Left - monitor.Bounds.Left, 2f, Math.Max(2f, monitor.Bounds.Width - width - 2f));
            float y = Math.Clamp(target.Bounds.Top - monitor.Bounds.Top, 2f, Math.Max(2f, monitor.Bounds.Height - height - 2f));
            var box = new RectangleF(x, y, width, height);
            float radius = look.Pill ? height / 2f : look.Radius * scale;
            float leftRadius = look.LeftRadius >= 0 ? look.LeftRadius * scale : radius;

            int alpha = matches ? 255 : FadedAlpha;

            if (look.Shadow is { } shadow)
            {
                using var shadowPath = Rounded(new RectangleF(x, y + (2f * scale), width, height), leftRadius, radius);
                using var shadowBrush = new SolidBrush(Fade(shadow, alpha));
                g.FillPath(shadowBrush, shadowPath);
            }

            if (look.Tail is { } tail)
            {
                float tailX = x + (6f * scale);
                using var tailPath = new GraphicsPath();
                tailPath.AddPolygon(
                [
                    new PointF(tailX, y + height - 1),
                    new PointF(tailX + (10f * scale), y + height - 1),
                    new PointF(tailX + (3f * scale), y + height + (6f * scale))
                ]);
                using var tailBrush = new SolidBrush(Fade(tail, alpha));
                g.FillPath(tailBrush, tailPath);
            }

            if (look.KeyEdge is { } keyEdge)
            {
                using var edgePath = Rounded(box, leftRadius, radius);
                using var edgeBrush = new SolidBrush(Fade(keyEdge, alpha));
                g.FillPath(edgeBrush, edgePath);
            }

            var face = look.KeyEdge is null ? box : new RectangleF(x, y, width, height - edge);

            using (var facePath = Rounded(face, leftRadius, radius))
            using (var fill = new SolidBrush(Fade(look.Fill, alpha)))
            {
                g.FillPath(fill, facePath);

                if (look.Border is { } border)
                {
                    using var pen = new Pen(Fade(border, alpha), Math.Max(1f, look.BorderWidth * scale));
                    g.DrawPath(pen, facePath);
                }
            }

            if (look.TagHole)
            {
                float hole = 5f * scale;
                using var holeBrush = new SolidBrush(Fade(Color.White, alpha));
                g.FillEllipse(holeBrush, x + (4f * scale), y + ((height - hole) / 2f), hole, hole);
            }

            float textX = x + padX + extraLeft + ((width - (text.Width + (padX * 2) + extraLeft)) / 2f);
            float textY = y + padY;
            string head = shown[..typedLength];
            string rest = shown[typedLength..];

            if (head.Length > 0)
            {
                using var typedBrush = new SolidBrush(look.Typed);
                g.DrawString(head, font, typedBrush, textX, textY, format);
                textX += g.MeasureString(head, font, int.MaxValue, format).Width;
            }

            using var textBrush = new SolidBrush(Fade(look.Text, alpha));
            g.DrawString(rest, font, textBrush, textX, textY, format);
        }
    }

    /// <summary>The original yellow tag, unchanged.</summary>
    private static void DrawClassic(
        Graphics g,
        MonitorInfo monitor,
        IReadOnlyList<(ClickableTarget Target, string Label)> items,
        string typed)
    {
        var labelColor = Color.FromArgb(255, 255, 214, 10);
        var labelBorder = Color.FromArgb(230, 30, 30, 30);
        var typedColor = Color.FromArgb(255, 210, 55, 35);

        float scale = (float)monitor.Scale;
        float fontSize = Math.Clamp(13f * scale, 11f, 28f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };

        foreach (var (target, label) in items)
        {
            bool matches = typed.Length == 0 ||
                label.StartsWith(typed, StringComparison.Ordinal);
            int alpha = matches ? 255 : 38;

            var measured = g.MeasureString(label, font);
            float paddingX = 6f * scale;
            float paddingY = 2f * scale;
            float boxWidth = measured.Width + (paddingX * 2);
            float boxHeight = measured.Height + (paddingY * 2);

            int localLeft = target.Bounds.Left - monitor.Bounds.Left;
            int localTop = target.Bounds.Top - monitor.Bounds.Top;

            float x = Math.Clamp(localLeft, 2f, Math.Max(2f, monitor.Bounds.Width - boxWidth - 2f));
            float y = Math.Clamp(localTop, 2f, Math.Max(2f, monitor.Bounds.Height - boxHeight - 2f));
            var box = new RectangleF(x, y, boxWidth, boxHeight);

            using (var shadow = new SolidBrush(Color.FromArgb(Math.Min(120, alpha), 0, 0, 0)))
            using (var path = Rounded(new RectangleF(x + 2, y + 2, boxWidth, boxHeight), 4f * scale, 4f * scale))
            {
                g.FillPath(shadow, path);
            }

            using (var background = new SolidBrush(Color.FromArgb(alpha, labelColor)))
            using (var border = new Pen(Color.FromArgb(alpha, labelBorder), Math.Max(1f, scale)))
            using (var path = Rounded(box, 4f * scale, 4f * scale))
            {
                g.FillPath(background, path);
                g.DrawPath(border, path);
            }

            using var textBrush = new SolidBrush(
                matches ? Color.FromArgb(255, 25, 25, 25) : Color.FromArgb(alpha, 25, 25, 25));

            if (typed.Length > 0 && label.StartsWith(typed, StringComparison.Ordinal))
            {
                float typedWidth = g.MeasureString(label[..typed.Length], font).Width;
                using var typedBrush = new SolidBrush(Color.FromArgb(255, typedColor));
                g.DrawString(label[..typed.Length], font, typedBrush,
                    new RectangleF(x + paddingX, y + paddingY, typedWidth, boxHeight), format);
                g.DrawString(label[typed.Length..], font, textBrush,
                    new RectangleF(x + paddingX + typedWidth, y + paddingY, boxWidth, boxHeight), format);
            }
            else
            {
                g.DrawString(label, font, textBrush,
                    new RectangleF(x + paddingX, y + paddingY, boxWidth, boxHeight), format);
            }
        }
    }

    private static Color Fade(Color color, int alpha) =>
        Color.FromArgb(color.A * alpha / 255, color.R, color.G, color.B);

    /// <summary>Rounded rectangle with its own radius for the left corners (a tag's notch side).</summary>
    internal static GraphicsPath Rounded(RectangleF rect, float leftRadius, float rightRadius)
    {
        var path = new GraphicsPath();
        float l = Math.Min(leftRadius * 2, Math.Min(rect.Width, rect.Height));
        float r = Math.Min(rightRadius * 2, Math.Min(rect.Width, rect.Height));

        if (l <= 1 && r <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }

        l = Math.Max(l, 0.5f);
        r = Math.Max(r, 0.5f);
        path.AddArc(rect.X, rect.Y, l, l, 180, 90);
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        path.AddArc(rect.X, rect.Bottom - l, l, l, 90, 90);
        path.CloseFigure();
        return path;
    }
}
