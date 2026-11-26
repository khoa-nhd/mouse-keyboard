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

    /// <summary>How the labels look; applied from settings, used on the next draw.</summary>
    public HintTheme Theme { get; set; } = HintTheme.Classic;

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
        // Dispose the windows of monitors that are gone or changed size: each
        // form carries a monitor-sized bitmap and a native window handle that
        // only the matching key can reuse.
        var currentKeys = new HashSet<(IntPtr Handle, Rectangle Bounds)>();
        foreach (var monitor in monitors)
        {
            currentKeys.Add((monitor.Handle, monitor.Bounds));
        }

        foreach (var key in _forms.Keys.Where(k => !currentKeys.Contains(k)).ToList())
        {
            _forms[key].Dispose();
            _forms.Remove(key);
        }

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
            form.DrawHints(monitor, items, typed, Theme);
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
            string typed,
            HintTheme theme)
        {
            RenderMonitor(monitor, g => HintLabelPainter.Draw(g, monitor, items, typed, theme));
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
