using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// One monitor-sized layered window for grid mode, styled after Mouseless.
///
/// Primary phase: the whole ten by thirty lattice is drawn at once and every
/// cell carries the complete code that reaches it (`AG`, `SW`, ...), so the
/// target is identifiable before anything is typed. Cells that stop matching the
/// typed prefix fade instead of disappearing.
///
/// Subgrid phase: the chosen cell fills the screen as a ten by three grid, and
/// each sub-cell shows its full three-key code. Micro phase: the chosen
/// sub-cell fills the screen as a three by three grid with four-key codes.
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

        private static readonly Color[] RowTints =
        [
            Color.FromArgb(52, 88, 138),
            Color.FromArgb(45, 116, 118),
            Color.FromArgb(86, 76, 128)
        ];

        public void DrawGrid(GridOverlayState state)
        {
            var bounds = state.Monitor.Bounds;
            int width = bounds.Width;
            int height = bounds.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            float scale = (float)Math.Max(1.0, state.Monitor.Scale);
            var root = Local(state.Root, bounds);
            var target = new Point(
                Math.Clamp(state.Target.X - bounds.Left, 0, width - 1),
                Math.Clamp(state.Target.Y - bounds.Top, 0, height - 1));

            // The subgrid and micro grids are full-screen zooms of the chosen
            // box, so the virtual cursor is drawn at its position inside that
            // box rather than at its absolute spot on the monitor.
            if (state.Phase == GridOverlayPhase.Subgrid && state.Cell is { } cell)
            {
                target = MapIntoSpan(target, Local(cell, bounds), width, height);
            }
            else if (state.Phase == GridOverlayPhase.Micro && state.Subcell is { } subcell)
            {
                target = MapIntoSpan(target, Local(subcell, bounds), width, height);
            }

            RenderSurface(width, height, bounds.Left, bounds.Top, g =>
            {
                int backdropAlpha = state.Phase == GridOverlayPhase.Primary ? 118 : 196;
                using (var backdrop = new SolidBrush(Color.FromArgb(backdropAlpha, 7, 10, 18)))
                {
                    g.FillRectangle(backdrop, 0, 0, width, height);
                }

                switch (state.Phase)
                {
                    case GridOverlayPhase.Primary:
                        DrawPrimary(g, root, state, scale);
                        break;

                    case GridOverlayPhase.Subgrid:
                        DrawCodes(
                            g,
                            root,
                            GridMath.SubgridColumns,
                            GridMath.SubgridRows,
                            state.SubgridLabels,
                            state.Typed,
                            scale,
                            15f,
                            64f);
                        break;

                    case GridOverlayPhase.Micro:
                        DrawCodes(
                            g,
                            root,
                            GridMath.MicroColumns,
                            GridMath.MicroRows,
                            state.MicroLabels,
                            state.Typed,
                            scale,
                            20f,
                            96f);
                        break;
                }

                DrawFrame(g, root, scale);
                DrawTarget(g, target, scale);
            });
        }

        /// <summary>
        /// The full lattice: colour blocked columns, thin row rules, and the
        /// complete two-key code in every cell.
        /// </summary>
        private static void DrawPrimary(
            Graphics g,
            Rectangle root,
            GridOverlayState state,
            float scale)
        {
            int selected = ColumnIndexOf(root, state.Column);

            for (int i = 0; i < GridMath.PrimaryColumns; i++)
            {
                var strip = GridMath.Cell(root, i, 0, GridMath.PrimaryColumns, 1);
                Color tint = ColumnTints[(i / 2) % ColumnTints.Length];

                if (i == selected)
                {
                    using var fill = new LinearGradientBrush(
                        strip,
                        WithAlpha(tint, 96),
                        WithAlpha(tint, 148),
                        LinearGradientMode.Vertical);
                    g.FillRectangle(fill, strip);
                }
                else
                {
                    using var fill = new SolidBrush(WithAlpha(tint, 34));
                    g.FillRectangle(fill, strip);
                }
            }

            DrawGridLines(g, root, GridMath.PrimaryColumns, GridMath.PrimaryRows, scale, rowAlpha: 92);

            float cellWidth = root.Width / (float)GridMath.PrimaryColumns;
            float cellHeight = root.Height / (float)GridMath.PrimaryRows;
            float fontSize = Math.Clamp(
                Math.Min(cellWidth * 0.40f, cellHeight * 0.66f),
                9f,
                26f);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var bright = new SolidBrush(Color.FromArgb(238, 255, 255, 255));
            using var dim = new SolidBrush(Color.FromArgb(58, 255, 255, 255));
            float offset = Math.Max(1f, 1.4f * scale);
            string typed = state.Typed;

            for (int row = 0; row < GridMath.PrimaryRows; row++)
            {
                if (row >= state.Level2Labels.Count)
                {
                    break;
                }

                for (int column = 0; column < GridMath.PrimaryColumns; column++)
                {
                    if (column >= state.Level1Labels.Count)
                    {
                        break;
                    }

                    string code = state.Level1Labels[column] + state.Level2Labels[row];
                    if (code.Length < 2)
                    {
                        continue;
                    }

                    bool matches = typed.Length == 0 ||
                        code.StartsWith(typed, StringComparison.OrdinalIgnoreCase);

                    var cell = GridMath.Cell(root, column, row, GridMath.PrimaryColumns, GridMath.PrimaryRows);
                    DrawShadowedString(
                        g,
                        code,
                        font,
                        matches ? bright : dim,
                        cell.Left + (cell.Width / 2f),
                        cell.Top + (cell.Height / 2f),
                        offset,
                        format,
                        shadowAlpha: matches ? 175 : 60);
                }
            }
        }

        /// <summary>
        /// One zoomed level: every cell of the chosen box fills the screen and
        /// carries its complete code, so the next key is obvious.
        /// </summary>
        private static void DrawCodes(
            Graphics g,
            Rectangle root,
            int columns,
            int rows,
            IReadOnlyList<string> labels,
            string prefix,
            float scale,
            float minFont,
            float maxFont)
        {
            for (int row = 0; row < rows; row++)
            {
                Color tint = RowTints[row % RowTints.Length];
                for (int column = 0; column < columns; column++)
                {
                    var cell = GridMath.Cell(root, column, row, columns, rows);
                    using var fill = new SolidBrush(WithAlpha(tint, 96));
                    g.FillRectangle(fill, cell);
                }
            }

            DrawGridLines(g, root, columns, rows, scale, rowAlpha: 214);

            if (labels.Count < columns * rows)
            {
                return;
            }

            var sample = GridMath.Cell(root, 0, 0, columns, rows);
            float fontSize = Math.Clamp(
                Math.Min(sample.Width * 0.34f, sample.Height * 0.40f),
                minFont,
                maxFont);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var brush = new SolidBrush(Color.FromArgb(248, 255, 255, 255));
            float offset = Math.Max(2f, 2.4f * scale);

            for (int i = 0; i < columns * rows; i++)
            {
                if (string.IsNullOrEmpty(labels[i]))
                {
                    continue;
                }

                var cell = GridMath.SubRect(root, i, columns, rows);
                DrawShadowedString(
                    g,
                    prefix + labels[i],
                    font,
                    brush,
                    cell.Left + (cell.Width / 2f),
                    cell.Top + (cell.Height / 2f),
                    offset,
                    format);
            }
        }

        private static int ColumnIndexOf(Rectangle root, Rectangle? column)
        {
            if (column is not { } value || root.Width <= 0)
            {
                return -1;
            }

            return Math.Clamp(
                (int)((long)(value.Left - root.Left) * GridMath.PrimaryColumns / root.Width),
                0,
                GridMath.PrimaryColumns - 1);
        }

        private static void DrawGridLines(
            Graphics g,
            Rectangle rect,
            int columns,
            int rows,
            float scale,
            int rowAlpha = 214)
        {
            using var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(3f, 2.6f * scale));
            using var vertical = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, 1.15f * scale));
            using var horizontal = new Pen(Color.FromArgb(rowAlpha, 255, 255, 255), Math.Max(1f, scale));

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

        private static Point MapIntoSpan(Point point, Rectangle span, int width, int height)
        {
            float x = span.Width > 0
                ? (point.X - span.Left) / (float)span.Width
                : 0.5f;
            float y = span.Height > 0
                ? (point.Y - span.Top) / (float)span.Height
                : 0.5f;

            return new Point(
                Math.Clamp((int)Math.Round(x * width), 0, Math.Max(0, width - 1)),
                Math.Clamp((int)Math.Round(y * height), 0, Math.Max(0, height - 1)));
        }

        private static Rectangle Local(Rectangle rect, Rectangle bounds) => new(
            rect.Left - bounds.Left,
            rect.Top - bounds.Top,
            rect.Width,
            rect.Height);
    }
}
