namespace KeyboardMouse.Input;

public enum TapHoldAction
{
    None,
    Tap,
    HoldStart,
    HoldEnd
}

/// <summary>
/// Distinguishes a short tap from a hold for a single key (Caps Lock).
/// Pure logic with an injected clock, so it is fully unit-testable.
/// </summary>
public sealed class TapHoldDetector
{
    private int _tapTimeoutMs;

    private bool _isDown;
    private long _downTimeMs;
    private bool _pendingTap;
    private bool _holdStarted;

    public TapHoldDetector(int tapTimeoutMs)
    {
        if (tapTimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tapTimeoutMs));
        }

        _tapTimeoutMs = tapTimeoutMs;
    }

    public int TapTimeoutMs
    {
        get => _tapTimeoutMs;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _tapTimeoutMs = value;
        }
    }

    public bool IsDown => _isDown;

    /// <summary>True while a press can still become a tap.</summary>
    public bool IsPending => _isDown && _pendingTap;

    /// <summary>True while the press has been promoted to a hold.</summary>
    public bool IsHolding => _isDown && _holdStarted;

    public TapHoldAction OnKeyDown(long timestampMs)
    {
        if (_isDown)
        {
            return TapHoldAction.None; // auto-repeat
        }

        _isDown = true;
        _downTimeMs = timestampMs;
        _pendingTap = true;
        _holdStarted = false;
        return TapHoldAction.None;
    }

    /// <summary>
    /// Called when any other key goes down while this key is held: that means
    /// the user wants the momentary layer now, without waiting for the timeout.
    /// </summary>
    public TapHoldAction OnOtherKeyDown(long timestampMs)
    {
        if (!_pendingTap || !_isDown)
        {
            return TapHoldAction.None;
        }

        _pendingTap = false;
        _holdStarted = true;
        return TapHoldAction.HoldStart;
    }

    /// <summary>
    /// Called periodically while the key is down. Promotes the press to a hold
    /// when the tap window has elapsed, even if no other key was pressed.
    /// </summary>
    public TapHoldAction Poll(long timestampMs)
    {
        if (!_pendingTap || !_isDown || timestampMs - _downTimeMs < _tapTimeoutMs)
        {
            return TapHoldAction.None;
        }

        _pendingTap = false;
        _holdStarted = true;
        return TapHoldAction.HoldStart;
    }

    public TapHoldAction OnKeyUp(long timestampMs)
    {
        if (!_isDown)
        {
            return TapHoldAction.None;
        }

        _isDown = false;
        var elapsed = timestampMs - _downTimeMs;

        if (_pendingTap && elapsed < _tapTimeoutMs)
        {
            _pendingTap = false;
            _holdStarted = false;
            return TapHoldAction.Tap;
        }

        _pendingTap = false;
        var wasHolding = _holdStarted;
        _holdStarted = false;

        // If polling never ran (e.g. the app was suspended), the release still
        // ends the hold. The transient "on" state in between is not observable.
        return wasHolding || elapsed >= _tapTimeoutMs
            ? TapHoldAction.HoldEnd
            : TapHoldAction.None;
    }

    public void Reset()
    {
        _isDown = false;
        _pendingTap = false;
        _holdStarted = false;
        _downTimeMs = 0;
    }
}
