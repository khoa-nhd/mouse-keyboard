using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Caps Lock logic: tap toggles mouse mode, hold is momentary, Shift+Caps
/// passes through as a real Caps Lock. Owns the TapHoldDetector.
/// The toggle key itself is remappable via settings (activation.toggleKey).
/// </summary>
public sealed class ActivationController
{
    private readonly AppState _state;
    private readonly Action<bool> _setMode;
    private readonly TapHoldDetector _detector;

    private PhysicalKey _toggleKey = PhysicalKey.CapsLock;

    private bool _capsDown;
    private bool _swallowCaps;
    private bool _modeBeforeHold;

    public ActivationController(AppState state, Action<bool> setMode, int tapTimeoutMs)
    {
        _state = state;
        _setMode = setMode;
        _detector = new TapHoldDetector(tapTimeoutMs);
    }

    /// <summary>Raised when polling starts or stops being needed.</summary>
    public event Action? PollingChanged;

    public bool PollingRequired => _capsDown && _swallowCaps && _detector.IsPending;

    public void SetTapTimeout(int milliseconds)
    {
        _detector.TapTimeoutMs = Math.Clamp(milliseconds, 50, 1000);
    }

    /// <summary>
    /// Changes the toggle key. Only real keys count: modifiers and unknown
    /// keys cannot be toggles, so such values are ignored.
    /// </summary>
    public void SetToggleKey(PhysicalKey key)
    {
        if (key == PhysicalKey.None || key.IsModifier())
        {
            return;
        }

        _toggleKey = key;
    }

    public HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers)
    {
        if (keyEvent.Key != _toggleKey)
        {
            return HandleResult.PassThrough;
        }

        if (keyEvent.IsDown)
        {
            if (keyEvent.IsRepeat)
            {
                return _swallowCaps ? HandleResult.Swallow : HandleResult.PassThrough;
            }

            _capsDown = true;

            // Shift + Caps Lock = the "real caps lock" passthrough; that
            // special case only exists for Caps Lock itself.
            if (modifiers.Shift && _toggleKey == PhysicalKey.CapsLock)
            {
                // Real Caps Lock: let both down and up through untouched.
                _swallowCaps = false;
                _detector.Reset();
                return HandleResult.PassThrough;
            }

            _detector.Reset();
            _swallowCaps = true;
            _modeBeforeHold = _state.MouseModeOn;
            _detector.OnKeyDown(keyEvent.TimestampMs);
            PollingChanged?.Invoke();
            return HandleResult.Swallow;
        }

        if (!_capsDown)
        {
            return HandleResult.PassThrough;
        }

        _capsDown = false;

        if (!_swallowCaps)
        {
            _swallowCaps = false;
            _detector.Reset();
            PollingChanged?.Invoke();
            return HandleResult.PassThrough;
        }

        var action = _detector.OnKeyUp(keyEvent.TimestampMs);
        Apply(action);
        PollingChanged?.Invoke();
        return HandleResult.Swallow;
    }

    /// <summary>Another key went down while Caps is held: enter momentary mode now.</summary>
    public void NotifyOtherKeyDown(long timestampMs)
    {
        if (!_capsDown || !_swallowCaps)
        {
            return;
        }

        var action = _detector.OnOtherKeyDown(timestampMs);
        if (action != TapHoldAction.None)
        {
            Apply(action);
            PollingChanged?.Invoke();
        }
    }

    public void Poll(long timestampMs)
    {
        if (!_capsDown || !_swallowCaps)
        {
            return;
        }

        var action = _detector.Poll(timestampMs);
        if (action != TapHoldAction.None)
        {
            Apply(action);
            PollingChanged?.Invoke();
        }
    }

    private void Apply(TapHoldAction action)
    {
        switch (action)
        {
            case TapHoldAction.Tap:
                _setMode(!_state.MouseModeOn);
                break;

            case TapHoldAction.HoldStart:
                _modeBeforeHold = _state.MouseModeOn;
                _setMode(true);
                break;

            case TapHoldAction.HoldEnd:
                // Restore the state from before the hold, so holding Caps for a
                // momentary click does not turn a toggled-on mode off.
                _setMode(_modeBeforeHold);
                break;
        }
    }

    public void Reset()
    {
        _detector.Reset();
        _capsDown = false;
        _swallowCaps = false;
        _modeBeforeHold = false;
        PollingChanged?.Invoke();
    }
}
