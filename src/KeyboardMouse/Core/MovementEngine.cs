using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using KeyboardMouse.Native;
using KeyboardMouse.Output;

namespace KeyboardMouse.Core;

/// <summary>
/// Drives pointer movement or scrolling on its own thread while a direction
/// key is held. Uses a high-resolution waitable timer (~125 Hz) and falls back
/// to a 1 ms system timer on older Windows. Idle state blocks on an event, so
/// the thread costs nothing when mouse mode is not moving.
///
/// Each tick also adopts any movement made by a physical mouse / TrackPoint,
/// so both devices coexist instead of fighting each other.
/// </summary>
public sealed class MovementEngine : IMovementControl, IDisposable
{
    private const int TickMilliseconds = 8;
    private const double MaxFrameSeconds = 0.05;
    private const int ResyncThresholdPixels = 2;
    private const double ScrollPrecisionSpeed = 100.0;
    private const double ScrollTapStepSmooth = 30.0;

    private readonly IMouseOutput _output;
    private readonly IMonitorService? _monitors;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Thread _thread;
    private readonly WheelAccumulator _wheelVertical = new();
    private readonly WheelAccumulator _wheelHorizontal = new();

    private SpeedProfile _moveProfile;
    private SpeedProfile _scrollProfile;
    private bool _smoothScroll;
    private bool _scrollPrecision;
    private MovementMode _mode = MovementMode.Move;
    private Direction _directions;
    private double _moveStartSeconds;
    private double _posX;
    private double _posY;
    private int _lastSentX;
    private int _lastSentY;
    private bool _hasSentPosition;
    private double _lastTick;
    private volatile bool _disposed;

    public MovementEngine(IMouseOutput output, SpeedProfiles profiles, IMonitorService? monitors = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(profiles);

        _output = output;
        _monitors = monitors;
        _moveProfile = profiles.Normal;
        _scrollProfile = profiles.Scroll;
        _smoothScroll = profiles.SmoothScroll;

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "KeyboardMouse.Movement"
        };
        _thread.Start();
    }

    public void SetDirection(Direction direction, bool down)
    {
        if (direction == Direction.None)
        {
            return;
        }

        bool nudge;
        lock (_gate)
        {
            var old = _directions;
            _directions = down ? old | direction : old & ~direction;

            nudge = down && (old & direction) == 0;
            if (nudge && old == Direction.None)
            {
                // A fresh sequence always starts at the base speed, even if the
                // previous movement ended a moment ago.
                _moveStartSeconds = _clock.Elapsed.TotalSeconds;
                _wheelVertical.Reset();
                _wheelHorizontal.Reset();
                _hasSentPosition = false;
            }
        }

        if (nudge)
        {
            Nudge(direction);
        }

        Wake();
    }

    public void ClearDirections()
    {
        lock (_gate)
        {
            _directions = Direction.None;
            _wheelVertical.Reset();
            _wheelHorizontal.Reset();
        }

        Wake();
    }

    public void SetMode(MovementMode mode)
    {
        lock (_gate)
        {
            if (_mode == mode)
            {
                return;
            }

            _mode = mode;
            _moveStartSeconds = _clock.Elapsed.TotalSeconds;
            _wheelVertical.Reset();
            _wheelHorizontal.Reset();
        }

        Wake();
    }

    public void SetMoveProfile(SpeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            _moveProfile = profile;
            _moveStartSeconds = _clock.Elapsed.TotalSeconds;
        }

        Wake();
    }

    public void SetScrollProfile(SpeedProfile profile, bool smooth)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            _scrollProfile = profile;
            _smoothScroll = smooth;
            _moveStartSeconds = _clock.Elapsed.TotalSeconds;
            _wheelVertical.Reset();
            _wheelHorizontal.Reset();
        }

        Wake();
    }

    public void SetScrollPrecision(bool precision)
    {
        lock (_gate)
        {
            if (_scrollPrecision == precision)
            {
                return;
            }

            _scrollPrecision = precision;
            _moveStartSeconds = _clock.Elapsed.TotalSeconds;
        }

        Wake();
    }

    /// <summary>
    /// Wakes the engine thread. Safe during shutdown: disposing under a caller
    /// would throw, so a disposed or disposing event is simply left alone.
    /// </summary>
    private void Wake()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Dispose is already running; the thread is going down anyway.
        }
    }

    /// <summary>Immediate tap step, so a single press always moves or scrolls a little.</summary>
    private void Nudge(Direction direction)
    {
        lock (_gate)
        {
            if (!User32.GetCursorPos(out var cursor))
            {
                return;
            }

            var (dx, dy) = direction.ToUnitVector();

            if (_mode == MovementMode.Scroll)
            {
                double step = _smoothScroll ? ScrollTapStepSmooth : WheelAccumulator.WheelDelta;

                if (dy != 0)
                {
                    int delta = _wheelVertical.Add(-dy * step, _smoothScroll);
                    if (delta != 0)
                    {
                        _output.Wheel(delta);
                    }
                }

                if (dx != 0)
                {
                    int delta = _wheelHorizontal.Add(dx * step, _smoothScroll);
                    if (delta != 0)
                    {
                        _output.HorizontalWheel(delta);
                    }
                }

                return;
            }

            double scale = ScaleAt(cursor.X, cursor.Y);
            _posX = cursor.X + (dx * _moveProfile.TapStep * scale);
            _posY = cursor.Y + (dy * _moveProfile.TapStep * scale);
            Clamp(ref _posX, ref _posY);

            int nx = (int)Math.Round(_posX);
            int ny = (int)Math.Round(_posY);
            _lastSentX = nx;
            _lastSentY = ny;
            _hasSentPosition = true;

            if (nx != cursor.X || ny != cursor.Y)
            {
                _output.MoveTo(nx, ny);
            }
        }
    }

    private void Loop()
    {
        IntPtr timer = IntPtr.Zero;
        bool highResolutionPeriod = false;
        bool motionActive = false;

        try
        {
            timer = Kernel32.CreateWaitableTimerExW(
                IntPtr.Zero,
                null,
                Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                Kernel32.TIMER_ALL_ACCESS);

            IntPtr wakeHandle = _wake.SafeWaitHandle.DangerousGetHandle();
            IntPtr[] handles = timer != IntPtr.Zero
                ? [timer, wakeHandle]
                : [wakeHandle];

            while (!_disposed)
            {
                bool active;
                lock (_gate)
                {
                    active = _directions != Direction.None;
                }

                if (!active)
                {
                    motionActive = false;

                    if (timer != IntPtr.Zero)
                    {
                        Kernel32.CancelWaitableTimer(timer);
                    }

                    if (highResolutionPeriod)
                    {
                        Winmm.timeEndPeriod(1);
                        highResolutionPeriod = false;
                    }

                    _wake.WaitOne();
                    continue;
                }

                if (!motionActive)
                {
                    StartMotion();
                    motionActive = true;

                    if (timer == IntPtr.Zero)
                    {
                        // Fallback path: ask the scheduler for 1 ms sleeps.
                        Winmm.timeBeginPeriod(1);
                        highResolutionPeriod = true;
                    }
                }

                WaitForTick(timer, handles);
                if (_disposed)
                {
                    break;
                }

                double now = _clock.Elapsed.TotalSeconds;
                double dt = now - _lastTick;
                _lastTick = now;

                if (dt <= 0)
                {
                    continue;
                }

                if (dt > MaxFrameSeconds)
                {
                    dt = MaxFrameSeconds;
                }

                Tick(dt, now);
            }
        }
        finally
        {
            if (timer != IntPtr.Zero)
            {
                Kernel32.CloseHandle(timer);
            }

            if (highResolutionPeriod)
            {
                Winmm.timeEndPeriod(1);
            }
        }
    }

    private void StartMotion()
    {
        lock (_gate)
        {
            _moveStartSeconds = _clock.Elapsed.TotalSeconds;
            _lastTick = _moveStartSeconds;
            _wheelVertical.Reset();
            _wheelHorizontal.Reset();

            if (User32.GetCursorPos(out var cursor))
            {
                _posX = cursor.X;
                _posY = cursor.Y;
                _lastSentX = cursor.X;
                _lastSentY = cursor.Y;
                _hasSentPosition = true;
            }
        }
    }

    private void WaitForTick(IntPtr timer, IntPtr[] handles)
    {
        if (timer == IntPtr.Zero)
        {
            _wake.WaitOne(TickMilliseconds);
            return;
        }

        long due = -(TickMilliseconds * 10_000L);
        if (!Kernel32.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            _wake.WaitOne(TickMilliseconds);
            return;
        }

        uint result = Kernel32.WaitForMultipleObjects(
            (uint)handles.Length,
            handles,
            false,
            Kernel32.INFINITE);

        if (result == Kernel32.WAIT_OBJECT_0 + 1)
        {
            // Woken by a key event, not the timer. Disarm so the old due time
            // cannot fire a second time while we are already awake.
            Kernel32.CancelWaitableTimer(timer);
        }
        else if (result == uint.MaxValue)
        {
            // WAIT_FAILED: do not spin at 100% CPU.
            _wake.WaitOne(1);
        }
    }

    private void Tick(double dt, double now)
    {
        lock (_gate)
        {
            Direction directions = _directions;
            if (directions == Direction.None)
            {
                return;
            }

            var (ux, uy) = directions.ToNormalizedVector();
            if (ux == 0 && uy == 0)
            {
                return;
            }

            double elapsed = Math.Max(0, now - _moveStartSeconds);

            if (_mode == MovementMode.Scroll)
            {
                double speed = _scrollPrecision
                    ? ScrollPrecisionSpeed
                    : _scrollProfile.VelocityAt(elapsed);

                int vertical = _wheelVertical.Add(-uy * speed * dt, _smoothScroll);
                if (vertical != 0)
                {
                    _output.Wheel(vertical);
                }

                int horizontal = _wheelHorizontal.Add(ux * speed * dt, _smoothScroll);
                if (horizontal != 0)
                {
                    _output.HorizontalWheel(horizontal);
                }

                return;
            }

            double scale = ScaleAt((int)Math.Round(_posX), (int)Math.Round(_posY));
            double velocity = _moveProfile.VelocityAt(elapsed) * scale;

            _posX += ux * velocity * dt;
            _posY += uy * velocity * dt;

            // TrackPoint / real mouse re-sync: if the physical pointer is
            // somewhere we did not put it, adopt its position instead of
            // fighting it.
            if (_hasSentPosition && User32.GetCursorPos(out var cursor) &&
                (Math.Abs(cursor.X - _lastSentX) > ResyncThresholdPixels ||
                 Math.Abs(cursor.Y - _lastSentY) > ResyncThresholdPixels))
            {
                _posX = cursor.X;
                _posY = cursor.Y;
            }

            Clamp(ref _posX, ref _posY);

            int nx = (int)Math.Round(_posX);
            int ny = (int)Math.Round(_posY);
            if (!_hasSentPosition || nx != _lastSentX || ny != _lastSentY)
            {
                _output.MoveTo(nx, ny);
                _lastSentX = nx;
                _lastSentY = ny;
                _hasSentPosition = true;
            }
        }
    }

    private double ScaleAt(int x, int y) =>
        _monitors?.GetScaleAtPoint(new Point(x, y)) ?? 1.0;

    private void Clamp(ref double x, ref double y)
    {
        if (_monitors is not null)
        {
            var (cx, cy) = _monitors.ClampToDesktop(x, y);
            x = cx;
            y = cy;
            return;
        }

        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        int height = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        x = Math.Clamp(x, left, left + width - 1);
        y = Math.Clamp(y, top, top + height - 1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wake.Set();
        // Dispose the event only if the engine thread really stopped: the
        // thread waits on (or cancels) this event, and disposing it under a
        // still-running loop can throw ObjectDisposedException in that thread.
        if (_thread.Join(1000))
        {
            _wake.Dispose();
        }
    }
}
