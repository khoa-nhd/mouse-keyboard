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
    private const int MaxTrailNormal = 180;
    private const int MaxTrailTurbo = 350;
    private const float MinTrailSpacing = 0.5f;

    /// <summary>How long a comet sample stays visible, in seconds.</summary>
    private const float TrailLifeNormal = 1.8f;
    private const float TrailLifeTurbo = 3.2f;

    private readonly AppState _state;
    private readonly CursorForm _form = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<(int X, int Y, long Tick)> _trail = new();

    private CursorStyle _style = CursorStyle.Dot;
    private int _dotSize = 12;
    private CursorPalette _palette = CursorPalette.Default;
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

    /// <summary>One colour per state; the active layer decides which one is drawn.</summary>
    public void SetPalette(CursorPalette palette)
    {
        if (palette == _palette)
        {
            return;
        }

        _palette = palette;
        _lastColor = Color.Empty;
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
            ? _palette.AutoClick
            : feedback.DragLock
                ? _palette.DragLock
                : feedback.Scroll
                    ? _palette.Scroll
                    : feedback.Precision
                        ? _palette.Precision
                        : feedback.Turbo
                            ? _palette.Turbo
                            : _palette.Normal;

        bool moved = cursor.X != _lastX || cursor.Y != _lastY;
        if (!force && !moved && color == _lastColor && !CursorPainter.IsAnimated(_style))
        {
            return;
        }

        long now = Environment.TickCount64;
        bool turbo = feedback.Turbo;

        if (moved)
        {
            TrackTrail(cursor.X, cursor.Y, now, turbo);
        }

        // Comet samples age out even while the pointer rests, so the tail
        // dissolves instead of freezing on screen.
        if (_style == CursorStyle.Comet)
        {
            PruneTrail(now, turbo);
        }

        _lastX = cursor.X;
        _lastY = cursor.Y;
        _lastColor = color;

        var visual = CursorPainter.Measure(_style, _dotSize);
        var pointer = new PointF(-visual.OffsetX, -visual.OffsetY);
        float phase = (now % 3600000) / 1000f;

        _form.Render(visual, _style, _dotSize, color, phase, pointer, BuildLocalTrail(pointer, now, turbo), cursor);

        if (!_shown)
        {
            _form.ShowOverlay();
            _shown = true;
        }
    }

    /// <summary>Keeps a short history of pointer samples for the comet tail.</summary>
    private void TrackTrail(int x, int y, long now, bool turbo)
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

    private TrailSample[] BuildLocalTrail(PointF pointer, long now, bool turbo)
    {
        if (_style != CursorStyle.Comet || _trail.Count == 0)
        {
            return Array.Empty<TrailSample>();
        }

        // Samples are pointer positions; the tail must follow the head, which
        // is drawn at pointer + CometHeadInset. Without this shift the tail
        // hangs off the cursor instead of trailing the head.
        var head = new PointF(pointer.X + CursorPainter.CometHeadInset, pointer.Y + CursorPainter.CometHeadInset);
        float life = turbo ? TrailLifeTurbo : TrailLifeNormal;

        var local = new TrailSample[_trail.Count];
        for (int i = 0; i < _trail.Count; i++)
        {
            // _lastX/_lastY match the newest sample; translate into surface space.
            local[i] = new TrailSample(
                new PointF(_trail[i].X - _lastX + head.X, _trail[i].Y - _lastY + head.Y),
                Math.Clamp((now - _trail[i].Tick) / (life * 1000f), 0f, 1f));
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
    }
}
