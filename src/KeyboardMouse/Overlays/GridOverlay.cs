using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// One monitor-sized layered window for grid mode, styled after Mouseless.
///
/// There is a single grid and a single frame: every cell shows its complete
/// four-character code from the moment it opens, so the user reads the code in
/// the cell they want and types it. Cells that stop matching the typed prefix
/// fade instead of disappearing, and the cell the code currently points at is
/// highlighted.
/// </summary>
public sealed class GridOverlay : IGridOverlay
{
    private readonly GridOverlayForm _form = new();

    /// <summary>How the 3 x 3 grid looks; applied from settings, used on the next draw.</summary>
    public GridTheme Theme { get; set; } = GridTheme.Classic;

    public void Show(GridOverlayState state)
    {
        _form.DrawGrid(state, Theme);
        _form.ShowOverlay();
    }

    public void Hide() => _form.HideOverlay();

    public void Dispose() => _form.Dispose();

    private sealed class GridOverlayForm : OverlayForm
    {
        private static readonly Color[] ColumnTints =
        [
            Color.FromArgb(59, 91, 140),
            Color.FromArgb(73, 82, 133),
            Color.FromArgb(44, 112, 123),
            Color.FromArgb(84, 78, 130),
            Color.FromArgb(48, 100, 141)
        ];

        private static readonly Color Accent = Color.FromArgb(255, 214, 102);

        public void DrawGrid(GridOverlayState state, GridTheme theme)
        {
            var bounds = state.Monitor.Bounds;
            int width = bounds.Width;
            int height = bounds.Height;
            if (width <= 0 || height <= 0 || state.Columns < 1 || state.Rows < 1)
            {
                return;
            }

            float scale = (float)Math.Max(1.0, state.Monitor.Scale);
            var root = Local(state.Root, bounds);
            var target = new Point(
                Math.Clamp(state.Target.X - bounds.Left, 0, width - 1),
                Math.Clamp(state.Target.Y - bounds.Top, 0, height - 1));

            if (state.Recursive)
            {
                RenderSurface(width, height, bounds.Left, bounds.Top, g =>
                {
                    var screen = new Rectangle(0, 0, width, height);

                    if (theme == GridTheme.Classic)
                    {
                        DrawRecursive(g, screen, root, state, scale);
                    }
                    else
                    {
                        RecursiveGridPainter.Draw(g, screen, root, target, state, scale, theme);
                    }

                    DrawTarget(g, target, scale);
                });
                return;
            }

            RenderSurface(width, height, bounds.Left, bounds.Top, g =>
            {
                // Light enough to read the screen through the grid; the codes
                // stay legible because each one carries its own shadow.
                using (var backdrop = new SolidBrush(Color.FromArgb(60, 7, 10, 18)))
                {
                    g.FillRectangle(backdrop, 0, 0, width, height);
                }

                DrawColumnTints(g, root, state);
                DrawHighlight(g, root, state);
                DrawGridLines(g, root, state.Columns, state.Rows, scale);
                DrawLabels(g, root, state, scale);
                DrawFrame(g, root, scale);
                DrawTarget(g, target, scale);
            });
        }

        /// <summary>
        /// The recursive grid: the monitor outside the current level is dimmed,
        /// the level itself is split 3 x 3 with its cell key in each cell, and a
        /// row of dots shows how many levels are left.
        /// </summary>
        private static void DrawRecursive(
            Graphics g,
            Rectangle screen,
            Rectangle active,
            GridOverlayState state,
            float scale)
        {
            using (var outside = new SolidBrush(Color.FromArgb(96, 7, 10, 18)))
            {
                FillOutside(g, outside, screen, active);
            }

            using (var inside = new SolidBrush(Color.FromArgb(40, 7, 10, 18)))
            {
                g.FillRectangle(inside, active);
            }

            for (int i = 0; i < state.Columns * state.Rows; i++)
            {
                var cell = GridMath.SubRect(active, i, state.Columns, state.Rows);
                Color tint = ColumnTints[i % ColumnTints.Length];
                using var fill = new SolidBrush(WithAlpha(tint, (i % 2) == 0 ? 30 : 18));
                g.FillRectangle(fill, cell);
            }

            using (var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(3f, 3f * scale)))
            using (var line = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1.2f, 1.4f * scale)))
            {
                for (int i = 1; i < state.Columns; i++)
                {
                    int x = active.Left + (int)((long)active.Width * i / state.Columns);
                    g.DrawLine(shadow, x, active.Top, x, active.Bottom);
                    g.DrawLine(line, x, active.Top, x, active.Bottom);
                }

                for (int i = 1; i < state.Rows; i++)
                {
                    int y = active.Top + (int)((long)active.Height * i / state.Rows);
                    g.DrawLine(shadow, active.Left, y, active.Right, y);
                    g.DrawLine(line, active.Left, y, active.Right, y);
                }
            }

            using (var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(4f, 3.4f * scale)))
            using (var frame = new Pen(WithAlpha(Accent, 230), Math.Max(2f, 2f * scale)))
            {
                g.DrawRectangle(shadow, active);
                g.DrawRectangle(frame, active);
            }

            var sample = GridMath.SubRect(active, 0, state.Columns, state.Rows);
            float fontSize = Math.Clamp(
                Math.Min(sample.Width * 0.30f, sample.Height * 0.42f),
                11f,
                Math.Max(11f, 64f * scale));

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var brush = new SolidBrush(Color.FromArgb(250, 255, 255, 255));
            float offset = Math.Max(1f, 1.5f * scale);

            for (int i = 0; i < state.Columns * state.Rows && i < state.Labels.Count; i++)
            {
                string label = state.Labels[i];
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                var cell = GridMath.SubRect(active, i, state.Columns, state.Rows);
                DrawShadowedString(
                    g,
                    label,
                    font,
                    brush,
                    cell.Left + (cell.Width / 2f),
                    cell.Top + (cell.Height / 2f),
                    offset,
                    format,
                    shadowAlpha: 200);
            }

            DrawDepth(g, active, state.Depth, state.MaxDepth, scale);
        }

        /// <summary>One dot per level: filled for the levels already taken.</summary>
        private static void DrawDepth(Graphics g, Rectangle active, int depth, int maxDepth, float scale)
        {
            if (maxDepth < 2)
            {
                return;
            }

            float dot = Math.Max(4f, 4f * scale);
            float gap = dot * 1.4f;
            float x = active.Left + (dot * 2f);
            float y = active.Top + (dot * 2f);

            using var filled = new SolidBrush(WithAlpha(Accent, 240));
            using var empty = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1f, scale));
            using var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0));

            for (int i = 0; i < maxDepth; i++)
            {
                float cx = x + (i * ((dot * 2f) + gap));
                g.FillEllipse(shadow, cx - dot - 1, y - dot - 1, (dot * 2f) + 2, (dot * 2f) + 2);

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

        /// <summary>
        /// One tint per ten-column band, so the bands the first code character
        /// selects are visible before anything is typed.
        /// </summary>
        private static void DrawColumnTints(Graphics g, Rectangle root, GridOverlayState state)
        {
            for (int band = 0; band * ColumnBand < state.Columns; band++)
            {
                int start = band * ColumnBand;
                int end = Math.Min(state.Columns, start + ColumnBand);
                int left = root.Left + (int)((long)root.Width * start / state.Columns);
                int right = root.Left + (int)((long)root.Width * end / state.Columns);
                var strip = new Rectangle(left, root.Top, right - left, root.Height);
                Color tint = ColumnTints[band % ColumnTints.Length];

                using var fill = new SolidBrush(WithAlpha(tint, band % 2 == 0 ? 26 : 15));
                g.FillRectangle(fill, strip);
            }
        }

        private static void DrawHighlight(Graphics g, Rectangle root, GridOverlayState state)
        {
            if (state.Highlight < 0 || state.Highlight >= state.Columns * state.Rows)
            {
                return;
            }

            var cell = GridMath.SubRect(root, state.Highlight, state.Columns, state.Rows);

            using var glow = new SolidBrush(WithAlpha(Accent, 34));
            g.FillRectangle(glow, cell);

            using var border = new Pen(WithAlpha(Accent, 210), 2f);
            g.DrawRectangle(border, cell);
        }

        /// <summary>
        /// Every cell carries its four characters. Cells still reachable from the
        /// typed prefix stay bright; the rest fade so the remaining choice reads
        /// at a glance.
        /// </summary>
        private static void DrawLabels(
            Graphics g,
            Rectangle root,
            GridOverlayState state,
            float scale)
        {
            var sample = GridMath.Cell(root, 0, 0, state.Columns, state.Rows);
            float fontSize = Math.Clamp(
                Math.Min(sample.Width * 0.24f, sample.Height * 0.40f),
                7f,
                26f);

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var bright = new SolidBrush(Color.FromArgb(242, 255, 255, 255));
            using var dim = new SolidBrush(Color.FromArgb(46, 255, 255, 255));
            float offset = Math.Max(1f, 1.4f * scale);
            string typed = state.Typed;

            for (int i = 0; i < state.Columns * state.Rows && i < state.Labels.Count; i++)
            {
                string code = state.Labels[i];
                if (string.IsNullOrEmpty(code))
                {
                    continue;
                }

                bool matches = GridCodes.Matches(code, typed);
                var cell = GridMath.SubRect(root, i, state.Columns, state.Rows);

                DrawShadowedString(
                    g,
                    code,
                    font,
                    matches ? bright : dim,
                    cell.Left + (cell.Width / 2f),
                    cell.Top + (cell.Height / 2f),
                    offset,
                    format,
                    shadowAlpha: matches ? 190 : 85);
            }
        }

        /// <summary>Columns share one tint per ten-column band of the code.</summary>
        private const int ColumnBand = 10;

        private static void DrawGridLines(
            Graphics g,
            Rectangle rect,
            int columns,
            int rows,
            float scale)
        {
            using var shadow = new Pen(Color.FromArgb(64, 0, 0, 0), Math.Max(3f, 2.6f * scale));
            using var vertical = new Pen(Color.FromArgb(78, 255, 255, 255), Math.Max(1f, scale));
            using var horizontal = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(1f, scale));

            for (int i = 1; i < columns; i++)
            {
                int x = rect.Left + (int)((long)rect.Width * i / columns);
                g.DrawLine(shadow, x, rect.Top, x, rect.Bottom);
                g.DrawLine(vertical, x, rect.Top, x, rect.Bottom);
            }

            for (int i = 1; i < rows; i++)
            {
                int y = rect.Top + (int)((long)rect.Height * i / rows);
                g.DrawLine(shadow, rect.Left, y, rect.Right, y);
                g.DrawLine(horizontal, rect.Left, y, rect.Right, y);
            }
        }

        private static void DrawFrame(Graphics g, Rectangle root, float scale)
        {
            using var shadow = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(4f, 3f * scale));
            using var line = new Pen(Color.FromArgb(185, 255, 255, 255), Math.Max(1.5f, 1.6f * scale));
            g.DrawRectangle(shadow, root);
            g.DrawRectangle(line, root);
        }

        private static void DrawTarget(Graphics g, Point target, float scale)
        {
            float radius = Math.Max(9f, 10f * scale);
            float x = target.X;
            float y = target.Y;

            using var shadow = new Pen(Color.FromArgb(190, 0, 0, 0), Math.Max(4f, 3.6f * scale));
            g.DrawEllipse(shadow, x - radius, y - radius, radius * 2f, radius * 2f);

            using var ring = new Pen(Color.FromArgb(255, 255, 255, 255), Math.Max(1.8f, 2f * scale));
            g.DrawEllipse(ring, x - radius, y - radius, radius * 2f, radius * 2f);

            float tick = Math.Max(4f, 5f * scale);
            g.DrawLine(ring, x - radius - tick, y, x - radius + tick, y);
            g.DrawLine(ring, x + radius - tick, y, x + radius + tick, y);
            g.DrawLine(ring, x, y - radius - tick, x, y - radius + tick);
            g.DrawLine(ring, x, y + radius - tick, x, y + radius + tick);

            float dot = Math.Max(2.2f, 2.6f * scale);
            using var fill = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            g.FillEllipse(fill, x - dot, y - dot, dot * 2f, dot * 2f);
        }

        private static void DrawShadowedString(
            Graphics g,
            string text,
            Font font,
            Brush brush,
            float x,
            float y,
            float offset,
            StringFormat format,
            int shadowAlpha = 185)
        {
            using var shadow = new SolidBrush(Color.FromArgb(shadowAlpha, 0, 0, 0));
            g.DrawString(text, font, shadow, x + offset, y + offset, format);
            g.DrawString(text, font, brush, x, y, format);
        }

        private static StringFormat CenteredFormat() => new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        private static Color WithAlpha(Color color, int alpha) =>
            Color.FromArgb(alpha, color.R, color.G, color.B);

        private static Rectangle Local(Rectangle rect, Rectangle bounds) => new(
            rect.Left - bounds.Left,
            rect.Top - bounds.Top,
            rect.Width,
            rect.Height);
    }
}
