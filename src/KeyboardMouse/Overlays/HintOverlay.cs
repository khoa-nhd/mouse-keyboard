using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Core;

namespace KeyboardMouse.Overlays;

/// <summary>
/// One layered window per monitor, showing a small yellow label at the
/// top-left of every clickable element. Labels that no longer match the typed
/// prefix fade instead of disappearing, so the user can still see the screen.
/// </summary>
public sealed class HintOverlay : IHintOverlay
{
    private readonly Dictionary<(IntPtr Handle, Rectangle Bounds), HintForm> _forms = new();

    public void ShowScanning(MonitorInfo monitor, string text)
    {
        Hide();

        var form = GetOrCreate(monitor);
        form.DrawScanning(monitor, text);
        form.ShowOverlay();
    }

    public void Show(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<ClickableTarget> targets,
        IReadOnlyList<string> labels,
        string typed)
    {
        var used = new HashSet<(IntPtr Handle, Rectangle Bounds)>();

        foreach (var monitor in monitors)
        {
            var items = new List<(ClickableTarget Target, string Label)>();

            for (int i = 0; i < targets.Count && i < labels.Count; i++)
            {
                if (targets[i].Bounds.IntersectsWith(monitor.Bounds))
                {
                    items.Add((targets[i], labels[i]));
                }
            }

            var key = (monitor.Handle, monitor.Bounds);
            if (items.Count == 0)
            {
                if (_forms.TryGetValue(key, out var emptyForm))
                {
                    emptyForm.HideOverlay();
                }

                continue;
            }

            var form = GetOrCreate(monitor);
            form.DrawHints(monitor, items, typed);
            form.ShowOverlay();
            used.Add(key);
        }

        foreach (var (key, form) in _forms)
        {
            if (!used.Contains(key))
            {
                form.HideOverlay();
            }
        }
    }

    public void Hide()
    {
        foreach (var form in _forms.Values)
        {
            form.HideOverlay();
        }
    }

    public void Dispose()
    {
        foreach (var form in _forms.Values)
        {
            form.Dispose();
        }

        _forms.Clear();
    }

    private HintForm GetOrCreate(MonitorInfo monitor)
    {
        var key = (monitor.Handle, monitor.Bounds);
        if (!_forms.TryGetValue(key, out var form))
        {
            form = new HintForm();
            _forms[key] = form;
        }

        return form;
    }

    private sealed class HintForm : OverlayForm
    {
        private static readonly Color LabelColor = Color.FromArgb(255, 255, 214, 10);
        private static readonly Color LabelBorder = Color.FromArgb(230, 30, 30, 30);
        private static readonly Color TypedColor = Color.FromArgb(255, 210, 55, 35);

        public void DrawScanning(MonitorInfo monitor, string text)
        {
            RenderMonitor(monitor, g =>
            {
                float scale = (float)monitor.Scale;
                float fontSize = Math.Clamp(13f * scale, 11f, 28f);
                using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                using var textBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
                using var background = new SolidBrush(Color.FromArgb(230, 25, 25, 25));

                var size = g.MeasureString(text, font, int.MaxValue, format);
                float width = size.Width + (26 * scale);
                float height = size.Height + (12 * scale);
                float x = (monitor.Bounds.Width - width) / 2f;
                float y = (monitor.Bounds.Height - height) / 2f;

                var rect = new RectangleF(x, y, width, height);
                using var path = RoundedRect(rect, 8f * scale);
                g.FillPath(background, path);
                g.DrawString(text, font, textBrush, rect, format);
            });
        }

        public void DrawHints(
            MonitorInfo monitor,
            IReadOnlyList<(ClickableTarget Target, string Label)> items,
            string typed)
        {
            RenderMonitor(monitor, g =>
            {
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
                    using (var path = RoundedRect(new RectangleF(x + 2, y + 2, boxWidth, boxHeight), 4f * scale))
                    {
                        g.FillPath(shadow, path);
                    }

                    using (var background = new SolidBrush(Color.FromArgb(alpha, LabelColor)))
                    using (var border = new Pen(Color.FromArgb(alpha, LabelBorder), Math.Max(1f, scale)))
                    using (var path = RoundedRect(box, 4f * scale))
                    {
                        g.FillPath(background, path);
                        g.DrawPath(border, path);
                    }

                    using var textBrush = new SolidBrush(
                        matches ? Color.FromArgb(255, 25, 25, 25) : Color.FromArgb(alpha, 25, 25, 25));

                    if (typed.Length > 0 && label.StartsWith(typed, StringComparison.Ordinal))
                    {
                        float typedWidth = g.MeasureString(label[..typed.Length], font).Width;
                        using var typedBrush = new SolidBrush(Color.FromArgb(255, TypedColor));
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
            });
        }

        private void RenderMonitor(MonitorInfo monitor, Action<Graphics> draw)
        {
            var bounds = monitor.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            RenderSurface(bounds.Width, bounds.Height, bounds.Left, bounds.Top, draw);
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            var path = new GraphicsPath();

            if (diameter <= 1)
            {
                path.AddRectangle(rect);
                return path;
            }

            var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
