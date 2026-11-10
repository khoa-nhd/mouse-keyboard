using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Mouseless-style grid mode.
///
/// The overlay opens on the whole primary lattice, ten columns by thirty rows,
/// with every cell labelled by the complete code that reaches it (`AG`, `SW`,
/// ...), so the whole screen is addressable at a glance instead of one column
/// at a time. Typing the first key highlights its column; typing the second
/// picks the cell and zooms into it, showing a ten by three subgrid whose cells
/// carry the full three-key code. Typing the third key picks a sub-cell and
/// zooms into a three by three micro grid, where the fourth key clicks.
///
/// Space clicks the virtual cursor at any stage, Enter or Shift clicks it with
/// the right button, Backspace goes back one stage, Tab moves to the next
/// monitor, Ctrl + the grid key closes keeping the position, and Esc cancels
/// and restores the original pointer.
///
/// <see cref="GridSettings.ActionLevel"/> decides how early a click happens:
/// "1" after the column, "2" after the cell, "subgrid" after the sub-cell, and
/// the default "micro" only after the micro cell.
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
    private PhysicalKey[] _microKeys = [];
    private string[] _microLabels = [];
    private int _actionLevel = 4;
    private bool _active;
    private Point _savedPointer;
    private MonitorInfo? _monitor;
    private Rectangle _root;
    private int _columnIndex = -1;
    private Rectangle? _column;
    private Rectangle? _cell;
    private Rectangle? _subcell;
    private string _typed = string.Empty;
    private Point _target;
    private GridOverlayPhase _phase = GridOverlayPhase.Primary;

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
        _target = _savedPointer;
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
            // The primary lattice answers to both key sets: the first key picks
            // a column, the second the row inside it.
            GridOverlayPhase.Primary => _typed.Length == 0
                ? FindKey(_level1Keys, keyEvent.Key)
                : FindKey(_level2Keys, keyEvent.Key),
            GridOverlayPhase.Subgrid => FindKey(_subgridKeys, keyEvent.Key),
            GridOverlayPhase.Micro => FindKey(_microKeys, keyEvent.Key),
            _ => -1
        };

        if (index >= 0)
        {
            Advance(index, modifiers.Shift);
        }

        return HandleResult.Swallow;
    }

    private void Advance(int index, bool right)
    {
        switch (_phase)
        {
            case GridOverlayPhase.Primary when _typed.Length == 0:
                SelectColumn(index, right);
                break;

            case GridOverlayPhase.Primary:
                SelectRow(index, right);
                break;

            case GridOverlayPhase.Subgrid:
                SelectSubcell(index, right);
                break;

            case GridOverlayPhase.Micro:
                SelectMicrocell(index, right);
                break;
        }
    }

    private void SelectColumn(int index, bool right)
    {
        _columnIndex = index;
        _column = GridMath.Cell(_root, index, 0, GridMath.PrimaryColumns, 1);
        _cell = null;
        _subcell = null;
        _typed = _level1Labels[index];
        _target = GridMath.Center(_column.Value);

        if (_actionLevel == 1)
        {
            ExecuteAction(right);
            return;
        }

        Show();
    }

    private void SelectRow(int index, bool right)
    {
        if (_columnIndex < 0)
        {
            return;
        }

        _cell = GridMath.Cell(_root, _columnIndex, index, GridMath.PrimaryColumns, GridMath.PrimaryRows);
        _subcell = null;
        _typed = _level1Labels[_columnIndex] + _level2Labels[index];
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

        _subcell = GridMath.SubRect(cell, index, GridMath.SubgridColumns, GridMath.SubgridRows);
        _typed += _subgridLabels[index];
        _target = GridMath.Center(_subcell.Value);

        if (_actionLevel <= 3)
        {
            ExecuteAction(right);
            return;
        }

        _phase = GridOverlayPhase.Micro;
        Show();
    }

    private void SelectMicrocell(int index, bool right)
    {
        if (_subcell is not { } subcell)
        {
            return;
        }

        var micro = GridMath.SubRect(subcell, index, GridMath.MicroColumns, GridMath.MicroRows);
        _typed += _microLabels[index];
        _target = GridMath.Center(micro);
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
            case GridOverlayPhase.Micro when _cell is { } cell:
                _phase = GridOverlayPhase.Subgrid;
                _subcell = null;
                _typed = Trim(_typed, cell: true);
                _target = GridMath.Center(cell);
                Show();
                break;

            case GridOverlayPhase.Subgrid when _column is { } column:
                _phase = GridOverlayPhase.Primary;
                _cell = null;
                _subcell = null;
                _typed = Trim(_typed, cell: false);
                _target = GridMath.Center(column);
                Show();
                break;

            case GridOverlayPhase.Primary when _typed.Length > 0:
                ResetSelection();
                _target = _savedPointer;
                Show();
                break;
        }
    }

    /// <summary>Drops the last two letters of a micro code, or the column letter.</summary>
    private static string Trim(string typed, bool cell)
    {
        int keep = cell ? 2 : 1;
        return typed.Length <= keep ? typed : typed[..keep];
    }

    private void MoveToNextMonitor()
    {
        _monitors ??= new Monitors();
        var next = _monitors.Next(_root.Width > 0 ? GridMath.Center(_root) : _savedPointer);

        _monitor = next;
        _root = next.Bounds;
        ResetSelection();
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
        _phase = GridOverlayPhase.Primary;
        _columnIndex = -1;
        _column = null;
        _cell = null;
        _subcell = null;
        _typed = string.Empty;
        _target = _savedPointer;
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
                _phase,
                _column,
                _cell,
                _subcell,
                _level1Labels,
                _level2Labels,
                _subgridLabels,
                _microLabels,
                _typed,
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
        ParseLevel(_settings.Level1Keys, GridSettings.DefaultLevel1Keys(), out _level1Keys, out _level1Labels);
        ParseLevel(_settings.Level2Keys, GridSettings.DefaultLevel2Keys(), out _level2Keys, out _level2Labels);
        ParseLevel(_settings.SubgridKeys, GridSettings.DefaultSubgridKeys(), out _subgridKeys, out _subgridLabels);
        ParseLevel(_settings.MicroKeys, GridSettings.DefaultMicroKeys(), out _microKeys, out _microLabels);

        _actionLevel = GridSettings.NormalizeActionLevel(_settings.ActionLevel) switch
        {
            "1" => 1,
            "2" => 2,
            "subgrid" => 3,
            _ => 4
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
