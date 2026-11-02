using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Grid mode: three levels of 3×3 subdivision. Each cell key moves the pointer
/// to the cell center and makes that cell the next grid. Space/F click and
/// close, G keeps the position and closes, Backspace goes up a level, N moves
/// the grid to the next monitor, Esc restores the original pointer position.
/// </summary>
public sealed class GridController : IModeController
{
    private readonly IMouseOutput _mouse;
    private readonly Func<Point> _cursor;
    private readonly IGridOverlay _overlay;
    private readonly Action<Action> _dispatch;
    private readonly List<Rectangle> _parents = new();

    private IMonitorService? _monitors;
    private GridSettings _settings;
    private PhysicalKey[] _cellKeys = [];
    private string[] _labels = [];
    private bool _active;
    private Point _savedPointer;
    private MonitorInfo? _monitor;
    private Rectangle _rect;
    private int _depth;

    public GridController(
        IMouseOutput mouse,
        IMonitorService? monitors,
        IGridOverlay overlay,
        GridSettings settings,
        Func<Point>? cursor = null,
        Action<Action>? dispatch = null)
    {
        _mouse = mouse;
        _monitors = monitors;
        _overlay = overlay;
        _settings = settings;
        _cursor = cursor ?? Pointer.Position;
        _dispatch = dispatch ?? (action => action());
        ParseSettings();
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
        _rect = _monitor.Bounds;
        _parents.Clear();
        _depth = 0;
        _active = true;
        Show();
    }

    public HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers)
    {
        if (!_active)
        {
            return HandleResult.PassThrough;
        }

        // Modifiers keep passing through so the user can hold Ctrl/Shift for a
        // modified click inside grid mode.
        if (keyEvent.Key.IsModifier())
        {
            return HandleResult.PassThrough;
        }

        if (!keyEvent.IsDown || keyEvent.IsRepeat)
        {
            return HandleResult.Swallow;
        }

        switch (keyEvent.Key)
        {
            case PhysicalKey.Escape:
                Cancel();
                return HandleResult.Swallow;

            case PhysicalKey.Backspace:
                GoUp();
                return HandleResult.Swallow;

            case PhysicalKey.G:
                Close(restore: false);
                return HandleResult.Swallow;

            case PhysicalKey.N:
                MoveToNextMonitor();
                return HandleResult.Swallow;

            case PhysicalKey.Space:
                _mouse.LeftClick();
                Close(restore: false);
                return HandleResult.Swallow;

            case PhysicalKey.F:
                _mouse.RightClick();
                Close(restore: false);
                return HandleResult.Swallow;
        }

        int cell = FindCell(keyEvent.Key);
        if (cell >= 0)
        {
            SelectCell(cell);
        }

        return HandleResult.Swallow;
    }

    private void SelectCell(int cellIndex)
    {
        var cell = GridMath.SubRect(_rect, cellIndex);
        if (cell.Width < 1 || cell.Height < 1)
        {
            return;
        }

        _parents.Add(_rect);
        _rect = cell;
        _depth++;

        var center = GridMath.Center(_rect);
        _mouse.MoveTo(center.X, center.Y);

        int maxDepth = Math.Clamp(_settings.MaxDepth, 1, 5);
        if (_depth >= maxDepth || _rect.Width <= 2 || _rect.Height <= 2)
        {
            Close(restore: false);
        }
        else
        {
            Show();
        }
    }

    private void GoUp()
    {
        if (_parents.Count == 0)
        {
            return;
        }

        _rect = _parents[^1];
        _parents.RemoveAt(_parents.Count - 1);
        _depth = Math.Max(0, _depth - 1);

        var center = GridMath.Center(_rect);
        _mouse.MoveTo(center.X, center.Y);
        Show();
    }

    private void MoveToNextMonitor()
    {
        _monitors ??= new Monitors();
        var next = _monitors.Next(GridMath.Center(_rect));

        _monitor = next;
        _rect = next.Bounds;
        _parents.Clear();
        _depth = 0;

        var center = GridMath.Center(_rect);
        _mouse.MoveTo(center.X, center.Y);
        Show();
    }

    private void Cancel()
    {
        _mouse.MoveTo(_savedPointer.X, _savedPointer.Y);
        Close(restore: false);
    }

    private void Close(bool restore)
    {
        _overlay.Hide();
        _active = false;
        _parents.Clear();
        _depth = 0;

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

        // Rendering the overlay is a full-monitor bitmap job; running it inside
        // the keyboard hook would risk the low-level hook timeout, so hand it
        // to the dispatcher like the hint layer does.
        _dispatch(() =>
        {
            if (_active && _monitor is not null)
            {
                _overlay.Show(
                    _monitor,
                    _rect,
                    _labels,
                    _depth,
                    Math.Clamp(_settings.MaxDepth, 1, 5));
            }
        });
    }

    public void Reset()
    {
        if (_active)
        {
            Close(restore: false);
        }
    }

    private int FindCell(PhysicalKey key)
    {
        for (int i = 0; i < _cellKeys.Length; i++)
        {
            if (_cellKeys[i] == key)
            {
                return i;
            }
        }

        return -1;
    }

    private void ParseSettings()
    {
        var names = _settings.Keys;
        var parsed = new PhysicalKey[GridMath.Cells];
        bool valid = names is { Length: GridMath.Cells };

        if (valid)
        {
            var seen = new HashSet<PhysicalKey>();
            for (int i = 0; i < names.Length; i++)
            {
                if (!KeyNames.TryParse(names[i], out var key) || !seen.Add(key))
                {
                    valid = false;
                    break;
                }

                parsed[i] = key;
            }
        }

        if (!valid)
        {
            var defaults = new GridSettings().Keys;
            for (int i = 0; i < parsed.Length; i++)
            {
                KeyNames.TryParse(defaults[i], out parsed[i]);
            }
        }

        _cellKeys = parsed;
        _labels = parsed.Select(KeyNames.Display).ToArray();
    }
}
