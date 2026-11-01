using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Native;

namespace KeyboardMouse.Overlays;

/// <summary>
/// Small dot near the pointer, visible while mouse mode is on. Its colour
/// shows the active layer, which is the quickest way to confirm that A/S/D
/// and drag lock are engaged. Follows the TrackPoint too, via a 30 Hz timer.
/// </summary>
public sealed class CursorIndicator : IDisposable
{
    private const int DotOffset = 16;
    private const int DotMargin = 4;

    private readonly AppState _state;
    private readonly CursorDotForm _form = new();
    private readonly System.Windows.Forms.Timer _timer;

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

        if (!force &&
            cursor.X == _lastX &&
            cursor.Y == _lastY &&
            color == _lastColor)
        {
            return;
        }

        _lastX = cursor.X;
        _lastY = cursor.Y;
        _lastColor = color;

        _form.RenderDot(cursor.X, cursor.Y, _dotSize, color);

        if (!_shown)
        {
            _form.ShowOverlay();
            _shown = true;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _form.Dispose();
    }

    private sealed class CursorDotForm : OverlayForm
    {
        public void RenderDot(int x, int y, int dotSize, Color color)
        {
            int size = DotOffset + dotSize + DotMargin;
            int left = DotOffset;
            int top = DotOffset;

            RenderSurface(size, size, x, y, g =>
            {
                using var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
                g.FillEllipse(shadow, left + 1.5f, top + 1.5f, dotSize, dotSize);

                using var fill = new SolidBrush(color);
                g.FillEllipse(fill, left, top, dotSize, dotSize);

                using var outline = new Pen(Color.FromArgb(235, 255, 255, 255), 1.2f);
                g.DrawEllipse(outline, left, top, dotSize, dotSize);
            });
        }
    }
}
