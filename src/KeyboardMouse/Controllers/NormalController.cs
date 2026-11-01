using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Mouse mode, normal layer: IJKL movement, Space left button, F/E/R buttons,
/// U/O back/forward, V drag lock, T click-and-exit, Esc leave.
///
/// A = turbo while held, S = precision while held, D = scroll while held.
/// The layers are plain hold keys, so no tap/hold detector is needed.
/// G opens the grid, H opens hints. X/C/N jump to the screen, window, and next
/// monitor; M/' start the two-key mark commands handled by
/// <see cref="PendingController"/>. Y/P/Z send Ctrl+C/V/Z while
/// <c>editExtras</c> is on. B toggles the auto clicker.
///
/// Every binding is remappable: the whole map is built from
/// <see cref="KeysSettings"/>. The first binding for a key wins, so a
/// duplicate in settings.json never makes two actions fire at once.
/// </summary>
public sealed class NormalController : IModeController
{
    private enum NormalAction
    {
        Up,
        Left,
        Down,
        Right,
        Turbo,
        Precision,
        ScrollLayer,
        LeftClick,
        RightClick,
        MiddleClick,
        DoubleClick,
        ClickAndExit,
        DragLock,
        Back,
        Forward,
        CenterScreen,
        CenterWindow,
        NextMonitor,
        EnterGrid,
        EnterHints,
        SaveMark,
        JumpMark,
        Copy,
        Paste,
        Undo,
        AutoClick
    }

    private readonly AppState _state;
    private readonly IMovementControl _movement;
    private readonly IMouseOutput _mouse;
    private readonly IKeyOutput _keys;
    private readonly IMonitorService? _monitors;
    private readonly Func<Point> _cursor;
    private readonly Action _openGrid;
    private readonly Action _openHint;
    private readonly Action _beginMarkSave;
    private readonly Action _beginMarkJump;
    private readonly Action _exitMode;
    private readonly Action? _toggleAutoClick;

    private SpeedProfiles _profiles;
    private KeysSettings _settings = new();
    private bool _editingExtras;
    private readonly Dictionary<PhysicalKey, NormalAction> _bindings = new();
    private bool _rightButtonDown;

    public NormalController(
        AppState state,
        IMovementControl movement,
        IMouseOutput mouse,
        IKeyOutput keys,
        SpeedProfiles profiles,
        IMonitorService? monitors,
        Func<Point> cursor,
        KeysSettings settings,
        Action openGrid,
        Action openHint,
        Action beginMarkSave,
        Action beginMarkJump,
        Action exitMode,
        bool editingExtras = false,
        Action? toggleAutoClick = null)
    {
        _state = state;
        _movement = movement;
        _mouse = mouse;
        _keys = keys;
        _profiles = profiles;
        _monitors = monitors;
        _cursor = cursor;
        _openGrid = openGrid;
        _openHint = openHint;
        _beginMarkSave = beginMarkSave;
        _beginMarkJump = beginMarkJump;
        _exitMode = exitMode;
        _toggleAutoClick = toggleAutoClick;
        _editingExtras = editingExtras;

        UpdateKeys(settings);
        ApplyEngine();
    }

    public bool IsActive => _state.MouseModeOn;

    public void UpdateProfiles(SpeedProfiles profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
        ApplyEngine();
    }

    public void UpdateKeys(KeysSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        RebuildBindings();
    }

    public void UpdateEditingExtras(bool enabled)
    {
        if (_editingExtras == enabled)
        {
            return;
        }

        _editingExtras = enabled;
        RebuildBindings();
    }

    /// <summary>
    /// Rebuilds the key map from the current settings. Field order matters:
    /// it defines which action wins when settings.json binds the same key
    /// twice — the earlier binding keeps the key, the later one is ignored.
    /// </summary>
    private void RebuildBindings()
    {
        var s = _settings;
        var bindings = new Dictionary<PhysicalKey, NormalAction>();

        void Bind(string? name, PhysicalKey fallback, NormalAction action)
        {
            var key = KeyNames.TryParse(name, out var parsed) ? parsed : fallback;
            if (key == PhysicalKey.None)
            {
                return;
            }

            if (key.IsModifier())
            {
                // Modifier keys must stay modifiers; binding a modifier here
                // would swallow Ctrl/Shift/Alt everywhere.
                return;
            }

            if (!bindings.ContainsKey(key))
            {
                bindings.Add(key, action);
            }
        }

        Bind(s.Up, PhysicalKey.I, NormalAction.Up);
        Bind(s.Left, PhysicalKey.J, NormalAction.Left);
        Bind(s.Down, PhysicalKey.K, NormalAction.Down);
        Bind(s.Right, PhysicalKey.L, NormalAction.Right);
        Bind(s.LeftClick, PhysicalKey.Space, NormalAction.LeftClick);
        Bind(s.RightClick, PhysicalKey.F, NormalAction.RightClick);
        Bind(s.MiddleClick, PhysicalKey.E, NormalAction.MiddleClick);
        Bind(s.DoubleClick, PhysicalKey.R, NormalAction.DoubleClick);
        Bind(s.ClickAndExit, PhysicalKey.T, NormalAction.ClickAndExit);
        Bind(s.DragLock, PhysicalKey.V, NormalAction.DragLock);
        Bind(s.Back, PhysicalKey.U, NormalAction.Back);
        Bind(s.Forward, PhysicalKey.O, NormalAction.Forward);
        Bind(s.ScrollLayer, PhysicalKey.D, NormalAction.ScrollLayer);
        Bind(s.PrecisionLayer, PhysicalKey.S, NormalAction.Precision);
        Bind(s.TurboLayer, PhysicalKey.A, NormalAction.Turbo);
        Bind(s.CenterScreen, PhysicalKey.X, NormalAction.CenterScreen);
        Bind(s.CenterWindow, PhysicalKey.C, NormalAction.CenterWindow);
        Bind(s.NextMonitor, PhysicalKey.N, NormalAction.NextMonitor);
        Bind(s.Grid, PhysicalKey.G, NormalAction.EnterGrid);
        Bind(s.Hint, PhysicalKey.H, NormalAction.EnterHints);
        Bind(s.MarkSave, PhysicalKey.M, NormalAction.SaveMark);
        Bind(s.MarkJump, PhysicalKey.Apostrophe, NormalAction.JumpMark);

        if (_toggleAutoClick is not null)
        {
            Bind(s.AutoClick, PhysicalKey.B, NormalAction.AutoClick);
        }

        if (_editingExtras)
        {
            Bind(s.Copy, PhysicalKey.Y, NormalAction.Copy);
            Bind(s.Paste, PhysicalKey.P, NormalAction.Paste);
            Bind(s.Undo, PhysicalKey.Z, NormalAction.Undo);
        }

        // Swap in the finished map: a key arriving mid-rebuild always sees a
        // complete map, never a half-built one.
        _bindings.Clear();
        foreach (var pair in bindings)
        {
            _bindings[pair.Key] = pair.Value;
        }
    }

    public HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers)
    {
        // Escape is not remappable: it is the common "get out" key. Leave
        // mouse mode on the down; both down and up stay swallowed.
        if (keyEvent.Key == PhysicalKey.Escape)
        {
            if (keyEvent.IsDown)
            {
                _exitMode();
            }

            return HandleResult.Swallow;
        }

        if (!_bindings.TryGetValue(keyEvent.Key, out var action))
        {
            return HandleResult.PassThrough;
        }

        switch (action)
        {
            case NormalAction.Up:
                _movement.SetDirection(Direction.Up, keyEvent.IsDown);
                break;

            case NormalAction.Left:
                _movement.SetDirection(Direction.Left, keyEvent.IsDown);
                break;

            case NormalAction.Down:
                _movement.SetDirection(Direction.Down, keyEvent.IsDown);
                break;

            case NormalAction.Right:
                _movement.SetDirection(Direction.Right, keyEvent.IsDown);
                break;

            case NormalAction.Turbo:
                SetTurbo(keyEvent.IsDown);
                break;

            case NormalAction.Precision:
                SetPrecision(keyEvent.IsDown);
                break;

            case NormalAction.ScrollLayer:
                SetScroll(keyEvent.IsDown);
                break;

            case NormalAction.LeftClick:
                HandleLeftButton(keyEvent);
                break;

            case NormalAction.RightClick:
                HandleRightButton(keyEvent);
                break;

            case NormalAction.MiddleClick:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    _mouse.MiddleClick();
                }

                break;

            case NormalAction.DoubleClick:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    _mouse.DoubleClick();
                }

                break;

            case NormalAction.Back:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    if (_state.Scroll)
                    {
                        _keys.SendKey(PhysicalKey.PageUp);
                    }
                    else
                    {
                        _mouse.XButtonClick(MouseXButton.X1);
                    }
                }

                break;

            case NormalAction.Forward:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    if (_state.Scroll)
                    {
                        _keys.SendKey(PhysicalKey.PageDown);
                    }
                    else
                    {
                        _mouse.XButtonClick(MouseXButton.X2);
                    }
                }

                break;

            case NormalAction.ClickAndExit:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    // Click, then leave mouse mode so the user can type again.
                    _mouse.LeftClick();
                    _exitMode();
                }

                break;

            case NormalAction.DragLock:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    ToggleDragLock();
                }

                break;

            case NormalAction.CenterScreen:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    JumpToScreenCenter();
                }

                break;

            case NormalAction.CenterWindow:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    JumpToWindowCenter();
                }

                break;

            case NormalAction.NextMonitor:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    JumpToNextMonitor();
                }

                break;

            case NormalAction.EnterGrid:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    _openGrid();
                }

                break;

            case NormalAction.EnterHints:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    _openHint();
                }

                break;

            case NormalAction.SaveMark:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    _beginMarkSave();
                }

                break;

            case NormalAction.JumpMark:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    _beginMarkJump();
                }

                break;

            case NormalAction.Copy:
            case NormalAction.Paste:
            case NormalAction.Undo:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    SendEditingChord(action);
                }

                break;

            case NormalAction.AutoClick:
                if (keyEvent.IsDown && !keyEvent.IsRepeat)
                {
                    // Auto-clicking with the left button held would end a locked
                    // drag on the first click anyway; drop it cleanly first.
                    if (_state.DragLock && !_state.AutoClicking)
                    {
                        ToggleDragLock();
                    }

                    _toggleAutoClick?.Invoke();
                }

                break;

            default:
                break;
        }

        return HandleResult.Swallow;
    }

    /// <summary>
    /// Y/P/Z synthesize Ctrl+C / Ctrl+V / Ctrl+Z. The chord keys themselves
    /// stay fixed; only the launcher keys are remappable.
    /// </summary>
    private void SendEditingChord(NormalAction action)
    {
        var key = action switch
        {
            NormalAction.Copy => PhysicalKey.C,
            NormalAction.Paste => PhysicalKey.V,
            _ => PhysicalKey.Z
        };

        _keys.SendChord(ctrl: true, key);
    }

    private void SetTurbo(bool down)
    {
        if (_state.Turbo == down)
        {
            return;
        }

        _state.SetTurbo(down);
        ApplyEngine();
    }

    private void SetPrecision(bool down)
    {
        if (_state.Precision == down)
        {
            return;
        }

        _state.SetPrecision(down);
        ApplyEngine();
    }

    private void SetScroll(bool down)
    {
        if (_state.Scroll == down)
        {
            return;
        }

        _state.SetScroll(down);
        ApplyEngine();
    }

    /// <summary>
    /// Pushes the current layer state into the engine. Precision wins over
    /// turbo; in the scroll layer holding S gives slow, precise scrolling.
    /// </summary>
    private void ApplyEngine()
    {
        bool scroll = _state.Scroll;

        _movement.SetMode(scroll ? MovementMode.Scroll : MovementMode.Move);
        _movement.SetScrollProfile(_profiles.Scroll, _profiles.SmoothScroll);
        _movement.SetScrollPrecision(scroll && _state.Precision);

        SpeedProfile moveProfile = _state.Precision
            ? _profiles.Precision
            : _state.Turbo
                ? _profiles.Turbo
                : _profiles.Normal;

        _movement.SetMoveProfile(moveProfile);
    }

    private void HandleRightButton(KeyEvent keyEvent)
    {
        if (keyEvent.IsDown)
        {
            if (!keyEvent.IsRepeat && !_rightButtonDown)
            {
                _rightButtonDown = true;
                _mouse.RightDown();
            }
        }
        else if (_rightButtonDown)
        {
            _rightButtonDown = false;
            _mouse.RightUp();
        }
    }

    private void HandleLeftButton(KeyEvent keyEvent)
    {
        if (keyEvent.IsDown)
        {
            if (keyEvent.IsRepeat)
            {
                return;
            }

            if (_state.DragLock)
            {
                // A press with drag lock on ends the locked drag.
                _state.SetDragLock(false);
                _mouse.LeftUp();
            }
            else
            {
                _mouse.LeftDown();
            }

            return;
        }

        if (_state.DragLock)
        {
            _state.SetDragLock(false);
        }

        _mouse.LeftUp();
    }

    private void ToggleDragLock()
    {
        bool enable = !_state.DragLock;
        _state.SetDragLock(enable);

        if (enable)
        {
            _mouse.LeftDown();
        }
        else
        {
            _mouse.LeftUp();
        }
    }

    private void JumpToScreenCenter()
    {
        if (_monitors is null)
        {
            return;
        }

        _movement.ClearDirections();
        var center = _monitors.GetAtPoint(_cursor()).WorkCenter;
        _mouse.MoveTo(center.X, center.Y);
    }

    private void JumpToWindowCenter()
    {
        if (_monitors?.GetForegroundWindowCenter() is not { } center)
        {
            return;
        }

        _movement.ClearDirections();
        _mouse.MoveTo(center.X, center.Y);
    }

    private void JumpToNextMonitor()
    {
        if (_monitors is null)
        {
            return;
        }

        _movement.ClearDirections();
        var center = _monitors.Next(_cursor()).WorkCenter;
        _mouse.MoveTo(center.X, center.Y);
    }

    public void Reset()
    {
        _movement.ClearDirections();
        _state.SetDragLock(false);
        _state.SetTurbo(false);
        _state.SetPrecision(false);
        _state.SetScroll(false);
        _rightButtonDown = false;
        ApplyEngine();
        _mouse.ReleaseAll();
    }
}
