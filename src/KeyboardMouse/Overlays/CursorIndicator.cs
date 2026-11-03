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
    /// <summary>
    /// Maximum number of screen-space samples to retain. At 33 ms ticks that
    /// is ~6 seconds of history which is more than enough for any tail length.
    /// </summary>
    private const int MaxTrailSamples = 400;

    /// <summary>
    /// Oldest sample age (seconds). Turbo trails are 3× longer than normal.
    /// </summary>
    private const float TrailLifeNormal = 1.4f;
    private const float TrailLifeTurbo  = 3.0f;

    /// <summary>
    /// How many dense interpolated points we place between two raw samples
    /// when building the draw list. More = smoother on fast movement.
    /// </summary>
    private const int InterpSteps = 8;

    private readonly AppState _state;
    private readonly CursorForm _form = new();
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>Raw screen-space samples: (screenX, screenY, timestamp ms).</summary>
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
        _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 fps polling
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

    /// <summary>One colour per state; the active layer decides which one is drawn.</summary>
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
            _trail.Add((cursor.X, cursor.Y, now));
            if (_trail.Count > MaxTrailSamples)
                _trail.RemoveAt(0);
        }

        _lastX = cursor.X;
        _lastY = cursor.Y;
        _lastColor = color;

        if (_style == CursorStyle.Comet)
        {
            // Age out old samples before computing bounds.
            float life = turbo ? TrailLifeTurbo : TrailLifeNormal;
            PruneTrail(now, life);
        }

        // Compute the bounding box of all trail points + head to size the overlay.
        var (windowX, windowY, windowW, windowH, headLocal) =
            ComputeWindow(cursor.X, cursor.Y, now, turbo);

        float phase = (now % 3600000) / 1000f;
        var trail = BuildLocalTrail(cursor.X, cursor.Y, windowX, windowY, now, turbo);

        _form.Render(windowX, windowY, windowW, windowH,
            _style, _dotSize, color, phase, headLocal, trail);

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

    /// <summary>
    /// Returns the overlay window position and size (screen coords), plus
    /// the position of the comet head in that local window space.
    /// </summary>
    private (int wx, int wy, int ww, int wh, PointF headLocal)
        ComputeWindow(float cx, float cy, long now, bool turbo)
    {
        if (_style != CursorStyle.Comet || _trail.Count == 0)
        {
            // Non-comet: use the standard Measure offset.
            var v = CursorPainter.Measure(_style, _dotSize);
            CursorPainter.Gap(_style, out int gx, out int gy);
            int wx = (int)cx + v.OffsetX + gx;
            int wy = (int)cy + v.OffsetY + gy;
            var headLoc = new PointF(-v.OffsetX, -v.OffsetY);
            return (wx, wy, v.Width, v.Height, headLoc);
        }

        // Include head and all trail points in the bounding box.
        float headX = cx + CursorPainter.CometHeadInset + CursorPainter.PointerGapX;
        float headY = cy + CursorPainter.CometHeadInset + CursorPainter.PointerGapY;

        float minX = headX, maxX = headX;
        float minY = headY, maxY = headY;

        foreach (var (tx, ty, _) in _trail)
        {
            // Trail points are in screen space; offset by same gap as head.
            float lx = tx + CursorPainter.PointerGapX;
            float ly = ty + CursorPainter.PointerGapY;
            if (lx < minX) minX = lx;
            if (lx > maxX) maxX = lx;
            if (ly < minY) minY = ly;
            if (ly > maxY) maxY = ly;
        }

        const int Pad = 24; // padding around the bounding box for anti-alias bleed
        int winX = (int)minX - Pad;
        int winY = (int)minY - Pad;
        int winW = (int)(maxX - minX) + Pad * 2;
        int winH = (int)(maxY - minY) + Pad * 2;

        // Clamp to sane size (GDI bitmap limit).
        winW = Math.Max(winW, 40);
        winH = Math.Max(winH, 40);

        // Head position in window-local coordinates.
        var head = new PointF(headX - winX, headY - winY);
        return (winX, winY, winW, winH, head);
    }

    /// <summary>
    /// Converts raw screen-space samples into window-local TrailSamples,
    /// with Catmull-Rom interpolation between each pair of raw samples so
    /// fast motion stays smooth and curved.
    /// </summary>
    private TrailSample[] BuildLocalTrail(
        float cx, float cy,
        int winX, int winY,
        long now, bool turbo)
    {
        if (_style != CursorStyle.Comet || _trail.Count < 2)
            return Array.Empty<TrailSample>();

        float lifeSec = turbo ? TrailLifeTurbo : TrailLifeNormal;
        float lifeMsF = lifeSec * 1000f;

        // Build a dense list using Catmull-Rom spline between raw samples.
        var points = new List<(float X, float Y, long Tick)>(_trail.Count * InterpSteps);

        // Add interpolated points between pairs of raw samples.
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

        // Add the last raw sample.
        points.Add(_trail[^1]);

        // Convert to local-space TrailSamples (oldest first → head last in age order).
        float gapX = CursorPainter.PointerGapX;
        float gapY = CursorPainter.PointerGapY;

        var result = new List<TrailSample>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            var (px, py, tick) = points[i];
            float localX = px + gapX - winX;
            float localY = py + gapY - winY;
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
        /// <summary>Non-comet path: keep existing Measure-based layout.</summary>
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

        /// <summary>Comet path: explicit window position+size computed from trail bounds.</summary>
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
