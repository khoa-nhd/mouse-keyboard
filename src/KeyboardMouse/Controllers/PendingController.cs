using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;

namespace KeyboardMouse.Controllers;

/// <summary>
/// Handles the two-key mark commands. M (or the configured save key) opens a
/// pending state; the next letter saves the pointer under that letter. ' opens
/// the same state for jumping. Any other key cancels, and the state also
/// expires after two seconds so a forgotten M never eats the next keystroke.
/// </summary>
public sealed class PendingController : IModeController
{
    public const int DefaultTimeoutMs = 2000;

    private enum PendingKind
    {
        None,
        Save,
        Jump
    }

    private readonly IMouseOutput _mouse;
    private readonly IMarkStore _marks;
    private readonly Func<Point> _cursor;
    private readonly IMonitorService? _monitors;
    private readonly Func<long> _clock;

    private PendingKind _kind;
    private long _startedAt;

    public PendingController(
        IMouseOutput mouse,
        IMarkStore marks,
        Func<Point>? cursor = null,
        IMonitorService? monitors = null,
        Func<long>? clock = null)
    {
        _mouse = mouse;
        _marks = marks;
        _cursor = cursor ?? Pointer.Position;
        _monitors = monitors;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>Raised when the pending state starts or ends (timer management).</summary>
    public event Action? Changed;

    public bool IsActive => _kind != PendingKind.None;

    public void BeginSave() => Begin(PendingKind.Save);

    public void BeginJump() => Begin(PendingKind.Jump);

    public HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers)
    {
        if (!IsActive)
        {
            return HandleResult.PassThrough;
        }

        // Let modifiers through; they neither complete nor cancel the command.
        if (keyEvent.Key.IsModifier())
        {
            return HandleResult.PassThrough;
        }

        if (!keyEvent.IsDown)
        {
            return HandleResult.PassThrough;
        }

        if (keyEvent.IsRepeat)
        {
            // Holding the key that opened the pending state must not pick its
            // own letter. Ignore repeats and let the timeout do its job.
            return HandleResult.Swallow;
        }

        if (TryGetLetter(keyEvent.Key, out char letter))
        {
            Complete(letter);
            return HandleResult.Swallow;
        }

        Cancel();
        return HandleResult.PassThrough;
    }

    /// <summary>Clears an expired pending state. Called from the UI poll timer.</summary>
    public void Tick(long now)
    {
        if (!IsActive)
        {
            return;
        }

        if (now - _startedAt < DefaultTimeoutMs)
        {
            return;
        }

        Cancel();
    }

    public void Reset() => Cancel();

    private void Begin(PendingKind kind)
    {
        _kind = kind;
        _startedAt = _clock();
        Changed?.Invoke();
    }

    private void Complete(char letter)
    {
        var kind = _kind;
        _kind = PendingKind.None;
        Changed?.Invoke();

        if (kind == PendingKind.Save)
        {
            _marks.Set(letter, _cursor());
            return;
        }

        if (!_marks.TryGet(letter, out var position))
        {
            return;
        }

        if (_monitors is not null)
        {
            var (x, y) = _monitors.ClampToDesktop(position.X, position.Y);
            position = new Point((int)Math.Round(x), (int)Math.Round(y));
        }

        _mouse.MoveTo(position.X, position.Y);
    }

    private void Cancel()
    {
        if (!IsActive)
        {
            return;
        }

        _kind = PendingKind.None;
        Changed?.Invoke();
    }

    private static bool TryGetLetter(PhysicalKey key, out char letter) =>
        KeyNames.TryGetLetter(key, out letter);
}
