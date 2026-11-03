using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Native;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Indicator that follows the pointer while mouse mode is on. Its colour shows
/// the active layer, which is the quickest way to confirm that A/S/D and drag
/// lock are engaged. The shape is chosen with <see cref="CursorStyle"/>; some
/// styles animate, so the timer also drives motion. Follows the
/// TrackPoint too, because it reads the real cursor position every tick.
/// </summary>
public sealed class CursorIndicator : IDisposable
{
    private const int MaxTrailNormal = 180;
    private const int MaxTrailTurbo = 350;
    private const float MinTrailSpacing = 0.8f;
    private const float TrailLifeNormal = 1.4f;
    private const float TrailLifeTurbo  = 3.0f;
    private const int Reach = 350; // Fixed window half-size (700x700 total window, zero resize flicker)

    private readonly AppState _state;
    private readonly CursorForm _form = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<(float X, float Y, long Tick)> _trail = new();

    private CursorStyle _style = CursorStyle.Dot;
    private int _dotSize = 12;
    private CursorPalette _palette = CursorPalette.Default;
    private bool _enabled;
    private bool _shown;
    private float _lastX = float.MinValue;
    private float _lastY = float.MinValue;
    private Color _lastColor;

    public CursorIndicator(AppState state)
    {
        _state = state;
        _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 fps
        _timer.Tick += (_, _) => UpdatePosition(force: false);
    }

    public void SetDotSize(int size)
    {
        int clamped = Math.Clamp(size, 4, 48);
        if (_dotSize == clamped) return;
        _dotSize = clamped;
        _lastX = float.MinValue;
        _trail.Clear();
        if (_enabled) UpdatePosition(force: true);
    }

    public void SetPalette(CursorPalette palette)
    {
        if (palette == _palette) return;
        _palette = palette;
        _lastColor = Color.Empty;
        if (_enabled) UpdatePosition(force: true);
    }

    public void SetStyle(CursorStyle style)
    {
        if (_style == style) return;
        _style = style;
        _lastX = float.MinValue;
        _trail.Clear();
        if (_enabled) UpdatePosition(force: true);
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (enabled)
        {
            _shown = false;
            _lastX = float.MinValue;
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
        if (!_enabled || !User32.GetCursorPos(out var cursor)) return;

        var feedback = _state.GetFeedback();
        Color color = feedback.AutoClicking ? _palette.AutoClick
            : feedback.DragLock  ? _palette.DragLock
            : feedback.Scroll    ? _palette.Scroll
            : feedback.Precision ? _palette.Precision
            : feedback.Turbo     ? _palette.Turbo
            : _palette.Normal;

        bool moved = Math.Abs(cursor.X - _lastX) > 0.1f || Math.Abs(cursor.Y - _lastY) > 0.1f;
        if (!force && !moved && color == _lastColor && !CursorPainter.IsAnimated(_style))
            return;

        long now = Environment.TickCount64;
        bool turbo = feedback.Turbo;

        if (moved && _style == CursorStyle.Comet)
        {
            TrackTrail(cursor.X, cursor.Y, now, turbo);
        }

        if (_style == CursorStyle.Comet)
        {
            PruneTrail(now, turbo);
        }

        _lastX = cursor.X;
        _lastY = cursor.Y;
        _lastColor = color;

        float phase = (now % 3600000) / 1000f;

        if (_style == CursorStyle.Comet)
        {
            CursorPainter.Gap(CursorStyle.Comet, out int gapX, out int gapY);
            int winX = (int)(cursor.X + gapX + CursorPainter.CometHeadInset) - Reach;
            int winY = (int)(cursor.Y + gapY + CursorPainter.CometHeadInset) - Reach;
            int winW = Reach * 2;
            int winH = Reach * 2;

            var headLocal = new PointF(Reach, Reach);
            var trail = BuildLocalTrail(cursor.X, cursor.Y, headLocal, now, turbo);

            _form.Render(winX, winY, winW, winH,
                _style, _dotSize, color, phase, headLocal, trail);
        }
        else
        {
            var visual = CursorPainter.Measure(_style, _dotSize);
            var pointer = new PointF(-visual.OffsetX, -visual.OffsetY);
            _form.Render(visual, _style, _dotSize, color, phase, pointer, Array.Empty<TrailSample>(), cursor);
        }

        if (!_shown)
        {
            _form.ShowOverlay();
            _shown = true;
        }
    }

    private void TrackTrail(float x, float y, long now, bool turbo)
    {
        if (_trail.Count > 0)
        {
            var last = _trail[^1];
            float dx = x - last.X;
            float dy = y - last.Y;
            if ((dx * dx) + (dy * dy) < MinTrailSpacing * MinTrailSpacing)
            {
                return;
            }
        }

        _trail.Add((x, y, now));
        int max = turbo ? MaxTrailTurbo : MaxTrailNormal;
        if (_trail.Count > max)
        {
            _trail.RemoveAt(0);
        }
    }

    private void PruneTrail(long now, bool turbo)
    {
        float life = turbo ? TrailLifeTurbo : TrailLifeNormal;
        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            if (now - _trail[i].Tick > life * 1000f)
            {
                _trail.RemoveAt(i);
            }
        }
    }

    private TrailSample[] BuildLocalTrail(
        float cx, float cy,
        PointF headLocal,
        long now, bool turbo)
    {
        if (_style != CursorStyle.Comet || _trail.Count == 0)
            return Array.Empty<TrailSample>();

        float life = turbo ? TrailLifeTurbo : TrailLifeNormal;
        float lifeMs = life * 1000f;

        var result = new List<TrailSample>(_trail.Count);
        for (int i = 0; i < _trail.Count; i++)
        {
            var (px, py, tick) = _trail[i];
            float localX = px - cx + headLocal.X;
            float localY = py - cy + headLocal.Y;
            float age = Math.Clamp((now - tick) / lifeMs, 0f, 1f);
            result.Add(new TrailSample(new PointF(localX, localY), age));
        }

        return result.ToArray();
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
            IReadOnlyList<TrailSample> trail,
            POINT cursor)
        {
            CursorPainter.Gap(style, out int gapX, out int gapY);
            RenderSurface(
                visual.Width,
                visual.Height,
                cursor.X + visual.OffsetX + gapX,
                cursor.Y + visual.OffsetY + gapY,
                g => CursorPainter.Paint(g, style, size, color, phase, pointer, trail));
        }

        public void Render(
            int winX, int winY, int winW, int winH,
            CursorStyle style,
            int size,
            Color color,
            float phase,
            PointF headLocal,
            IReadOnlyList<TrailSample> trail)
        {
            RenderSurface(
                winW, winH, winX, winY,
                g => CursorPainter.Paint(g, style, size, color, phase, headLocal, trail));
        }
    }
}
