using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Native;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Indicator that follows the pointer while mouse mode is on. Its colour shows
/// the active layer, which is the quickest way to confirm that A/S/D and drag
/// lock are engaged. The shape is chosen with <see cref="CursorStyle"/>; some
/// styles animate, so the 30 Hz timer also drives motion. Follows the
/// TrackPoint too, because it reads the real cursor position every tick.
/// </summary>
public sealed class CursorIndicator : IDisposable
{
    private const int MaxTrail = 14;
    private const int TrailSpacing = 3;

    private readonly AppState _state;
    private readonly CursorForm _form = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<PointF> _trail = new();

    private CursorStyle _style = CursorStyle.Dot;
    private int _dotSize = 12;
    private bool _enabled;
    private bool _shown;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private Color _lastColor;

    public CursorIndicator(AppState state)
    {
        _state = state;
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) => UpdatePosition(force: false);
    }

    public void SetDotSize(int size)
    {
        int clamped = Math.Clamp(size, 4, 48);
        if (_dotSize == clamped)
        {
            return;
        }

        _dotSize = clamped;
        _lastX = int.MinValue;
        _trail.Clear();
        if (_enabled)
        {
            UpdatePosition(force: true);
        }
    }

    public void SetStyle(CursorStyle style)
    {
        if (_style == style)
        {
            return;
        }

        _style = style;
        _lastX = int.MinValue;
        _trail.Clear();
        if (_enabled)
        {
            UpdatePosition(force: true);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;

        if (enabled)
        {
            _shown = false;
            _lastX = int.MinValue;
            _trail.Clear();
            _timer.Start();
            UpdatePosition(force: true);
        }
        else
        {
            _timer.Stop();
            _form.HideOverlay();
            _shown = false;
        }
    }

    private void UpdatePosition(bool force)
    {
        if (!_enabled || !User32.GetCursorPos(out var cursor))
        {
            return;
        }

        var feedback = _state.GetFeedback();
        Color color = feedback.AutoClicking
            ? Color.FromArgb(255, 241, 196, 15)   // yellow: auto clicker running
            : feedback.DragLock
            ? Color.FromArgb(255, 230, 126, 34)   // orange: drag lock
            : feedback.Scroll
                ? Color.FromArgb(255, 52, 152, 219)  // blue: scroll layer
                : feedback.Precision
                    ? Color.FromArgb(255, 155, 89, 182) // purple: precision
                    : feedback.Turbo
                        ? Color.FromArgb(255, 231, 76, 60) // red: turbo
                        : Color.FromArgb(255, 46, 204, 113); // green: normal

        bool moved = cursor.X != _lastX || cursor.Y != _lastY;
        if (!force && !moved && color == _lastColor && !CursorPainter.IsAnimated(_style))
        {
            return;
        }

        if (moved)
        {
            TrackTrail(cursor.X, cursor.Y);
        }

        _lastX = cursor.X;
        _lastY = cursor.Y;
        _lastColor = color;

        var visual = CursorPainter.Measure(_style, _dotSize);
        var pointer = new PointF(-visual.OffsetX, -visual.OffsetY);
        float phase = (Environment.TickCount64 % 3600000) / 1000f;

        _form.Render(visual, _style, _dotSize, color, phase, pointer, BuildLocalTrail(pointer), cursor);

        if (!_shown)
        {
            _form.ShowOverlay();
            _shown = true;
        }
    }

    /// <summary>Keeps a short history of pointer samples for the comet tail.</summary>
    private void TrackTrail(int x, int y)
    {
        if (_style != CursorStyle.Comet)
        {
            return;
        }

        if (_trail.Count > 0)
        {
            var last = _trail[^1];
            float dx = x - last.X;
            float dy = y - last.Y;
            if ((dx * dx) + (dy * dy) < TrailSpacing * TrailSpacing)
            {
                return;
            }
        }

        _trail.Add(new PointF(x, y));
        if (_trail.Count > MaxTrail)
        {
            _trail.RemoveAt(0);
        }
    }

    private PointF[] BuildLocalTrail(PointF pointer)
    {
        if (_style != CursorStyle.Comet || _trail.Count == 0)
        {
            return Array.Empty<PointF>();
        }

        var local = new PointF[_trail.Count];
        for (int i = 0; i < _trail.Count; i++)
        {
            // _lastX/_lastY match the newest sample; translate into surface space.
            local[i] = new PointF(_trail[i].X - _lastX + pointer.X, _trail[i].Y - _lastY + pointer.Y);
        }

        return local;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _form.Dispose();
    }

    private sealed class CursorForm : OverlayForm
    {
        public void Render(
            CursorVisual visual,
            CursorStyle style,
            int size,
            Color color,
            float phase,
            PointF pointer,
            IReadOnlyList<PointF> trail,
            POINT cursor)
        {
            RenderSurface(
                visual.Width,
                visual.Height,
                cursor.X + visual.OffsetX,
                cursor.Y + visual.OffsetY,
                g => CursorPainter.Paint(g, style, size, color, phase, pointer, trail));
        }
    }
}
