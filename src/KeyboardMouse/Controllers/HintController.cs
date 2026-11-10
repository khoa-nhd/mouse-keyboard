using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Hint mode: scan the foreground window in the background, label its clickable
/// elements, and turn a short typed label into a click. Shift at the end means
/// hover only; Ctrl (or holding F, as in PLAN.md) means right click.
/// </summary>
public sealed class HintController : IModeController
{
    private readonly IMouseOutput _mouse;
    private readonly IMonitorService? _monitors;
    private readonly IHintOverlay _overlay;
    private readonly IClickableScanner _scanner;
    private readonly Func<Point> _cursor;
    private readonly Func<IntPtr> _foreground;
    private readonly Action<Action> _dispatch;

    private HintSettings _settings;
    private KeysSettings _keys;
    private PhysicalKey _rightClickKey = PhysicalKey.F;
    private bool _active;
    private bool _scanning;
    private int _scanVersion;
    private readonly List<ClickableTarget> _targets = [];
    private readonly List<ClickableTarget> _deferred = [];
    private readonly HashSet<Rectangle> _known = [];
    private IReadOnlyList<string> _labels = [];
    private string _typed = "";
    private bool _rightClickHeld;

    public HintController(
        IMouseOutput mouse,
        IMonitorService? monitors,
        IHintOverlay overlay,
        IClickableScanner scanner,
        HintSettings settings,
        Func<Point> cursor,
        Func<IntPtr> foreground,
        Action<Action> dispatch,
        KeysSettings? keys = null)
    {
        _mouse = mouse;
        _monitors = monitors;
        _overlay = overlay;
        _scanner = scanner;
        _settings = settings;
        _keys = keys ?? new KeysSettings();
        _rightClickKey = ResolveRightClickKey(_keys.RightClick);
        _cursor = cursor;
        _foreground = foreground;
        _dispatch = dispatch;
    }

    /// <summary>
    /// Label room an incomplete scan starts with: eight symbols give sixty-four
    /// two-letter labels, which covers a typical screen without making a short
    /// list type three letters per hint.
    /// </summary>
    public bool IsActive => _active;

    public void UpdateSettings(HintSettings settings)
    {
        _settings = settings;

        if (_active)
        {
            Close();
        }
    }

    public void UpdateKeys(KeysSettings settings)
    {
        _keys = settings;
        _rightClickKey = ResolveRightClickKey(_keys.RightClick);
    }

    /// <summary>Starts a scan (H from the normal layer) or closes an open hint set.</summary>
    public void Open()
    {
        if (_active)
        {
            Close();
            return;
        }

        _active = true;
        _scanning = true;
        _typed = string.Empty;
        _targets.Clear();
        _deferred.Clear();
        _known.Clear();
        _labels = [];
        _rightClickHeld = false;

        int version = ++_scanVersion;

        // Overlay creation and starting the UIA scan must not run inside the
        // keyboard hook callback. The dispatch target is the UI thread.
        _dispatch(() => StartScan(version));
    }

    private void StartScan(int version)
    {
        if (!_active || !_scanning || version != _scanVersion)
        {
            return;
        }

        if (_monitors is not null && _monitors.All.Count > 0)
        {
            _overlay.ShowScanning(_monitors.GetAtPoint(_cursor()), "Scanning...");
        }

        // Monitor-wide scopes do not need an active window; only the historical
        // foreground scope has nothing to scan without one.
        var scope = ScanScopeNames.Parse(_settings.ScanScope);
        IntPtr window = _foreground();
        if (window == IntPtr.Zero && scope == HintScanScope.ForegroundWindow)
        {
            Close();
            return;
        }

        var options = new HintScanOptions(
            Math.Clamp(_settings.MaxElements, 1, 2000),
            _settings.IncludeTaskbar,
            scope,
            _cursor(),
            _settings.VisualDetection);

        try
        {
            _scanner.BeginScan(
                window,
                options,
                result => _dispatch(() => ApplyScanResult(version, result)),
                () => _dispatch(() => FailScan(version)));
        }
        catch (Exception ex)
        {
            Platform.Log.Error("Could not start the hint scan", ex);
            Close();
        }
    }

    public HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers)
    {
        if (!_active)
        {
            return HandleResult.PassThrough;
        }

        if (_rightClickKey != PhysicalKey.None && keyEvent.Key == _rightClickKey)
        {
            _rightClickHeld = keyEvent.IsDown;
        }

        // Modifiers remain usable for the rest of Windows (and are read from
        // the router's snapshot when the final letter is pressed).
        if (keyEvent.Key.IsModifier())
        {
            return HandleResult.PassThrough;
        }

        // Key-ups are swallowed by the router if their down was swallowed.
        if (!keyEvent.IsDown)
        {
            return HandleResult.PassThrough;
        }

        if (keyEvent.IsRepeat)
        {
            return HandleResult.Swallow;
        }

        if (_scanning)
        {
            if (keyEvent.Key == PhysicalKey.Escape)
            {
                Close();
            }

            return HandleResult.Swallow;
        }

        switch (keyEvent.Key)
        {
            case PhysicalKey.Escape:
                Close();
                return HandleResult.Swallow;

            case PhysicalKey.Backspace:
                RemoveLastLetter();
                return HandleResult.Swallow;
        }

        if (KeyNames.TryGetCharacter(keyEvent.Key, out char character))
        {
            TypeLetter(character, modifiers, keyEvent.Key == _rightClickKey);
        }

        return HandleResult.Swallow;
    }

    public void Reset() => Close();

    /// <summary>
    /// Takes one delivery from the scan. A scan reports early with the windows
    /// that answered first and completes later with the rest, so typing can
    /// start while the slow windows are still reading; late targets are appended
    /// and keep their own new labels, and every label already on screen stays on
    /// its target.
    /// </summary>
    private void ApplyScanResult(int version, HintScanResult result)
    {
        if (!_active || version != _scanVersion)
        {
            return;
        }

        if (result.Targets.Count == 0)
        {
            // An early batch with nothing in it is not a reason to give up; a
            // finished scan with nothing found is.
            if (result.Complete && _targets.Count == 0)
            {
                Close();
            }

            return;
        }

        bool first = _targets.Count == 0;

        if (first)
        {
            // Labels are positional. A scan that still has a window reading sizes
            // its labels for the room the next batches usually need, so the new
            // ones continue the sequence instead of renumbering the hints that
            // are already on screen. A finished scan sizes them exactly.
            int pool = result.Complete
                ? result.Targets.Count
                : Math.Max(result.Targets.Count, PartialLabelReserve);

            _labels = HintLabels.Generate(pool, _settings.Alphabet);
            _scanning = false;
        }

        int added = Merge(result.Targets);

        // A batch that needs longer labels than the ones on screen can only be
        // applied while nothing has been typed; until then it is parked, not
        // dropped, and lands as soon as the typed prefix is empty again.
        if (_deferred.Count > 0 && _typed.Length == 0)
        {
            _labels = HintLabels.Generate(_targets.Count + _deferred.Count, _settings.Alphabet);
            added += DrainDeferred();
        }

        _scanning = false;

        if (added > 0 || first)
        {
            Show();
        }
    }

    /// <summary>Labels kept free for the windows that answer after the first.</summary>
    private const int PartialLabelReserve = 64;

    private int Capacity() => Math.Clamp(_settings.MaxElements, 1, 2000);

    /// <summary>
    /// Appends targets not shown yet, up to the configured cap and the room the
    /// current labels leave. Targets without a free label wait in
    /// <see cref="_deferred"/> so no hint already on screen changes its label.
    /// </summary>
    private int Merge(IReadOnlyList<ClickableTarget> targets)
    {
        int capacity = Capacity();
        int added = 0;

        foreach (var target in targets)
        {
            if (!_known.Add(target.Bounds))
            {
                continue;
            }

            if (_targets.Count >= capacity || _targets.Count >= _labels.Count)
            {
                if (_targets.Count < capacity)
                {
                    _deferred.Add(target);
                }

                continue;
            }

            _targets.Add(target);
            added++;
        }

        return added;
    }

    private int DrainDeferred()
    {
        int added = 0;

        while (_deferred.Count > 0 && _targets.Count < _labels.Count)
        {
            _targets.Add(_deferred[0]);
            _deferred.RemoveAt(0);
            added++;
        }

        return added;
    }

    private void FailScan(int version)
    {
        if (!_active || !_scanning || version != _scanVersion)
        {
            return;
        }

        Close();
    }

    private void TypeLetter(char letter, ModifierState modifiers, bool keyIsF)
    {
        if (_labels.Count == 0)
        {
            return;
        }

        string candidate = _typed + letter;
        bool anyMatch = false;

        for (int i = 0; i < _targets.Count; i++)
        {
            if (_labels[i].StartsWith(candidate, StringComparison.Ordinal))
            {
                anyMatch = true;
                break;
            }
        }

        if (!anyMatch)
        {
            return;
        }

        _typed = candidate;
        int length = _labels[0].Length;

        if (_typed.Length < length)
        {
            Show();
            return;
        }

        int index = -1;
        for (int i = 0; i < _targets.Count; i++)
        {
            if (_labels[i] == _typed)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            Show();
            return;
        }

        Activate(index, modifiers, keyIsF);
    }

    private void Activate(int index, ModifierState modifiers, bool keyIsF)
    {
        var target = _targets[index];

        // The geometry pass moves the click point off the box centre for round,
        // glyph shaped or irregular controls, where the centre is empty pixels.
        var center = target.HitPoint;

        if (_monitors is not null)
        {
            var (x, y) = _monitors.ClampToDesktop(center.X, center.Y);
            center = new Point((int)Math.Round(x), (int)Math.Round(y));
        }

        _mouse.MoveTo(center.X, center.Y);

        bool hover = modifiers.Shift;
        bool rightClick = modifiers.Ctrl ||
            (_rightClickHeld && !keyIsF && _rightClickKey != PhysicalKey.None);

        if (!hover)
        {
            if (rightClick)
            {
                _mouse.RightClick();
            }
            else
            {
                _mouse.LeftClick();
            }
        }

        Close();
    }

    private void RemoveLastLetter()
    {
        if (_typed.Length == 0)
        {
            return;
        }

        _typed = _typed[..^1];

        if (_typed.Length == 0 && _deferred.Count > 0)
        {
            _labels = HintLabels.Generate(_targets.Count + _deferred.Count, _settings.Alphabet);
            DrainDeferred();
        }

        Show();
    }

    private void Show()
    {
        if (_monitors is null)
        {
            _overlay.Hide();
            return;
        }

        int shown = Math.Min(_targets.Count, _labels.Count);
        _overlay.Show(_monitors.All, _targets.Take(shown).ToArray(), _labels.Take(shown).ToArray(), _typed);
    }

    private void Close()
    {
        _scanVersion++;
        _active = false;
        _scanning = false;
        _typed = string.Empty;
        _targets.Clear();
        _deferred.Clear();
        _known.Clear();
        _labels = [];
        _rightClickHeld = false;
        _overlay.Hide();
    }

    private static PhysicalKey ResolveRightClickKey(string? name)
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
            : PhysicalKey.F;
    }
}
