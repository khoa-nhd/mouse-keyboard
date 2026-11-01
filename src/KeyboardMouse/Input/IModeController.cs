namespace KeyboardMouse.Input;

public enum HandleResult
{
    PassThrough,
    Swallow
}

/// <summary>
/// A mode-specific key handler (normal, grid, hints, pending mark).
/// Controllers are consulted in priority order while mouse mode is on.
/// </summary>
public interface IModeController
{
    bool IsActive { get; }

    HandleResult Handle(KeyEvent keyEvent, ModifierState modifiers);

    /// <summary>Release any state this controller owns (called when mode turns off).</summary>
    void Reset();
}
