using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Controllers;
using KeyboardMouse.Core;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Input;

/// <summary>
/// Central decision point: swallow or pass every physical key event.
///
/// Order: pause -> panic -> Caps activation -> active controller chain.
/// Every swallowed key-down remembers its key so the matching key-up is
/// swallowed too, even if the mode changed in between (no orphan key-ups).
/// </summary>
public sealed class InputRouter
{
    private readonly AppState _state;
    private readonly IMovementControl _movement;
    private readonly IMouseOutput _mouse;
    private readonly ActivationController _activation;
    private readonly HintController _hint;
    private readonly GridController _grid;
    private readonly PendingController _pending;
    private readonly NormalController _normal;
    private readonly AutoClicker _autoClicker;
    private readonly IModeController[] _controllers;
    private readonly Func<long> _clock;
    private readonly HashSet<PhysicalKey> _swallowedDown = new();
    private int _autoOffSeconds;
    private long _lastActivity;
    private PhysicalKey _toggleKey = PhysicalKey.CapsLock;

    // Modifier bookkeeping is per physical key, so releasing one Shift while
    // the other is held keeps Shift logically down.
    private bool _leftShift;
    private bool _rightShift;
    private bool _leftCtrl;
    private bool _rightCtrl;
    private bool _leftAlt;
    private bool _rightAlt;
    private bool _leftWin;
    private bool _rightWin;

    public InputRouter(
        AppState state,
        IMovementControl movement,
        IMouseOutput mouse,
        int tapTimeoutMs,
        Func<long>? clock = null,
        IKeyOutput? keyOutput = null,
        SpeedProfiles? profiles = null,
        IMonitorService? monitors = null,
        IGridOverlay? gridOverlay = null,
        GridSettings? gridSettings = null,
        IMarkStore? marks = null,
        KeysSettings? keys = null,
        IClickableScanner? clickableScanner = null,
        IHintOverlay? hintOverlay = null,
        HintSettings? hintSettings = null,
        Func<IntPtr>? foreground = null,
        Action<Action>? dispatch = null,
        Func<Point>? cursor = null,
        int autoOffSeconds = 0,
        PhysicalKey toggleKey = PhysicalKey.CapsLock,
        AutoClickSettings? autoClick = null,
        Func<int, Action, IDisposable>? autoClickScheduler = null)
    {
        _state = state;
        _movement = movement;
        _mouse = mouse;
        _clock = clock ?? (() => Environment.TickCount64);
        _autoOffSeconds = Math.Clamp(autoOffSeconds, 0, 86_400);
        _lastActivity = _clock();

        _activation = new ActivationController(state, SetMode, tapTimeoutMs);
        SetToggleKey(toggleKey);
        _activation.PollingChanged += () => PollingChanged?.Invoke();

        Func<Point> pointer = cursor ?? Pointer.Position;

        _hint = new HintController(
            mouse,
            monitors,
            hintOverlay ?? NullHintOverlay.Instance,
            clickableScanner ?? NullClickableScanner.Instance,
            hintSettings ?? new HintSettings(),
            pointer,
            foreground ?? Native.User32.GetForegroundWindow,
            dispatch ?? (action => action()));

        _grid = new GridController(
            mouse,
            monitors,
            gridOverlay ?? NullGridOverlay.Instance,
            gridSettings ?? new GridSettings(),
            pointer,
            dispatch ?? (action => action()));

        _pending = new PendingController(
            mouse,
            marks ?? new InMemoryMarkStore(),
            pointer,
            monitors,
            _clock);
        _pending.Changed += () => PollingChanged?.Invoke();

        _autoClicker = new AutoClicker(state, mouse, autoClick, autoClickScheduler);
        _autoClicker.Changed += () => AutoClickChanged?.Invoke();

        _normal = new NormalController(
            state,
            movement,
            mouse,
            keyOutput ?? NullKeyOutput.Instance,
            profiles ?? SpeedProfiles.Default,
            monitors,
            pointer,
            keys ?? new KeysSettings(),
            () => _grid.Open(),
            () => _hint.Open(),
            () => _pending.BeginSave(),
            () => _pending.BeginJump(),
            () => SetMode(false),
            _autoClicker.Toggle);

        _controllers = [_hint, _grid, _pending, _normal];
    }

    public void UpdateProfiles(SpeedProfiles profiles)
    {
        _normal.UpdateProfiles(profiles);
    }

    public void UpdateGrid(GridSettings settings)
    {
        _grid.UpdateSettings(settings);
    }

    public void UpdateKeys(KeysSettings settings)
    {
        _normal.UpdateKeys(settings);
    }

    public void UpdateHint(HintSettings settings)
    {
        _hint.UpdateSettings(settings);
    }


    public void UpdateAutoClick(AutoClickSettings settings)
    {
        _autoClicker.UpdateSettings(settings);
    }

    public bool AutoClicking => _autoClicker.IsRunning;

    /// <summary>Raised (on any thread) when the auto clicker starts or stops.</summary>
    public event Action? AutoClickChanged;

    public void UpdateAutoOff(int seconds)
    {
        _autoOffSeconds = Math.Clamp(seconds, 0, 86_400);
        // The polling requirement may have changed with the new timeout.
        PollingChanged?.Invoke();
    }

    /// <summary>Remaps the tap-to-toggle key (activation.toggleKey).
    /// Keys that cannot be toggles are ignored by the activation controller.</summary>
    public void SetToggleKey(PhysicalKey key)
    {
        _activation.SetToggleKey(key);
        _toggleKey = key;
    }

    public bool MarkPending => _pending.IsActive;

    /// <summary>Raised when the Caps polling timer should be started or stopped.</summary>
    public event Action? PollingChanged;

    public bool PollingRequired =>
        _activation.PollingRequired || _pending.IsActive ||
        (_autoOffSeconds > 0 && _state.MouseModeOn);

    public bool IsPaused => _state.Paused;

    public void SetMode(bool on)
    {
        if (_state.MouseModeOn == on)
        {
            return;
        }

        _state.SetMouseMode(on);

        if (on)
        {
            // The idle clock starts now: the router may have been idle far
            // longer than autoOffSeconds before this, and the instant off of
            // the first poll would otherwise kill the mode as it turns on.
            _lastActivity = _clock();
        }
        else
        {
            // Stop clicking before releasing buttons, so no click lands after.
            _autoClicker.Stop();
            _movement.ClearDirections();
            foreach (var controller in _controllers)
            {
                controller.Reset();
            }

            _mouse.ReleaseAll();
        }

        // The mode change may start or stop the auto-off polling.
        PollingChanged?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        if (paused && _state.MouseModeOn)
        {
            SetMode(false);
        }

        _state.SetPaused(paused);
    }

    public void SetTapTimeout(int milliseconds)
    {
        _activation.SetTapTimeout(milliseconds);
    }

    /// <summary>Called periodically (UI timer) only while Caps is held.</summary>
    public void Tick()
    {
        if (_state.Paused)
        {
            return;
        }

        _activation.Poll(_clock());
        _pending.Tick(_clock());

        // A running auto clicker is activity too: auto-off must not stop it.
        if (_autoClicker.IsRunning)
        {
            _lastActivity = _clock();
        }

        // Auto-off: after `autoOffSeconds` without any key event in mouse
        // mode, leave the mode (which releases all buttons). 0 disables it.
        if (_autoOffSeconds > 0 &&
            _state.MouseModeOn &&
            _clock() - _lastActivity > _autoOffSeconds * 1000L)
        {
            SetMode(false);
        }
    }

    /// <summary>
    /// Handles one physical key event. Returns true when the event must be
    /// swallowed (never reach the foreground application).
    /// </summary>
    public bool HandleKey(KeyEvent keyEvent)
    {
        UpdateModifiers(keyEvent);

        if (_state.MouseModeOn)
        {
            // Any key event counts as activity for the auto-off timer; press
            // and release alike.
            _lastActivity = keyEvent.TimestampMs;
        }

        if (_state.Paused)
        {
            // An up for a key we swallowed may still arrive while paused (the
            // mode was turned off or the pause happened in between): drop the
            // down bookkeeping or it would sit in the set forever.
            if (!keyEvent.IsDown)
            {
                _swallowedDown.Remove(keyEvent.Key);
            }

            return false;
        }

        if (keyEvent.IsDown && !keyEvent.IsRepeat &&
            Modifiers.Ctrl && Modifiers.Alt && keyEvent.Key == PhysicalKey.Backspace)
        {
            Panic();
            _swallowedDown.Add(keyEvent.Key);
            return true;
        }

        // Any other key going down while Caps is held promotes the press to a
        // momentary hold immediately.
        if (keyEvent.IsDown && !keyEvent.IsRepeat && keyEvent.Key != _toggleKey)
        {
            _activation.NotifyOtherKeyDown(keyEvent.TimestampMs);
        }

        bool handled = _activation.Handle(keyEvent, Modifiers) == HandleResult.Swallow;
        bool controllerHandled = false;

        if (_state.MouseModeOn)
        {
            foreach (var controller in _controllers)
            {
                if (!controller.IsActive)
                {
                    continue;
                }

                if (controller.Handle(keyEvent, Modifiers) == HandleResult.Swallow)
                {
                    controllerHandled = true;
                    break;
                }
            }
        }

        if (keyEvent.IsDown)
        {
            handled |= controllerHandled;
            if (handled)
            {
                _swallowedDown.Add(keyEvent.Key);
            }

            return handled;
        }

        // On release, only swallow if the matching down was swallowed or the
        // activation layer itself handled it. A key that was already down
        // before mouse mode turned on must pass its up through to the app.
        // Controllers still see the up so they can clear their held state.
        bool wasSwallowed = _swallowedDown.Remove(keyEvent.Key);
        return wasSwallowed || handled;
    }

    /// <summary>Panic: release everything and leave mouse mode.</summary>
    public void Panic()
    {
        _autoClicker.Stop();
        SetMode(false);
        _activation.Reset();
        _movement.ClearDirections();
        _mouse.ReleaseAll();
    }

    private ModifierState Modifiers =>
        new(_leftShift || _rightShift,
            _leftCtrl || _rightCtrl,
            _leftAlt || _rightAlt,
            _leftWin || _rightWin);

    private void UpdateModifiers(KeyEvent keyEvent)
    {
        bool down = keyEvent.IsDown;

        switch (keyEvent.Key)
        {
            case PhysicalKey.LeftShift: _leftShift = down; break;
            case PhysicalKey.RightShift: _rightShift = down; break;
            case PhysicalKey.LeftCtrl: _leftCtrl = down; break;
            case PhysicalKey.RightCtrl: _rightCtrl = down; break;
            case PhysicalKey.LeftAlt: _leftAlt = down; break;
            case PhysicalKey.RightAlt: _rightAlt = down; break;
            case PhysicalKey.LeftWin: _leftWin = down; break;
            case PhysicalKey.RightWin: _rightWin = down; break;
        }
    }
}
