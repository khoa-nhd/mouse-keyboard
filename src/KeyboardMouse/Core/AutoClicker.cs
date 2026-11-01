using KeyboardMouse.Config;
using KeyboardMouse.Output;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Core;

public enum AutoClickButton
{
    Left,
    Right,
    Middle
}

/// <summary>
/// Clicks repeatedly at wherever the pointer currently is, so the user can keep
/// moving with IJKL (or the TrackPoint) while it runs. Toggled with B in mouse
/// mode; stopped by leaving mouse mode, panic, session lock and suspend.
///
/// Clicks fire on a timer thread. MouseOutput serializes SendInput under its
/// own lock, the same way the movement thread uses it.
/// </summary>
public sealed class AutoClicker : IDisposable
{
    public const int MinIntervalMs = 10;
    public const int MaxIntervalMs = 60_000;

    private readonly object _gate = new();
    private readonly AppState _state;
    private readonly IMouseOutput _mouse;
    private readonly Func<int, Action, IDisposable> _schedule;
    private IDisposable? _timer;
    private int _intervalMs = 100;
    private AutoClickButton _button = AutoClickButton.Left;

    /// <param name="schedule">Starts a periodic callback (interval in ms) and
    /// returns a handle that stops it. Defaults to a System.Threading.Timer;
    /// tests pass a fake to drive ticks by hand.</param>
    public AutoClicker(
        AppState state,
        IMouseOutput mouse,
        AutoClickSettings? settings = null,
        Func<int, Action, IDisposable>? schedule = null)
    {
        _state = state;
        _mouse = mouse;
        _schedule = schedule ?? StartThreadingTimer;
        UpdateSettings(settings ?? new AutoClickSettings());
    }

    public event Action? Changed;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _timer is not null;
            }
        }
    }

    public void UpdateSettings(AutoClickSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool restart;
        lock (_gate)
        {
            _intervalMs = Math.Clamp(settings.IntervalMs, MinIntervalMs, MaxIntervalMs);
            _button = ParseButton(settings.Button);
            restart = _timer is not null;
        }

        if (restart)
        {
            // Pick up the new interval immediately.
            Stop();
            Start();
        }
    }

    public void Toggle()
    {
        if (IsRunning)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null)
            {
                return;
            }

            _timer = _schedule(_intervalMs, Tick);
        }

        _state.SetAutoClicking(true);
        Changed?.Invoke();
    }

    public void Stop()
    {
        IDisposable? timer;
        lock (_gate)
        {
            timer = _timer;
            _timer = null;
        }

        if (timer is null)
        {
            return;
        }

        timer.Dispose();
        _state.SetAutoClicking(false);
        Changed?.Invoke();
    }

    /// <summary>One click with the configured button (timer callback).</summary>
    public void Tick()
    {
        AutoClickButton button;
        lock (_gate)
        {
            if (_timer is null)
            {
                return; // a late tick after Stop
            }

            button = _button;
        }

        switch (button)
        {
            case AutoClickButton.Right:
                _mouse.RightClick();
                break;
            case AutoClickButton.Middle:
                _mouse.MiddleClick();
                break;
            default:
                _mouse.LeftClick();
                break;
        }
    }

    public void Dispose() => Stop();

    public static AutoClickButton ParseButton(string? name) =>
        Enum.TryParse<AutoClickButton>(name, ignoreCase: true, out var button) &&
        Enum.IsDefined(button)
            ? button
            : AutoClickButton.Left;

    private static IDisposable StartThreadingTimer(int intervalMs, Action tick) =>
        new System.Threading.Timer(
            _ =>
            {
                // An exception on a thread-pool timer would kill the process.
                try
                {
                    tick();
                }
                catch (Exception ex)
                {
                    Log.Error("Auto-click tick failed", ex);
                }
            },
            null,
            intervalMs,
            intervalMs);
}
