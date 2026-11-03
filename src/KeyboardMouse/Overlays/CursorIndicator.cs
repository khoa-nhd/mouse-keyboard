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
    private const int MaxTrailSamples = 500;
    private const float TrailLifeNormal = 1.0f;
    private const float TrailLifeTurbo  = 2.2f;
    private const float MinTrailSpacing = 1.5f;
    private const int InterpSteps = 10;
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
        _timer = new System.Windows.Forms.Timer { Interval = 8 }; // ~125 fps (matches MovementEngine)
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

        if (_style == CursorStyle.Comet)
        {
            CursorPainter.Gap(CursorStyle.Comet, out int gapX, out int gapY);
            float headScreenX = cursor.X + gapX + CursorPainter.CometHeadInset;
            float headScreenY = cursor.Y + gapY + CursorPainter.CometHeadInset;

            if (moved)
            {
                if (_trail.Count > 0)
                {
                    var last = _trail[^1];
                    float dx = headScreenX - last.X;
                    float dy = headScreenY - last.Y;
                    if ((dx * dx) + (dy * dy) >= MinTrailSpacing * MinTrailSpacing)
                    {
                        _trail.Add((headScreenX, headScreenY, now));
                    }
                }
                else
                {
                    _trail.Add((headScreenX, headScreenY, now));
                }

                if (_trail.Count > MaxTrailSamples)
                    _trail.RemoveAt(0);
            }

            _lastX = cursor.X;
            _lastY = cursor.Y;
            _lastColor = color;

            float life = turbo ? TrailLifeTurbo : TrailLifeNormal;
            PruneTrail(now, life);

            float phase = (now % 3600000) / 1000f;

            // Fixed-size sliding window anchored around the comet head (zero resize/reposition flicker)
            int winX = (int)headScreenX - Reach;
            int winY = (int)headScreenY - Reach;
            int winW = Reach * 2;
            int winH = Reach * 2;

            var headLocal = new PointF(Reach, Reach);
            var trail = BuildLocalTrail(headScreenX, headScreenY, winX, winY, now, turbo);

            _form.Render(winX, winY, winW, winH,
                _style, _dotSize, color, phase, headLocal, trail);
        }
        else
        {
            _lastX = cursor.X;
            _lastY = cursor.Y;
            _lastColor = color;

            float phase = (now % 3600000) / 1000f;
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

    private void PruneTrail(long now, float lifeSec)
    {
        long cutoff = (long)(lifeSec * 1000f);
        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            if (now - _trail[i].Tick > cutoff)
            {
                _trail.RemoveRange(0, i + 1);
                return;
            }
        }
    }

    private TrailSample[] BuildLocalTrail(
        float headScreenX, float headScreenY,
        int winX, int winY,
        long now, bool turbo)
    {
        if (_style != CursorStyle.Comet || _trail.Count < 2)
            return Array.Empty<TrailSample>();

        float lifeSec = turbo ? TrailLifeTurbo : TrailLifeNormal;
        float lifeMsF = lifeSec * 1000f;

        var points = new List<(float X, float Y, long Tick)>(_trail.Count * InterpSteps);

        for (int i = 0; i < _trail.Count - 1; i++)
        {
            var p0 = _trail[Math.Max(0, i - 1)];
            var p1 = _trail[i];
            var p2 = _trail[i + 1];
            var p3 = _trail[Math.Min(_trail.Count - 1, i + 2)];

            for (int s = 0; s < InterpSteps; s++)
            {
                float t = s / (float)InterpSteps;
                float x = CatmullRom(p0.X, p1.X, p2.X, p3.X, t);
                float y = CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t);
                long tick = p1.Tick + (long)((p2.Tick - p1.Tick) * t);
                points.Add((x, y, tick));
            }
        }

        points.Add(_trail[^1]);

        var result = new List<TrailSample>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            var (px, py, tick) = points[i];
            float localX = px - winX;
            float localY = py - winY;
            float age = Math.Clamp((now - tick) / lifeMsF, 0f, 1f);
            result.Add(new TrailSample(new PointF(localX, localY), age));
        }

        return result.ToArray();
    }

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
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
