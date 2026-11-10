using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Grid mode: one grid of small cells over the screen, and four characters
/// printed in every cell.
///
/// The four characters are the complete code that reaches the cell, so they are
/// readable before anything is typed. Typing narrows the grid: the first
/// character lights a band of columns, the second a single column, the third a
/// band of rows, and the fourth the cell itself, which clicks at its centre.
/// There is no second grid and no extra level to step into.
///
/// Space clicks the virtual cursor at any point and Enter clicks it with the
/// right button. Held while typing, Shift turns the click into a right click,
/// and Tab moves the pointer to the cell without clicking at all (Tab before
/// anything is typed switches to the next monitor). Backspace takes a character
/// back, Ctrl + the grid key closes keeping the position, and Esc cancels and
/// restores the original pointer.
/// </summary>
public sealed class GridController : IModeController
{
    /// <summary>What finishing a code does with the pointer.</summary>
    private enum GridAction
    {
        None,
        Left,
        Right
    }

    private readonly IMouseOutput _mouse;
    private readonly Func<Point> _cursor;
    private readonly IGridOverlay _overlay;
    private readonly Action<Action> _dispatch;

    private IMonitorService? _monitors;
    private GridSettings _settings;
    private KeysSettings _keys;
    private PhysicalKey _gridKey = PhysicalKey.G;
    private PhysicalKey[] _keyChars = [];
    private string[] _keyLabels = [];
    private string[] _labels = [];
    private int _columns = GridSettings.DefaultColumns;
    private int _rows = GridSettings.DefaultRows;
    private bool _active;
    private Point _savedPointer;
    private MonitorInfo? _monitor;
    private Rectangle _root;
    private string _typed = string.Empty;
    private int _highlight = -1;
    private Point _target;

    public GridController(
        IMouseOutput mouse,
        IMonitorService? monitors,
        IGridOverlay overlay,
        GridSettings settings,
        Func<Point>? cursor = null,
        Action<Action>? dispatch = null,
        KeysSettings? keys = null)
    {
        _mouse = mouse;
        _monitors = monitors;
        _overlay = overlay;
        _settings = settings;
        _keys = keys ?? new KeysSettings();
        _cursor = cursor ?? Pointer.Position;
        _dispatch = dispatch ?? (action => action());
        ParseSettings();
        ParseControlKeys();
    }

    public bool IsActive => _active;

    public void UpdateSettings(GridSettings settings)
    {
        _settings = settings;
        ParseSettings();

        if (_active)
        {
            Close(restore: false);
        }
    }

    public void UpdateKeys(KeysSettings settings)
    {
        _keys = settings;
        ParseControlKeys();
    }

    public void Open()
    {
        if (_active)
        {
            Close(restore: false);
            return;
        }

        ParseSettings();
        _monitors ??= new Monitors();

        _savedPointer = _cursor();
        _monitor = _monitors.GetAtPoint(_savedPointer);
        _root = _monitor.Bounds;
        ResetSelection();
        _active = true;
        Show();
    }

    public HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers)
    {
        if (!_active)
        {
            return HandleResult.PassThrough;
        }

        // Modifiers pass through so the final click can be Shift-modified.
        if (keyEvent.Key.IsModifier())
        {
            return HandleResult.PassThrough;
        }

        if (!keyEvent.IsDown || keyEvent.IsRepeat)
        {
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Escape)
        {
            Cancel();
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Backspace)
        {
            RemoveLastCharacter();
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Tab)
        {
            // Partway through a code, Tab parks the pointer on the highlighted
            // cell without clicking. Before anything is typed there is no cell
            // to park on, so it keeps its old job of switching monitor.
            if (_typed.Length > 0 && _highlight >= 0)
            {
                ExecuteAction(GridAction.None);
            }
            else
            {
                MoveToNextMonitor();
            }

            return HandleResult.Swallow;
        }

        // The grid key is one of the cell characters, so closing needs Ctrl.
        if (keyEvent.Key == _gridKey && modifiers.Ctrl)
        {
            Close(restore: false);
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Space)
        {
            ExecuteAction(ActionFor(modifiers));
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Enter)
        {
            ExecuteAction(GridAction.Right);
            return HandleResult.Swallow;
        }

        int index = FindKey(_keyChars, keyEvent.Key);

        if (index >= 0)
        {
            TypeCharacter(_keyLabels[index], ActionFor(modifiers));
        }

        return HandleResult.Swallow;
    }

    /// <summary>Shift turns the click into a right click; without it, a left click.</summary>
    private static GridAction ActionFor(ModifierState modifiers) =>
        modifiers.Shift ? GridAction.Right : GridAction.Left;

    /// <summary>
    /// Adds one character of a cell code. A character that no remaining cell can
    /// accept is ignored, so a stray key never moves the selection.
    /// </summary>
    private void TypeCharacter(string character, GridAction action)
    {
        if (character.Length == 0 || _typed.Length >= GridCodes.Length)
        {
            return;
        }

        string candidate = _typed + character;
        int match = FirstMatch(candidate);

        if (match < 0)
        {
            return;
        }

        _typed = candidate;
        _highlight = match;
        _target = CenterOf(match);

        if (_typed.Length == GridCodes.Length)
        {
            ExecuteAction(action);
            return;
        }

        Show();
    }

    private void RemoveLastCharacter()
    {
        if (_typed.Length == 0)
        {
            return;
        }

        _typed = _typed[..^1];

        if (_typed.Length == 0)
        {
            _highlight = -1;
            _target = _savedPointer;
        }
        else
        {
            _highlight = FirstMatch(_typed);
            _target = _highlight >= 0 ? CenterOf(_highlight) : _savedPointer;
        }

        Show();
    }

    private int FirstMatch(string prefix)
    {
        for (int i = 0; i < _labels.Length; i++)
        {
            if (GridCodes.Matches(_labels[i], prefix))
            {
                return i;
            }
        }

        return -1;
    }

    private Point CenterOf(int index) =>
        GridMath.Center(GridMath.SubRect(_root, index, _columns, _rows));

    private void ExecuteAction(GridAction action)
    {
        _mouse.MoveTo(_target.X, _target.Y);

        switch (action)
        {
            case GridAction.Right:
                _mouse.RightClick();
                break;

            case GridAction.Left:
                _mouse.LeftClick();
                break;
        }

        Close(restore: false);
    }

    private void MoveToNextMonitor()
    {
        _monitors ??= new Monitors();
        var next = _monitors.Next(_root.Width > 0 ? GridMath.Center(_root) : _savedPointer);

        _monitor = next;
        _root = next.Bounds;
        _typed = string.Empty;
        _highlight = -1;
        _target = GridMath.Center(_root);
        Show();
    }

    private void Cancel() => Close(restore: true);

    private void Close(bool restore)
    {
        _overlay.Hide();
        _active = false;
        ResetSelection();

        if (restore)
        {
            _mouse.MoveTo(_savedPointer.X, _savedPointer.Y);
        }
    }

    private void ResetSelection()
    {
        _typed = string.Empty;
        _highlight = -1;
        _target = _active ? GridMath.Center(_root) : _savedPointer;
    }

    private void Show()
    {
        if (_monitor is null)
        {
            return;
        }

        // Rendering a full-monitor bitmap inside the keyboard hook would risk
        // the low-level hook timeout, so hand it to the dispatcher.
        _dispatch(() =>
        {
            if (!_active || _monitor is null)
            {
                return;
            }

            _overlay.Show(new GridOverlayState(
                _monitor,
                _root,
                _columns,
                _rows,
                _labels,
                _typed,
                _highlight,
                _target));
        });
    }

    public void Reset()
    {
        if (_active)
        {
            Close(restore: false);
        }
    }

    private static int FindKey(PhysicalKey[] keys, PhysicalKey key)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key)
            {
                return i;
            }
        }

        return -1;
    }

    private void ParseSettings()
    {
        ParseKeys(_settings.Keys, out _keyChars, out _keyLabels);

        _columns = Math.Clamp(_settings.Columns, GridSettings.MinColumns, GridSettings.MaxColumns);
        _rows = Math.Clamp(_settings.Rows, GridSettings.MinRows, GridSettings.MaxRows);
        _labels = GridCodes.Build(_columns, _rows, _keyLabels);
    }

    private static void ParseKeys(
        IReadOnlyList<string>? names,
        out PhysicalKey[] keys,
        out string[] labels)
    {
        var defaults = GridSettings.DefaultKeys();
        var parsed = new PhysicalKey[defaults.Length];
        var text = new string[defaults.Length];
        var seen = new HashSet<PhysicalKey>();
        bool useNames = names is not null && names.Count == defaults.Length;

        for (int i = 0; i < parsed.Length; i++)
        {
            string? name = useNames ? names![i] : defaults[i];

            if (string.IsNullOrWhiteSpace(name))
            {
                // An empty slot is deliberately unselectable.
                parsed[i] = PhysicalKey.None;
                continue;
            }

            if (!KeyNames.TryParse(name, out var key) || IsReservedKey(key))
            {
                KeyNames.TryParse(defaults[i], out key);
            }

            if (key == PhysicalKey.None || IsReservedKey(key) || !seen.Add(key))
            {
                parsed[i] = PhysicalKey.None;
                continue;
            }

            parsed[i] = key;
            text[i] = KeyNames.Display(key);
        }

        keys = parsed;
        labels = text;
    }

    private void ParseControlKeys()
    {
        _gridKey = ResolveControl(_keys.Grid, PhysicalKey.G);
    }

    private static PhysicalKey ResolveControl(string? name, PhysicalKey fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PhysicalKey.None;
        }

        return KeyNames.TryParse(name, out var key) &&
               key != PhysicalKey.None &&
               key != PhysicalKey.Escape &&
               !key.IsModifier()
            ? key
            : fallback;
    }

    private static bool IsReservedKey(PhysicalKey key) =>
        key is PhysicalKey.None
            or PhysicalKey.Escape
            or PhysicalKey.Backspace
            or PhysicalKey.Space
            or PhysicalKey.Enter
            or PhysicalKey.Tab
            or PhysicalKey.CapsLock ||
        key.IsModifier();
}
