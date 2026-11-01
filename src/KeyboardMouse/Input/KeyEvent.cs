namespace KeyboardMouse.Input;

/// <summary>
/// One physical key transition, as reported by the low-level keyboard hook.
/// </summary>
public readonly record struct KeyEvent(PhysicalKey Key, bool IsDown, bool IsRepeat, long TimestampMs);

/// <summary>
/// Modifier state at the moment of a key event, tracked from the hook itself
/// (never from GetAsyncKeyState) so it matches event order exactly.
/// </summary>
public readonly record struct ModifierState(bool Shift, bool Ctrl, bool Alt, bool Win)
{
    public static readonly ModifierState None = new(false, false, false, false);
}
