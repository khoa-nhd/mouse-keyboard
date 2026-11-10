using System.Drawing;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Tests;

internal sealed record ControlTruth(Rectangle Bounds, ControlType Type);

/// <summary>
/// One synthetic screen plus its typed ground truth. Categories exist so the
/// tests can report precision and recall per target type instead of one
/// aggregate number — an average would hide a broken icon-button rule behind
/// good text-button scores.
/// </summary>
internal sealed record ControlFixture(
    string Category,
    string Name,
    Image32 Image,
    ControlTruth[] Truth,
    bool HasInteractiveTargets = true);

internal static class ControlFixtures
{
    private static readonly Color Page = Color.FromArgb(255, 255, 255);
    private static readonly Color Ink = Color.FromArgb(32, 32, 32);
    private static readonly Color Muted = Color.FromArgb(110, 110, 110);
    private static readonly Color Accent = Color.FromArgb(45, 108, 223);
    private static readonly Color AccentInk = Color.FromArgb(20, 80, 190);
    private static readonly Color Edge = Color.FromArgb(150, 150, 150);

    public static (string Category, Image32 Image, ControlTruth[] Truth, bool Interactive)[] All()
    {
        var results = new List<(string, Image32, ControlTruth[], bool)>();

        results.Add(TextButtons());
        results.Add(IconButtons());
        results.Add(BorderlessToolbar());
        results.Add(SmallToolbar());
        results.Add(DenseNavigation());
        results.Add(CustomDrawn());
        results.Add(DecorativePage());
        results.Add(CardsWithActions());
        return results.ToArray();
    }

    public static Image32 Pick(string category) =>
        All().First(fixture => fixture.Category == category).Image;

    // ---------- drawing helpers ----------

    private static void Plus(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.8f);
        g.DrawLine(pen, bounds.Left, bounds.Top + (bounds.Height / 2), bounds.Right, bounds.Top + (bounds.Height / 2));
        g.DrawLine(pen, bounds.Left + (bounds.Width / 2), bounds.Top, bounds.Left + (bounds.Width / 2), bounds.Bottom);
    }

    private static void Chevron(Graphics g, Rectangle bounds, Color color, int direction = 1)
    {
        using var pen = new Pen(color, 1.8f);
        int mid = bounds.Top + (bounds.Height / 2);

        if (direction > 0)
        {
            g.DrawLine(pen, bounds.Left, mid - 3, bounds.Left + (bounds.Width / 2), mid + 3);
            g.DrawLine(pen, bounds.Left + (bounds.Width / 2), mid + 3, bounds.Right, mid - 3);
        }
        else
        {
            g.DrawLine(pen, bounds.Left, bounds.Top + (bounds.Height / 2), bounds.Right, bounds.Top);
            g.DrawLine(pen, bounds.Left, bounds.Top + (bounds.Height / 2), bounds.Right, bounds.Bottom);
        }
    }

    private static void MenuGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.8f);

        for (int i = 0; i < 3; i++)
        {
            int y = bounds.Top + 3 + (i * (bounds.Height - 6) / 2);
            g.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }
    }

    private static void SearchGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.8f);
        int d = (int)(bounds.Width * 0.7);
        g.DrawEllipse(pen, bounds.Left, bounds.Top, d, d);
        g.DrawLine(pen, bounds.Left + d - 1, bounds.Top + d - 1, bounds.Right, bounds.Bottom);
    }

    private static void GearGlyph(Graphics g, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.8f);
        using var brush = new SolidBrush(color);
        int cx = bounds.Left + (bounds.Width / 2);
        int cy = bounds.Top + (bounds.Height / 2);
        g.DrawEllipse(pen, cx - 5, cy - 5, 10, 10);

        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3;
            int x = cx + (int)(Math.Cos(angle) * 8);
            int y = cy + (int)(Math.Sin(angle) * 8);
            g.FillEllipse(brush, x - 1, y - 1, 3, 3);
        }
    }

    private static void Photo(Graphics g, Rectangle bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(196, 214, 236));
        g.FillRectangle(brush, bounds);

        var random = new Random(bounds.Width * 31 + bounds.Height);
        using var detail = new SolidBrush(Color.FromArgb(120, 150, 190));

        for (int i = 0; i < 26; i++)
        {
            int w = 6 + random.Next(26);
            int h = 6 + random.Next(20);
            int x = bounds.Left + random.Next(Math.Max(1, bounds.Width - w));
            int y = bounds.Top + random.Next(Math.Max(1, bounds.Height - h));
            g.FillEllipse(detail, x, y, w, h);
        }
    }

    // ---------- categories ----------

    /// <summary>Bordered, filled, flat, outlined, rounded and icon+text buttons.</summary>
    private static (string, Image32, ControlTruth[], bool) TextButtons()
    {
        using var canvas = new UiCanvas(560, 320, Page);
        var g = canvas.Graphics;

        var ok = new Rectangle(40, 40, 90, 30);
        SyntheticUi.Fill(g, ok, Page);
        SyntheticUi.Outline(g, ok, Edge);
        SyntheticUi.Text(g, "OK", ok.Left + 34, ok.Top + 6, Ink);

        var cancel = new Rectangle(140, 40, 96, 30);
        SyntheticUi.Fill(g, cancel, Page);
        SyntheticUi.Outline(g, cancel, Edge);
        SyntheticUi.Text(g, "Cancel", cancel.Left + 22, cancel.Top + 6, Ink);

        var save = new Rectangle(60, 96, 150, 34);
        SyntheticUi.FillRounded(g, save, Accent, 7);
        SyntheticUi.Text(g, "Save changes", save.Left + 22, save.Top + 8, Color.White);

        var flat = new Rectangle(240, 96, 110, 32);
        SyntheticUi.FillRounded(g, flat, Color.FromArgb(238, 238, 238), 6);
        SyntheticUi.Text(g, "Flat", flat.Left + 38, flat.Top + 7, Ink);

        var outlined = new Rectangle(60, 152, 130, 32);
        SyntheticUi.Fill(g, outlined, Page);
        SyntheticUi.Outline(g, outlined, Accent, 1.8f);
        SyntheticUi.Text(g, "Outlined", outlined.Left + 30, outlined.Top + 7, AccentInk);

        var iconText = new Rectangle(220, 152, 150, 32);
        SyntheticUi.FillRounded(g, iconText, Accent, 6);
        Plus(g, new Rectangle(iconText.Left + 14, iconText.Top + 10, 12, 12), Color.White);
        SyntheticUi.Text(g, "Download", iconText.Left + 38, iconText.Top + 7, Color.White);

        // Footer of text-only buttons: no surface at all, layout is the only cue.
        SyntheticUi.Text(g, "Not now", 120, 232, AccentInk);
        SyntheticUi.Text(g, "Continue", 250, 232, AccentInk);

        var truth = new[]
        {
            new ControlTruth(ok, ControlType.Button),
            new ControlTruth(cancel, ControlType.Button),
            new ControlTruth(save, ControlType.Button),
            new ControlTruth(flat, ControlType.Button),
            new ControlTruth(outlined, ControlType.Button),
            new ControlTruth(iconText, ControlType.Button)
        };

        return ("text-buttons", canvas.ToImage(), truth, true);
    }

    /// <summary>Title-bar controls and a row of bordered icon buttons.</summary>
    private static (string, Image32, ControlTruth[], bool) IconButtons()
    {
        using var canvas = new UiCanvas(620, 190, Page);
        var g = canvas.Graphics;

        var titleBar = new Rectangle(0, 0, 620, 36);
        SyntheticUi.Fill(g, titleBar, Color.FromArgb(242, 242, 242));

        var min = new Rectangle(520, 4, 30, 28);
        var max = new Rectangle(552, 4, 30, 28);
        var close = new Rectangle(584, 4, 30, 28);

        using (var pen = new Pen(Ink, 1.6f))
        {
            g.DrawLine(pen, min.Left + 10, min.Top + 16, min.Right - 10, min.Top + 16);
            g.DrawRectangle(pen, max.Left + 10, max.Top + 9, 10, 10);
            g.DrawLine(pen, close.Left + 10, close.Top + 9, close.Right - 10, close.Bottom - 9);
            g.DrawLine(pen, close.Right - 10, close.Top + 9, close.Left + 10, close.Bottom - 9);
        }

        var row = new List<Rectangle>();

        for (int i = 0; i < 5; i++)
        {
            var tile = new Rectangle(24 + (i * 44), 60, 34, 32);
            SyntheticUi.FillRounded(g, tile, Color.FromArgb(240, 240, 240), 5);
            row.Add(tile);
        }

        Chevron(g, new Rectangle(row[0].Left + 11, row[0].Top + 11, 12, 12), Ink, -1);
        Chevron(g, new Rectangle(row[1].Left + 11, row[1].Top + 11, 12, 12), Ink);
        SearchGlyph(g, new Rectangle(row[2].Left + 10, row[2].Top + 9, 14, 14), Ink);
        GearGlyph(g, new Rectangle(row[3].Left + 10, row[3].Top + 9, 14, 14), Ink);
        MenuGlyph(g, new Rectangle(row[4].Left + 10, row[4].Top + 11, 14, 10), Ink);

        var fab = new Rectangle(40, 122, 48, 48);
        using (var brush = new SolidBrush(Accent))
        {
            g.FillEllipse(brush, fab);
        }

        Plus(g, new Rectangle(fab.Left + 16, fab.Top + 16, 16, 16), Color.White);

        var del = new Rectangle(140, 130, 34, 32);
        SyntheticUi.FillRounded(g, del, Color.FromArgb(240, 240, 240), 5);
        using (var pen = new Pen(Ink, 1.6f))
        {
            g.DrawRectangle(pen, del.Left + 10, del.Top + 11, 10, 11);
            g.DrawLine(pen, del.Left + 8, del.Top + 9, del.Right - 8, del.Top + 9);
        }

        var more = new Rectangle(190, 130, 34, 32);
        SyntheticUi.FillRounded(g, more, Color.FromArgb(240, 240, 240), 5);
        using (var brush = new SolidBrush(Ink))
        {
            for (int i = 0; i < 3; i++)
            {
                g.FillEllipse(brush, more.Left + 9 + (i * 6), more.Top + 15, 3, 3);
            }
        }

        var truth = new List<ControlTruth>
        {
            new(min, ControlType.IconButton),
            new(max, ControlType.IconButton),
            new(close, ControlType.IconButton),
            new(fab, ControlType.IconButton),
            new(del, ControlType.IconButton),
            new(more, ControlType.IconButton)
        };

        truth.AddRange(row.Select(tile => new ControlTruth(tile, ControlType.IconButton)));

        return ("icon-buttons", canvas.ToImage(), truth.ToArray(), true);
    }

    /// <summary>Bare icons in a toolbar: no tile, no border, grouping only.</summary>
    private static (string, Image32, ControlTruth[], bool) BorderlessToolbar()
    {
        using var canvas = new UiCanvas(620, 56, Color.FromArgb(248, 248, 248));
        var g = canvas.Graphics;

        var truth = new List<ControlTruth>();
        int x = 20;

        for (int i = 0; i < 6; i++)
        {
            var glyph = new Rectangle(x, 18, 20, 20);

            switch (i % 3)
            {
                case 0: SearchGlyph(g, glyph, Ink); break;
                case 1: GearGlyph(g, glyph, Ink); break;
                default: MenuGlyph(g, new Rectangle(x, 21, 20, 14), Ink); break;
            }

            truth.Add(new ControlTruth(glyph, ControlType.ToolbarItem));
            x += 44;
        }

        using (var pen = new Pen(Color.FromArgb(210, 210, 210), 1.2f))
        {
            g.DrawLine(pen, 286, 14, 286, 42);
            g.DrawLine(pen, 470, 14, 470, 42);
        }

        return ("borderless-toolbar", canvas.ToImage(), truth.ToArray(), true);
    }

    /// <summary>12-14 px icons, the size a single-scale pass loses.</summary>
    private static (string, Image32, ControlTruth[], bool) SmallToolbar()
    {
        using var canvas = new UiCanvas(420, 44, Color.FromArgb(250, 250, 250));
        var g = canvas.Graphics;
        var truth = new List<ControlTruth>();
        int x = 16;

        for (int i = 0; i < 8; i++)
        {
            var glyph = new Rectangle(x, 15, 14, 14);
            SyntheticUi.Icon(g, glyph, Color.FromArgb(45, 45, 45));
            truth.Add(new ControlTruth(glyph, ControlType.ToolbarItem));
            x += 30;
        }

        return ("small-toolbar", canvas.ToImage(), truth.ToArray(), true);
    }

    /// <summary>Sidebar with icon+label rows, a search field, a dropdown, breadcrumbs.</summary>
    private static (string, Image32, ControlTruth[], bool) DenseNavigation()
    {
        using var canvas = new UiCanvas(320, 380, Page);
        var g = canvas.Graphics;

        var search = new Rectangle(20, 16, 220, 30);
        SyntheticUi.FillRounded(g, search, Color.FromArgb(242, 242, 242), 5);
        SyntheticUi.Text(g, "Search settings", search.Left + 10, search.Top + 7, Muted);

        var dropdown = new Rectangle(20, 56, 200, 30);
        SyntheticUi.Fill(g, dropdown, Page);
        SyntheticUi.Outline(g, dropdown, Edge);
        SyntheticUi.Text(g, "Appearance", dropdown.Left + 10, dropdown.Top + 7, Ink);
        Chevron(g, new Rectangle(dropdown.Right - 20, dropdown.Top + 11, 10, 8), Ink);

        var panel = new Rectangle(16, 100, 240, 216);
        SyntheticUi.FillRounded(g, panel, Color.FromArgb(248, 248, 248), 6);

        var rows = new List<Rectangle>();

        for (int i = 0; i < 6; i++)
        {
            var row = new Rectangle(24, 108 + (i * 34), 224, 30);

            if (i == 1)
            {
                SyntheticUi.FillRounded(g, row, Color.FromArgb(228, 236, 250), 5);
            }

            SyntheticUi.Icon(g, new Rectangle(row.Left + 8, row.Top + 8, 14, 14), Color.FromArgb(70, 70, 70));
            SyntheticUi.Text(g, new[] { "General", "Appearance", "Shortcuts", "Cursor", "Hints", "About" }[i], row.Left + 34, row.Top + 6, Ink);
            rows.Add(row);
        }

        SyntheticUi.UnderlinedText(g, "Documentation", 24, 334, AccentInk);
        SyntheticUi.UnderlinedText(g, "Changelog", 150, 334, AccentInk);

        var truth = new List<ControlTruth>
        {
            new(search, ControlType.TextField),
            new(dropdown, ControlType.Dropdown)
        };

        truth.AddRange(rows.Select(row => new ControlTruth(row, ControlType.MenuItem)));
        truth.Add(new ControlTruth(new Rectangle(24, 334, 92, 20), ControlType.Link));
        truth.Add(new ControlTruth(new Rectangle(150, 334, 74, 20), ControlType.Link));

        return ("dense-navigation", canvas.ToImage(), truth.ToArray(), true);
    }

    /// <summary>Owner-drawn controls: switch, slider, segmented tabs, checkbox, radio.</summary>
    private static (string, Image32, ControlTruth[], bool) CustomDrawn()
    {
        using var canvas = new UiCanvas(480, 280, Page);
        var g = canvas.Graphics;

        var toggle = new Rectangle(40, 40, 46, 22);
        SyntheticUi.FillRounded(g, toggle, Color.FromArgb(60, 160, 90), 11);

        using (var brush = new SolidBrush(Color.White))
        {
            g.FillEllipse(brush, toggle.Right - 20, toggle.Top + 3, 16, 16);
        }

        var track = new Rectangle(40, 96, 200, 6);
        SyntheticUi.FillRounded(g, track, Color.FromArgb(200, 200, 200), 3);
        SyntheticUi.FillRounded(g, new Rectangle(track.Left, track.Top, 120, 6), Accent, 3);

        using (var brush = new SolidBrush(Accent))
        {
            g.FillEllipse(brush, track.Left + 114, track.Top - 5, 16, 16);
        }

        var segmented = new Rectangle(40, 140, 210, 32);
        SyntheticUi.FillRounded(g, segmented, Color.FromArgb(238, 238, 238), 7);

        var tabs = new[]
        {
            new Rectangle(42, 142, 68, 28),
            new Rectangle(111, 142, 68, 28),
            new Rectangle(180, 142, 68, 28)
        };

        SyntheticUi.FillRounded(g, tabs[0], Color.White, 5);
        SyntheticUi.Text(g, "Day", tabs[0].Left + 22, tabs[0].Top + 5, Ink);
        SyntheticUi.Text(g, "Week", tabs[1].Left + 16, tabs[1].Top + 5, Muted);
        SyntheticUi.Text(g, "Month", tabs[2].Left + 12, tabs[2].Top + 5, Muted);

        var check = new Rectangle(40, 200, 18, 18);
        SyntheticUi.CheckBox(g, check, Color.FromArgb(120, 120, 120), Ink);
        SyntheticUi.Text(g, "Sync now", check.Right + 10, check.Top + 1, Ink);

        SyntheticUi.Radio(g, 230, 208, 9, true, Color.FromArgb(120, 120, 120), Accent);
        SyntheticUi.Text(g, "Metric", 250, 200, Ink);

        var custom = new Rectangle(240, 90, 160, 38);
        SyntheticUi.FillRounded(g, custom, Color.FromArgb(250, 176, 60), 10);
        SyntheticUi.Outline(g, custom, Color.FromArgb(196, 132, 30), 1.4f);
        SyntheticUi.Text(g, "Owner drawn", custom.Left + 32, custom.Top + 10, Color.FromArgb(60, 40, 0));

        var truth = new[]
        {
            new ControlTruth(toggle, ControlType.Switch),
            new ControlTruth(new Rectangle(track.Left, 88, 208, 22), ControlType.Slider),
            new ControlTruth(tabs[0], ControlType.Tab),
            new ControlTruth(tabs[1], ControlType.Tab),
            new ControlTruth(tabs[2], ControlType.Tab),
            new ControlTruth(check, ControlType.Checkbox),
            new ControlTruth(new Rectangle(221, 199, 18, 18), ControlType.RadioButton),
            new ControlTruth(custom, ControlType.Button)
        };

        return ("custom-drawn", canvas.ToImage(), truth, true);
    }

    /// <summary>Paragraphs, decorative glyphs, separators and a photo: nothing clickable.</summary>
    private static (string, Image32, ControlTruth[], bool) DecorativePage()
    {
        using var canvas = new UiCanvas(460, 320, Page);
        var g = canvas.Graphics;

        SyntheticUi.Text(g, "Hints are generated for every clickable element on the screen.", 24, 24, Color.FromArgb(70, 70, 70));
        SyntheticUi.Text(g, "Move the pointer with the keyboard and press a letter to click.", 24, 48, Color.FromArgb(70, 70, 70));
        SyntheticUi.Text(g, "Nothing in this block is interactive.", 24, 72, Color.FromArgb(70, 70, 70));

        SyntheticUi.Icon(g, new Rectangle(370, 34, 30, 30), Color.FromArgb(190, 190, 190));
        SyntheticUi.Icon(g, new Rectangle(40, 280, 24, 24), Color.FromArgb(190, 190, 190));

        using (var pen = new Pen(Color.FromArgb(224, 224, 224), 1.2f))
        {
            g.DrawLine(pen, 24, 116, 436, 116);
            g.DrawLine(pen, 24, 260, 436, 260);
        }

        Photo(g, new Rectangle(24, 130, 150, 110));

        return ("decorative", canvas.ToImage(), Array.Empty<ControlTruth>(), false);
    }

    /// <summary>Two cards: one purely informational, one containing a real button.</summary>
    private static (string, Image32, ControlTruth[], bool) CardsWithActions()
    {
        using var canvas = new UiCanvas(460, 300, Color.FromArgb(250, 250, 250));
        var g = canvas.Graphics;

        var info = new Rectangle(24, 24, 400, 100);
        SyntheticUi.FillRounded(g, info, Color.White, 8);
        SyntheticUi.Outline(g, info, Color.FromArgb(230, 230, 230), 1.2f);
        SyntheticUi.Text(g, "Keyboard hints", info.Left + 16, info.Top + 14, Ink);
        SyntheticUi.Text(g, "Labels appear on every clickable element.", info.Left + 16, info.Top + 40, Muted);

        var interactive = new Rectangle(24, 144, 400, 120);
        SyntheticUi.FillRounded(g, interactive, Color.White, 8);
        SyntheticUi.Outline(g, interactive, Color.FromArgb(230, 230, 230), 1.2f);
        SyntheticUi.Text(g, "Smallest target", interactive.Left + 16, interactive.Top + 14, Ink);

        var action = new Rectangle(interactive.Left + 16, interactive.Top + 66, 120, 32);
        SyntheticUi.FillRounded(g, action, Accent, 6);
        SyntheticUi.Text(g, "Set minimum", action.Left + 14, action.Top + 7, Color.White);

        var truth = new[] { new ControlTruth(action, ControlType.Button) };
        return ("cards", canvas.ToImage(), truth, true);
    }
}
