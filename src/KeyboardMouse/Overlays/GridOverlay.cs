using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// One monitor-sized layered window for grid mode. Dims everything outside the
/// active rectangle, draws the 3×3 lines and the key label in each cell.
/// </summary>
public sealed class GridOverlay : IGridOverlay
{
    private readonly GridOverlayForm _form = new();

    public void Show(
        MonitorInfo monitor,
        Rectangle activeRect,
        IReadOnlyList<string> labels,
        int depth,
        int maxDepth)
    {
        _form.DrawGrid(monitor, activeRect, labels, depth, maxDepth);
        _form.ShowOverlay();
    }

    public void Hide() => _form.HideOverlay();

    public void Dispose() => _form.Dispose();

    private sealed class GridOverlayForm : OverlayForm
    {
        public void DrawGrid(
            MonitorInfo monitor,
            Rectangle activeRect,
            IReadOnlyList<string> labels,
            int depth,
            int maxDepth)
        {
            var bounds = monitor.Bounds;
            int width = bounds.Width;
            int height = bounds.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var active = new Rectangle(
                activeRect.Left - bounds.Left,
                activeRect.Top - bounds.Top,
                activeRect.Width,
                activeRect.Height);

            float scale = (float)monitor.Scale;

            RenderSurface(width, height, bounds.Left, bounds.Top, g =>
            {
                // Dim everything outside the active rectangle.
                using (var dim = new SolidBrush(Color.FromArgb(78, 0, 0, 0)))
                {
                    var outer = new Rectangle(0, 0, width, height);
                    FillOutside(g, dim, outer, active);
                }

                // Grid lines with a dark drop shadow for readability on any UI.
                using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(3f, 3f * scale));
                using var line = new Pen(Color.FromArgb(245, 255, 255, 255), Math.Max(1.5f, 1.5f * scale));

                for (int i = 1; i < GridMath.Columns; i++)
                {
                    int x = active.Left + (int)((long)active.Width * i / GridMath.Columns);
                    g.DrawLine(shadow, x, active.Top, x, active.Bottom);
                    g.DrawLine(line, x, active.Top, x, active.Bottom);
                }

                for (int i = 1; i < GridMath.Rows; i++)
                {
                    int y = active.Top + (int)((long)active.Height * i / GridMath.Rows);
                    g.DrawLine(shadow, active.Left, y, active.Right, y);
                    g.DrawLine(line, active.Left, y, active.Right, y);
                }

                // Active rectangle frame.
                g.DrawRectangle(shadow, active);
                g.DrawRectangle(line, active);

                if (labels.Count < GridMath.Cells)
                {
                    return;
                }

                float fontSize = Math.Clamp(22f * scale, 16f, 72f);
                using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textShadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
                using var textBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                float offset = Math.Max(1f, 1.5f * scale);

                for (int i = 0; i < GridMath.Cells; i++)
                {
                    var cell = GridMath.SubRect(active, i);
                    var center = new PointF(
                        cell.Left + (cell.Width / 2f),
                        cell.Top + (cell.Height / 2f));

                    g.DrawString(labels[i], font, textShadow, center.X + offset, center.Y + offset, format);
                    g.DrawString(labels[i], font, textBrush, center.X, center.Y, format);
                }
            });
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
}
