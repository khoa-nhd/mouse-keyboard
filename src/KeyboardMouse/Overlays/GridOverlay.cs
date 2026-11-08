using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// One monitor-sized layered window for grid mode, styled after Mouseless:
/// translucent colour-blocked columns, a highlighted column with thirty row
/// keys, and a full-screen ten by three subgrid for the final click.
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

        private static readonly Color[] SubgridTints =
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

            // The subgrid is a full-screen zoom of the selected band, so the
            // virtual cursor is drawn at the target's position relative to the
            // band rather than at its absolute spot on the monitor.
            if (state.Phase == GridOverlayPhase.Subgrid && state.Cell is { } cell)
            {
                target = MapIntoSpan(target, Local(cell, bounds), width, height);
            }

            RenderSurface(width, height, bounds.Left, bounds.Top, g =>
            {
                using (var backdrop = new SolidBrush(Color.FromArgb(
                           state.Phase == GridOverlayPhase.Level1 ? 52 : 148,
                           7, 10, 18)))
                {
                    g.FillRectangle(backdrop, 0, 0, width, height);
                }

                switch (state.Phase)
                {
                    case GridOverlayPhase.Level1:
                        DrawColumns(g, root, state.Level1Labels, scale);
                        break;

                    case GridOverlayPhase.Level2:
                        DrawRows(
                            g,
                            root,
                            state.Column is { } column ? Local(column, bounds) : null,
                            state.Level1Labels,
                            state.Level2Labels,
                            scale);
                        break;

                    case GridOverlayPhase.Subgrid:
                        DrawSubgrid(g, root, state.SubgridLabels, scale);
                        break;
                }

                DrawFrame(g, root, scale);
                DrawTarget(g, target, scale);
            });
        }

        private static void DrawColumns(
            Graphics g,
            Rectangle root,
            IReadOnlyList<string> labels,
            float scale)
        {
            for (int i = 0; i < GridMath.Level1Columns; i++)
            {
                var cell = GridMath.Cell(root, i, 0, GridMath.Level1Columns, 1);
                Color tint = ColumnTints[(i / 2) % ColumnTints.Length];

                using (var fill = new LinearGradientBrush(
                           cell,
                           WithAlpha(tint, 58),
                           WithAlpha(tint, 104),
                           LinearGradientMode.Vertical))
                {
                    g.FillRectangle(fill, cell);
                }
            }

            DrawGridLines(g, root, GridMath.Level1Columns, 1, scale);

            if (labels.Count < GridMath.Level1Cells)
            {
                return;
            }

            float fontSize = Math.Clamp(
                Math.Min(root.Width / (float)GridMath.Level1Columns * 0.34f, root.Height * 0.075f),
                16f,
                76f);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var brush = new SolidBrush(Color.FromArgb(238, 255, 255, 255));
            float offset = Math.Max(1.5f, 2f * scale);

            for (int i = 0; i < GridMath.Level1Columns; i++)
            {
                string text = labels[i];
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var cell = GridMath.Cell(root, i, 0, GridMath.Level1Columns, 1);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    float y = cell.Top + cell.Height * (0.17f + repeat * 0.33f);
                    DrawShadowedString(g, text, font, brush, cell.Left + (cell.Width / 2f), y, offset, format);
                }
            }
        }

        private static void DrawRows(
            Graphics g,
            Rectangle root,
            Rectangle? selected,
            IReadOnlyList<string> level1Labels,
            IReadOnlyList<string> level2Labels,
            float scale)
        {
            if (selected is not { } column)
            {
                return;
            }

            int columnIndex = Math.Clamp(
                (int)((long)(column.Left - root.Left) * GridMath.Level1Columns / Math.Max(1, root.Width)),
                0,
                GridMath.Level1Columns - 1);
            Color selectedTint = ColumnTints[(columnIndex / 2) % ColumnTints.Length];

            for (int i = 0; i < GridMath.Level1Columns; i++)
            {
                var cell = GridMath.Cell(root, i, 0, GridMath.Level1Columns, 1);
                Color tint = ColumnTints[(i / 2) % ColumnTints.Length];
                using var fill = new SolidBrush(WithAlpha(tint, 24));
                g.FillRectangle(fill, cell);
            }

            using (var fill = new LinearGradientBrush(
                       column,
                       WithAlpha(selectedTint, 86),
                       WithAlpha(selectedTint, 138),
                       LinearGradientMode.Vertical))
            {
                g.FillRectangle(fill, column);
            }

            DrawGridLines(g, root, GridMath.Level1Columns, 1, scale);

            // Faint level-1 letters keep the column map visible around the
            // selected column, like Mouseless does.
            if (level1Labels.Count >= GridMath.Level1Cells)
            {
                float columnFontSize = Math.Clamp(column.Width * 0.34f, 16f, 76f);
                using var columnFont = new Font("Segoe UI", columnFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var faintFormat = CenteredFormat();
                using var faint = new SolidBrush(Color.FromArgb(72, 255, 255, 255));
                float faintOffset = Math.Max(1.5f, 2f * scale);

                for (int i = 0; i < GridMath.Level1Columns; i++)
                {
                    if (i == columnIndex || string.IsNullOrEmpty(level1Labels[i]))
                    {
                        continue;
                    }

                    var cell = GridMath.Cell(root, i, 0, GridMath.Level1Columns, 1);
                    DrawShadowedString(
                        g,
                        level1Labels[i],
                        columnFont,
                        faint,
                        cell.Left + (cell.Width / 2f),
                        cell.Top + (cell.Height * 0.5f),
                        faintOffset,
                        faintFormat,
                        shadowAlpha: 90);
                }
            }

            using (var border = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1.5f, 2f * scale)))
            {
                g.DrawRectangle(border, column);
            }

            if (level2Labels.Count < GridMath.Level2Cells)
            {
                return;
            }

            float bandHeight = column.Height / (float)GridMath.Level2Rows;
            float fontSize = Math.Clamp(Math.Min(column.Width * 0.26f, bandHeight * 0.62f), 9f, 30f);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var text = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            using var pill = new SolidBrush(Color.FromArgb(112, 9, 14, 24));
            using var pillLine = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, scale));
            float offset = Math.Max(1f, 1.4f * scale);

            for (int row = 0; row < GridMath.Level2Rows; row++)
            {
                string label = level2Labels[row];
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                var band = GridMath.Cell(column, 0, row, 1, GridMath.Level2Rows);
                float cx = band.Left + (band.Width / 2f);
                float cy = band.Top + (band.Height / 2f);

                var size = g.MeasureString(label, font);
                float pillWidth = Math.Max(font.Size * 1.15f, size.Width + (font.Size * 0.3f));
                float pillHeight = Math.Min(band.Height, Math.Max(font.Size * 1.2f, size.Height * 0.9f));
                using (var path = RoundedRect(
                           cx - (pillWidth / 2f),
                           cy - (pillHeight / 2f),
                           pillWidth,
                           pillHeight,
                           Math.Min(pillHeight / 2.4f, font.Size * 0.24f)))
                {
                    g.FillPath(pill, path);
                    g.DrawPath(pillLine, path);
                }

                DrawShadowedString(g, label, font, text, cx, cy, offset, format, shadowAlpha: 150);

                if (row is 9 or 19)
                {
                    using var groupLine = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, scale));
                    g.DrawLine(groupLine, band.Left, band.Bottom, band.Right, band.Bottom);
                }
            }
        }

        private static void DrawSubgrid(
            Graphics g,
            Rectangle root,
            IReadOnlyList<string> labels,
            float scale)
        {
            for (int row = 0; row < GridMath.SubgridRows; row++)
            {
                Color tint = SubgridTints[row % SubgridTints.Length];
                for (int column = 0; column < GridMath.SubgridColumns; column++)
                {
                    var cell = GridMath.Cell(
                        root,
                        column,
                        row,
                        GridMath.SubgridColumns,
                        GridMath.SubgridRows);
                    using var fill = new SolidBrush(WithAlpha(tint, 94));
                    g.FillRectangle(fill, cell);
                }
            }

            DrawGridLines(g, root, GridMath.SubgridColumns, GridMath.SubgridRows, scale);

            if (labels.Count < GridMath.SubgridCells)
            {
                return;
            }

            var sample = GridMath.Cell(root, 0, 0, GridMath.SubgridColumns, GridMath.SubgridRows);
            float fontSize = Math.Clamp(
                Math.Min(sample.Width * 0.38f, sample.Height * 0.42f),
                18f,
                96f);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = CenteredFormat();
            using var brush = new SolidBrush(Color.FromArgb(248, 255, 255, 255));
            float offset = Math.Max(2f, 2.4f * scale);

            for (int i = 0; i < GridMath.SubgridCells; i++)
            {
                string text = labels[i];
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var cell = GridMath.SubRect(root, i, GridMath.SubgridColumns, GridMath.SubgridRows);
                DrawShadowedString(
                    g,
                    text,
                    font,
                    brush,
                    cell.Left + (cell.Width / 2f),
                    cell.Top + (cell.Height / 2f),
                    offset,
                    format);
            }
        }

        private static void DrawGridLines(
            Graphics g,
            Rectangle rect,
            int columns,
            int rows,
            float scale)
        {
            using var shadow = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(3f, 2.6f * scale));
            using var line = new Pen(Color.FromArgb(214, 255, 255, 255), Math.Max(1f, 1.15f * scale));

            for (int i = 1; i < columns; i++)
            {
                int x = rect.Left + (int)((long)rect.Width * i / columns);
                g.DrawLine(shadow, x, rect.Top, x, rect.Bottom);
                g.DrawLine(line, x, rect.Top, x, rect.Bottom);
            }

            for (int i = 1; i < rows; i++)
            {
                int y = rect.Top + (int)((long)rect.Height * i / rows);
                g.DrawLine(shadow, rect.Left, y, rect.Right, y);
                g.DrawLine(line, rect.Left, y, rect.Right, y);
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

        private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(width, height));
            var path = new GraphicsPath();
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

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
