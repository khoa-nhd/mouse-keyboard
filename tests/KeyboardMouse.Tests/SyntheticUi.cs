using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Tests;

/// <summary>
/// Synthetic UI screens used by the vision tests. Drawing real controls with
/// GDI+ keeps the fixtures readable and lets every assertion name its own
/// ground-truth rectangle, which a screenshot fixture could not do.
/// </summary>
internal sealed class UiCanvas : IDisposable
{
    private readonly Bitmap _bitmap;

    public UiCanvas(int width, int height, Color background)
    {
        _bitmap = new Bitmap(width, height);
        Graphics = Graphics.FromImage(_bitmap);
        Graphics.Clear(background);
        Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public Graphics Graphics { get; }

    public Image32 ToImage() => Image32.FromBitmap(_bitmap);

    public void Dispose()
    {
        Graphics.Dispose();
        _bitmap.Dispose();
    }
}

internal static class SyntheticUi
{
    private static readonly Color Background = Color.FromArgb(255, 255, 255);
    private static readonly Color Surface = Color.FromArgb(246, 246, 246);

    public static void Outline(Graphics g, Rectangle bounds, Color color, float width = 1.6f)
    {
        using var pen = new Pen(color, width);
        g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    public static void Fill(Graphics g, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, bounds);
    }

    public static void FillRounded(Graphics g, Rectangle bounds, Color color, int radius)
    {
        using var path = RoundedPath(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void Text(Graphics g, string text, int x, int y, Color color, float size = 9f)
    {
        using var font = new Font("Segoe UI", size);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y);
    }

    public static void UnderlinedText(Graphics g, string text, int x, int y, Color color)
    {
        Text(g, text, x, y, color);

        using var font = new Font("Segoe UI", 9f);
        var measured = g.MeasureString(text, font);
        using var pen = new Pen(color, 1.2f);
        g.DrawLine(pen, x, y + measured.Height - 1, x + measured.Width - 2, y + measured.Height - 1);
    }

    /// <summary>A glyph-like cluster: a ring inside a dense hatch, 18x18 by default.</summary>
    public static void Icon(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.8f);
        g.DrawEllipse(pen, bounds);
        g.DrawLine(pen, bounds.Left + 3, bounds.Top + (bounds.Height / 2), bounds.Right - 3, bounds.Top + (bounds.Height / 2));
        g.DrawLine(pen, bounds.Left + (bounds.Width / 2), bounds.Top + 3, bounds.Left + (bounds.Width / 2), bounds.Bottom - 3);
        g.DrawLine(pen, bounds.Left + 4, bounds.Top + 4, bounds.Right - 4, bounds.Bottom - 4);
    }

    public static void CheckBox(Graphics g, Rectangle bounds, Color border, Color mark)
    {
        Fill(g, bounds, Background);
        Outline(g, bounds, border, 1.8f);

        using var pen = new Pen(mark, 1.8f);
        g.DrawLine(pen, bounds.Left + 3, bounds.Top + (bounds.Height / 2), bounds.Left + (bounds.Width / 2), bounds.Bottom - 4);
        g.DrawLine(pen, bounds.Left + (bounds.Width / 2), bounds.Bottom - 4, bounds.Right - 3, bounds.Top + 3);
    }

    public static void Radio(Graphics g, int centerX, int centerY, int radius, bool selected, Color border, Color mark)
    {
        using var pen = new Pen(border, 1.8f);
        g.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2, radius * 2);

        if (selected)
        {
            using var brush = new SolidBrush(mark);
            g.FillEllipse(brush, centerX - (radius / 2), centerY - (radius / 2), radius, radius);
        }
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---------- fixtures ----------

    /// <summary>Icon-only toolbar: three borderless tiles with an icon each.</summary>
    public static Image32 Toolbar(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(640, 56, Surface);
        var g = canvas.Graphics;
        var tiles = new[]
        {
            new Rectangle(8, 8, 40, 40),
            new Rectangle(56, 8, 40, 40),
            new Rectangle(104, 8, 40, 40)
        };

        foreach (var tile in tiles)
        {
            Fill(g, tile, Color.FromArgb(228, 228, 228));
            Icon(g, Rectangle.Inflate(tile, -11, -11), Color.FromArgb(40, 40, 40));
        }

        truth = tiles;
        return canvas.ToImage();
    }

    /// <summary>Two side-by-side bordered buttons plus a filled primary button.</summary>
    public static Image32 DialogButtons(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(520, 260, Color.FromArgb(250, 250, 250));
        var g = canvas.Graphics;

        var ok = new Rectangle(200, 200, 90, 30);
        var cancel = new Rectangle(298, 200, 90, 30);
        var primary = new Rectangle(180, 150, 140, 32);

        Fill(g, ok, Background);
        Outline(g, ok, Color.FromArgb(150, 150, 150), 1.6f);
        Text(g, "OK", ok.Left + 34, ok.Top + 6, Color.FromArgb(30, 30, 30));

        Fill(g, cancel, Background);
        Outline(g, cancel, Color.FromArgb(150, 150, 150), 1.6f);
        Text(g, "Cancel", cancel.Left + 20, cancel.Top + 6, Color.FromArgb(30, 30, 30));

        FillRounded(g, primary, Color.FromArgb(45, 108, 223), 6);
        Text(g, "Save changes", primary.Left + 24, primary.Top + 7, Color.White);

        truth = [ok, cancel, primary];
        return canvas.ToImage();
    }

    /// <summary>Three checkbox rows with labels.</summary>
    public static Image32 Checkboxes(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(480, 140, Background);
        var g = canvas.Graphics;
        var boxes = new[]
        {
            new Rectangle(20, 20, 18, 18),
            new Rectangle(20, 58, 18, 18),
            new Rectangle(20, 96, 18, 18)
        };

        var labels = new[] { "Remember me", "Show hints on start", "Copy labels" };

        for (int i = 0; i < boxes.Length; i++)
        {
            CheckBox(g, boxes[i], Color.FromArgb(120, 120, 120), Color.FromArgb(30, 30, 30));
            Text(g, labels[i], boxes[i].Right + 10, boxes[i].Top + 1, Color.FromArgb(30, 30, 30));
        }

        truth = boxes;
        return canvas.ToImage();
    }

    /// <summary>Two radio buttons, the first selected.</summary>
    public static Image32 Radios(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(320, 150, Background);
        var g = canvas.Graphics;

        Radio(g, 36, 32, 9, true, Color.FromArgb(120, 120, 120), Color.FromArgb(45, 108, 223));
        Radio(g, 36, 72, 9, false, Color.FromArgb(120, 120, 120), Color.FromArgb(45, 108, 223));
        Text(g, "Keyboard only", 56, 24, Color.FromArgb(30, 30, 30));
        Text(g, "Keyboard and mouse", 56, 64, Color.FromArgb(30, 30, 30));

        truth = [new Rectangle(27, 23, 18, 18), new Rectangle(27, 63, 18, 18)];
        return canvas.ToImage();
    }

    /// <summary>One underlined link and one plain paragraph line above it.</summary>
    public static Image32 Link(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(460, 110, Background);
        var g = canvas.Graphics;

        Text(g, "Hints are generated for every clickable element.", 20, 14, Color.FromArgb(60, 60, 60));
        UnderlinedText(g, "Learn more about hints", 20, 48, Color.FromArgb(20, 90, 200));

        truth = [new Rectangle(20, 48, 130, 20)];
        return canvas.ToImage();
    }

    /// <summary>Small controls a native-resolution pass can miss.</summary>
    public static Image32 SmallControls(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(900, 640, Surface);
        var g = canvas.Graphics;

        var tinyCheck = new Rectangle(40, 40, 13, 13);
        CheckBox(g, tinyCheck, Color.FromArgb(110, 110, 110), Color.FromArgb(30, 30, 30));

        var tinyIcon = new Rectangle(120, 40, 15, 15);
        Icon(g, tinyIcon, Color.FromArgb(50, 50, 50));

        var mediumIcon = new Rectangle(200, 36, 24, 24);
        Icon(g, mediumIcon, Color.FromArgb(50, 50, 50));

        var bigButton = new Rectangle(40, 120, 160, 38);
        Fill(g, bigButton, Background);
        Outline(g, bigButton, Color.FromArgb(140, 140, 140), 1.6f);
        Text(g, "Continue", bigButton.Left + 40, bigButton.Top + 9, Color.FromArgb(30, 30, 30));

        truth = [tinyCheck, tinyIcon, mediumIcon, bigButton];
        return canvas.ToImage();
    }

    /// <summary>A segmented control: one rounded container, three segments.</summary>
    public static Image32 Segmented(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(480, 120, Background);
        var g = canvas.Graphics;
        var container = new Rectangle(40, 40, 300, 36);

        FillRounded(g, container, Color.FromArgb(238, 238, 238), 8);

        var segments = new[]
        {
            new Rectangle(42, 42, 98, 32),
            new Rectangle(141, 42, 98, 32),
            new Rectangle(240, 42, 98, 32)
        };

        Fill(g, segments[0], Color.FromArgb(210, 210, 210));
        Fill(g, segments[1], Color.FromArgb(238, 238, 238));
        Fill(g, segments[2], Color.FromArgb(238, 238, 238));

        using var pen = new Pen(Color.FromArgb(190, 190, 190), 1.2f);
        g.DrawLine(pen, 140, 44, 140, 72);
        g.DrawLine(pen, 239, 44, 239, 72);

        Text(g, "Grid", 70, 47, Color.FromArgb(30, 30, 30));
        Text(g, "Hints", 169, 47, Color.FromArgb(30, 30, 30));
        Text(g, "Marks", 264, 47, Color.FromArgb(30, 30, 30));

        truth = segments;
        return canvas.ToImage();
    }
}
