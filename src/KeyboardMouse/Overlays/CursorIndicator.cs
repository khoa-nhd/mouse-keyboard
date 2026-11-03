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
    private const float MinTrailSpacing = 3.0f;
    private const float TrailLifeNormal = 1.4f;
    private const float TrailLifeTurbo  = 3.0f;
    private const float MaxTrailDistanceNormal = 280f;
    private const float MaxTrailDistanceTurbo = 330f;
    private const float MaxTrailJump = 240f;
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
            float distanceSq = (dx * dx) + (dy * dy);

            // A jump this large is a teleport (center screen, monitor change,
            // a stall) rather than movement: start a fresh tail instead of
            // drawing one long streak across the screen.
            if (distanceSq > MaxTrailJump * MaxTrailJump)
            {
                _trail.Clear();
            }
            else if (distanceSq < MinTrailSpacing * MinTrailSpacing)
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
        float maxDistance = turbo ? MaxTrailDistanceTurbo : MaxTrailDistanceNormal;

        // Walk the history from newest to oldest. Stop at the first sample
        // that would push the tail past its maximum length: older samples can
        // re-enter the window after a fast U-turn and were what looked like
        // several disconnected trails on screen.
        var newestFirst = new List<TrailSample>(_trail.Count);
        var distances = new List<float>(_trail.Count);
        float lastX = float.NaN;
        float lastY = float.NaN;
        float distance = 0f;

        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            var (px, py, tick) = _trail[i];
            float localX = px - cx + headLocal.X;
            float localY = py - cy + headLocal.Y;

            if (localX < 2f || localY < 2f ||
                localX > (Reach * 2) - 2f || localY > (Reach * 2) - 2f)
            {
                break;
            }

            if (!float.IsNaN(lastX))
            {
                float dx = localX - lastX;
                float dy = localY - lastY;
                distance += MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance > maxDistance)
                {
                    break;
                }
            }

            float timeAge = Math.Clamp((now - tick) / lifeMs, 0f, 1f);
            newestFirst.Add(new TrailSample(new PointF(localX, localY), timeAge));
            distances.Add(distance);
            lastX = localX;
            lastY = localY;
        }

        if (newestFirst.Count == 0)
            return Array.Empty<TrailSample>();

        // Fade by actual trail length as well as age, so a tail cut short by
        // the distance cap still ends in a transparent point instead of a
        // blunt, blocky edge.
        float total = Math.Max(distances[^1], 0.001f);
        var result = new TrailSample[newestFirst.Count];
        for (int i = 0; i < newestFirst.Count; i++)
        {
            int target = newestFirst.Count - 1 - i; // oldest first
            float spatialAge = Math.Clamp(distances[target] / total, 0f, 1f);
            float age = Math.Clamp(Math.Max(newestFirst[target].Age, spatialAge), 0f, 1f);
            result[i] = newestFirst[target] with { Age = age };
        }

        return result;
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
