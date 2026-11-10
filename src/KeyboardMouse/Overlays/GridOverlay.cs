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

    public void Show(GridOverlayState state)
    {
        _form.DrawGrid(state);
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

        public void DrawGrid(GridOverlayState state)
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

            RenderSurface(width, height, bounds.Left, bounds.Top, g =>
            {
                using (var backdrop = new SolidBrush(Color.FromArgb(118, 7, 10, 18)))
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

                using var fill = new SolidBrush(WithAlpha(tint, band % 2 == 0 ? 38 : 24));
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
                    shadowAlpha: matches ? 170 : 45);
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
            using var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(3f, 2.6f * scale));
            using var vertical = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1f, scale));
            using var horizontal = new Pen(Color.FromArgb(96, 255, 255, 255), Math.Max(1f, scale));

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
            using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(4f, 3f * scale));
            using var line = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1.5f, 1.6f * scale));
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
