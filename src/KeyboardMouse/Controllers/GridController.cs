using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Mouseless-style grid mode. Ten column keys pick a column, thirty band keys
/// pick a row inside it, and the ten by three subgrid picks the exact click
/// point (controlled by <see cref="GridSettings.ActionLevel"/>). Space clicks
/// the virtual cursor, Enter or Shift clicks it with the right button,
/// Backspace goes back one level, Tab moves to the next monitor, Ctrl + the
/// grid key closes keeping the position, and Esc cancels and restores the
/// original pointer.
/// </summary>
public sealed class GridController : IModeController
{
    private readonly IMouseOutput _mouse;
    private readonly Func<Point> _cursor;
    private readonly IGridOverlay _overlay;
    private readonly Action<Action> _dispatch;

    private IMonitorService? _monitors;
    private GridSettings _settings;
    private KeysSettings _keys;
    private PhysicalKey _gridKey = PhysicalKey.G;
    private PhysicalKey[] _level1Keys = [];
    private string[] _level1Labels = [];
    private PhysicalKey[] _level2Keys = [];
    private string[] _level2Labels = [];
    private PhysicalKey[] _subgridKeys = [];
    private string[] _subgridLabels = [];
    private int _actionLevel = 3;
    private bool _active;
    private Point _savedPointer;
    private MonitorInfo? _monitor;
    private Rectangle _root;
    private Rectangle? _column;
    private Rectangle? _cell;
    private Point _target;
    private GridOverlayPhase _phase = GridOverlayPhase.Level1;

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
        _column = null;
        _cell = null;
        _target = _savedPointer;
        _phase = GridOverlayPhase.Level1;
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
            GoUp();
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Tab)
        {
            MoveToNextMonitor();
            return HandleResult.Swallow;
        }

        // The grid key is a level-1 cell, so the close shortcut needs Ctrl.
        if (keyEvent.Key == _gridKey && modifiers.Ctrl)
        {
            Close(restore: false);
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Space)
        {
            ExecuteAction(right: modifiers.Shift);
            return HandleResult.Swallow;
        }

        if (keyEvent.Key == PhysicalKey.Enter)
        {
            ExecuteAction(right: true);
            return HandleResult.Swallow;
        }

        int index = _phase switch
        {
            GridOverlayPhase.Level1 => FindKey(_level1Keys, keyEvent.Key),
            GridOverlayPhase.Level2 => FindKey(_level2Keys, keyEvent.Key),
            GridOverlayPhase.Subgrid => FindKey(_subgridKeys, keyEvent.Key),
            _ => -1
        };

        if (index >= 0)
        {
            switch (_phase)
            {
                case GridOverlayPhase.Level1:
                    SelectColumn(index, modifiers.Shift);
                    break;

                case GridOverlayPhase.Level2:
                    SelectRow(index, modifiers.Shift);
                    break;

                case GridOverlayPhase.Subgrid:
                    SelectSubcell(index, modifiers.Shift);
                    break;
            }
        }

        return HandleResult.Swallow;
    }

    private void SelectColumn(int index, bool right)
    {
        _column = GridMath.Cell(_root, index, 0, GridMath.Level1Columns, 1);
        _cell = null;
        _target = GridMath.Center(_column.Value);

        if (_actionLevel == 1)
        {
            ExecuteAction(right);
            return;
        }

        _phase = GridOverlayPhase.Level2;
        Show();
    }

    private void SelectRow(int index, bool right)
    {
        if (_column is not { } column)
        {
            return;
        }

        _cell = GridMath.Cell(column, 0, index, 1, GridMath.Level2Rows);
        _target = GridMath.Center(_cell.Value);

        if (_actionLevel == 2)
        {
            ExecuteAction(right);
            return;
        }

        _phase = GridOverlayPhase.Subgrid;
        Show();
    }

    private void SelectSubcell(int index, bool right)
    {
        if (_cell is not { } cell)
        {
            return;
        }

        var subcell = GridMath.SubRect(cell, index, GridMath.SubgridColumns, GridMath.SubgridRows);
        _target = GridMath.Center(subcell);
        ExecuteAction(right);
    }

    private void ExecuteAction(bool right)
    {
        _mouse.MoveTo(_target.X, _target.Y);
        if (right)
        {
            _mouse.RightClick();
        }
        else
        {
            _mouse.LeftClick();
        }

        Close(restore: false);
    }

    private void GoUp()
    {
        switch (_phase)
        {
            case GridOverlayPhase.Subgrid when _column is { } column:
                _phase = GridOverlayPhase.Level2;
                _cell = null;
                _target = GridMath.Center(column);
                Show();
                break;

            case GridOverlayPhase.Level2:
                _phase = GridOverlayPhase.Level1;
                _column = null;
                _cell = null;
                _target = _savedPointer;
                Show();
                break;
        }
    }

    private void MoveToNextMonitor()
    {
        _monitors ??= new Monitors();
        var next = _monitors.Next(_root.Width > 0 ? GridMath.Center(_root) : _savedPointer);

        _monitor = next;
        _root = next.Bounds;
        _column = null;
        _cell = null;
        _phase = GridOverlayPhase.Level1;
        _target = GridMath.Center(_root);
        Show();
    }

    private void Cancel()
    {
        Close(restore: true);
    }

    private void Close(bool restore)
    {
        _overlay.Hide();
        _active = false;
        _column = null;
        _cell = null;
        _phase = GridOverlayPhase.Level1;

        if (restore)
        {
            _mouse.MoveTo(_savedPointer.X, _savedPointer.Y);
        }
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
                _column,
                _cell,
                _level1Labels,
                _level2Labels,
                _subgridLabels,
                _phase,
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
        ParseLevel(
            _settings.Level1Keys,
            GridSettings.DefaultLevel1Keys(),
            out _level1Keys,
            out _level1Labels);
        ParseLevel(
            _settings.Level2Keys,
            GridSettings.DefaultLevel2Keys(),
            out _level2Keys,
            out _level2Labels);
        ParseLevel(
            _settings.SubgridKeys,
            GridSettings.DefaultSubgridKeys(),
            out _subgridKeys,
            out _subgridLabels);

        _actionLevel = _settings.ActionLevel?.Trim().ToLowerInvariant() switch
        {
            "1" => 1,
            "2" => 2,
            _ => 3
        };
    }

    private static void ParseLevel(
        IReadOnlyList<string>? names,
        string[] defaults,
        out PhysicalKey[] keys,
        out string[] labels)
    {
        var parsed = new PhysicalKey[defaults.Length];
        var seen = new HashSet<PhysicalKey>();
        bool useNames = names is not null && names.Count == defaults.Length;

        for (int i = 0; i < parsed.Length; i++)
        {
            string? name = useNames ? names![i] : defaults[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                // An empty cell is deliberately unselectable.
                parsed[i] = PhysicalKey.None;
                continue;
            }

            if (!KeyNames.TryParse(name, out var key) || IsReservedKey(key))
            {
                KeyNames.TryParse(defaults[i], out key);
            }

            if (key == PhysicalKey.None || IsReservedKey(key))
            {
                parsed[i] = PhysicalKey.None;
                continue;
            }

            // A duplicate in a hand-edited file must not make two cells react
            // at once; the first cell keeps the key.
            parsed[i] = seen.Add(key) ? key : PhysicalKey.None;
        }

        keys = parsed;
        labels = parsed
            .Select(key => key == PhysicalKey.None ? string.Empty : KeyNames.Display(key))
            .ToArray();
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
