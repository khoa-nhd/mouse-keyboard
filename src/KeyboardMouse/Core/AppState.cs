namespace KeyboardMouse.Core;

/// <summary>
/// Shared, thread-safe application state. Only tiny snapshots are taken under
/// the lock; nothing inside it ever blocks.
/// </summary>
public sealed class AppState
{
    private readonly object _gate = new();
    private bool _mouseModeOn;
    private bool _paused;
    private bool _dragLock;
    private bool _turbo;
    private bool _precision;
    private bool _scroll;

    public event Action? MouseModeChanged;

    public event Action? PausedChanged;

    public bool MouseModeOn
    {
        get
        {
            lock (_gate)
            {
                return _mouseModeOn;
            }
        }
    }

    public bool Paused
    {
        get
        {
            lock (_gate)
            {
                return _paused;
            }
        }
    }

    public bool DragLock
    {
        get
        {
            lock (_gate)
            {
                return _dragLock;
            }
        }
    }

    public void SetMouseMode(bool on)
    {
        bool changed;
        lock (_gate)
        {
            changed = _mouseModeOn != on;
            if (changed)
            {
                _mouseModeOn = on;
            }
        }

        if (changed)
        {
            MouseModeChanged?.Invoke();
        }
    }

    public void SetPaused(bool paused)
    {
        bool changed;
        lock (_gate)
        {
            changed = _paused != paused;
            if (changed)
            {
                _paused = paused;
            }
        }

        if (changed)
        {
            PausedChanged?.Invoke();
        }
    }

    public void SetDragLock(bool enabled)
    {
        lock (_gate)
        {
            _dragLock = enabled;
        }
    }

    public bool Turbo
    {
        get
        {
            lock (_gate)
            {
                return _turbo;
            }
        }
    }

    public bool Precision
    {
        get
        {
            lock (_gate)
            {
                return _precision;
            }
        }
    }

    public bool Scroll
    {
        get
        {
            lock (_gate)
            {
                return _scroll;
            }
        }
    }

    public void SetTurbo(bool enabled)
    {
        lock (_gate)
        {
            _turbo = enabled;
        }
    }

    public void SetPrecision(bool enabled)
    {
        lock (_gate)
        {
            _precision = enabled;
        }
    }

    public void SetScroll(bool enabled)
    {
        lock (_gate)
        {
            _scroll = enabled;
        }
    }

    public FeedbackState GetFeedback()
    {
        lock (_gate)
        {
            return new FeedbackState(_turbo, _precision, _scroll, _dragLock);
        }
    }
}

public readonly record struct FeedbackState(bool Turbo, bool Precision, bool Scroll, bool DragLock);
