using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Tests;

/// <summary>
/// Screens that stress geometry rather than classification: shadows, rounded
/// corners, neighbours with a thin gap, containers, frame edges and pages large
/// enough that the pipeline has to work at a reduced scale.
/// </summary>
internal static class GeometryFixtures
{
    private static readonly Color Page = Color.FromArgb(255, 255, 255);
    private static readonly Color Ink = Color.FromArgb(32, 32, 32);
    private static readonly Color Edge = Color.FromArgb(120, 120, 120);
    private static readonly Color Accent = Color.FromArgb(45, 108, 223);
    private static readonly Color Shadow = Color.FromArgb(214, 214, 214);
    private static readonly Color Panel = Color.FromArgb(243, 243, 243);

    /// <summary>One shadowed outline button, one shadowed filled button, one plain rounded one.</summary>
    public static Image32 RoundedAndShadow(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(520, 220, Page);
        var g = canvas.Graphics;

        var outlined = new Rectangle(40, 40, 140, 40);
        var filled = new Rectangle(40, 120, 160, 44);
        var plain = new Rectangle(240, 40, 120, 34);

        DropShadow(g, outlined);
        RoundOutline(g, outlined, Edge, 1.6f, 8);
        SyntheticUi.Text(g, "Cancel", outlined.Left + 42, outlined.Top + 11, Ink);

        DropShadow(g, filled);
        SyntheticUi.FillRounded(g, filled, Accent, 9);
        SyntheticUi.Text(g, "Save changes", filled.Left + 24, filled.Top + 12, Color.White);

        SyntheticUi.FillRounded(g, plain, Color.FromArgb(232, 232, 232), 7);
        SyntheticUi.Text(g, "Flat", plain.Left + 44, plain.Top + 8, Ink);

        truth = [outlined, filled, plain];
        return canvas.ToImage();
    }

    /// <summary>Three buttons with a 6 px gap: close enough that a sloppy centre lands on the seam.</summary>
    public static Image32 AdjacentButtons(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(420, 120, Page);
        var g = canvas.Graphics;
        var buttons = new[]
        {
            new Rectangle(20, 40, 80, 30),
            new Rectangle(106, 40, 80, 30),
            new Rectangle(192, 40, 80, 30)
        };

        foreach (var button in buttons)
        {
            SyntheticUi.Fill(g, button, Page);
            RoundOutline(g, button, Edge, 1.6f, 6);
        }

        SyntheticUi.Text(g, "One", buttons[0].Left + 24, buttons[0].Top + 7, Ink);
        SyntheticUi.Text(g, "Two", buttons[1].Left + 24, buttons[1].Top + 7, Ink);
        SyntheticUi.Text(g, "Three", buttons[2].Left + 18, buttons[2].Top + 7, Ink);

        truth = buttons;
        return canvas.ToImage();
    }

    /// <summary>A card holding one button, and a button holding a glyph.</summary>
    public static Image32 NestedPanel(out Rectangle panel, out Rectangle button)
    {
        using var canvas = new UiCanvas(440, 260, Page);
        var g = canvas.Graphics;

        panel = new Rectangle(30, 30, 300, 180);
        SyntheticUi.FillRounded(g, panel, Panel, 10);
        SyntheticUi.Text(g, "Account settings", panel.Left + 18, panel.Top + 16, Ink);
        SyntheticUi.Text(g, "Manage how hints behave.", panel.Left + 18, panel.Top + 42, Color.FromArgb(90, 90, 90));

        button = new Rectangle(48, 150, 120, 32);
        SyntheticUi.FillRounded(g, button, Accent, 6);
        SyntheticUi.Text(g, "Open", button.Left + 38, button.Top + 7, Color.White);

        var iconButton = new Rectangle(360, 40, 44, 44);
        SyntheticUi.FillRounded(g, iconButton, Color.FromArgb(232, 232, 232), 8);
        SyntheticUi.Icon(g, Rectangle.Inflate(iconButton, -10, -10), Color.FromArgb(40, 40, 40));

        return canvas.ToImage();
    }

    /// <summary>Controls flush with the frame edge, where a box is legitimately clipped.</summary>
    public static Image32 EdgeRow(out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(300, 120, Page);
        var g = canvas.Graphics;

        var left = new Rectangle(0, 30, 70, 30);
        var middle = new Rectangle(110, 30, 80, 30);
        var right = new Rectangle(230, 30, 70, 30);

        SyntheticUi.Fill(g, left, Color.FromArgb(236, 236, 236));
        SyntheticUi.Text(g, "Back", left.Left + 20, left.Top + 7, Ink);

        SyntheticUi.Fill(g, middle, Page);
        RoundOutline(g, middle, Edge, 1.6f, 6);
        SyntheticUi.Text(g, "Next", middle.Left + 22, middle.Top + 7, Ink);

        SyntheticUi.Fill(g, right, Accent);
        SyntheticUi.Text(g, "Done", right.Left + 16, right.Top + 7, Color.White);

        truth = [left, middle, right];
        return canvas.ToImage();
    }

    /// <summary>A page large enough that the pipeline drops below native resolution.</summary>
    public static Image32 WidePage(int width, int height, out Rectangle[] truth)
    {
        using var canvas = new UiCanvas(width, height, Page);
        var g = canvas.Graphics;

        var buttons = new[]
        {
            new Rectangle(80, 80, 160, 40),
            new Rectangle(300, 80, 160, 40),
            new Rectangle(80, 200, 200, 44),
            new Rectangle(340, 200, 140, 38)
        };

        foreach (var button in buttons)
        {
            SyntheticUi.Fill(g, button, Color.FromArgb(240, 240, 240));
            RoundOutline(g, button, Edge, 1.8f, 6);
            SyntheticUi.Text(g, "Action", button.Left + 40, button.Top + 10, Ink);
        }

        truth = buttons;
        return canvas.ToImage();
    }

    /// <summary>A thick open arc: a control whose centre is empty background.</summary>
    public static Image32 OpenArc(out Rectangle truth)
    {
        using var canvas = new UiCanvas(200, 140, Page);
        var g = canvas.Graphics;
        truth = new Rectangle(60, 30, 70, 70);

        using var pen = new Pen(Color.FromArgb(36, 36, 36), 12f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.DrawArc(pen, truth, 40, 250);

        return canvas.ToImage();
    }

    private static void DropShadow(Graphics g, Rectangle bounds)
    {
        SyntheticUi.FillRounded(g, new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width, bounds.Height), Shadow, 9);
    }

    private static void RoundOutline(Graphics g, Rectangle bounds, Color color, float width, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
        path.Dispose();
    }
}
