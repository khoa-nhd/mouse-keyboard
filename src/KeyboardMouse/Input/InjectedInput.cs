namespace KeyboardMouse.Input;

/// <summary>
/// Marks the input this app sends with SendInput, so its own keyboard hook can
/// tell it apart from keys re-sent by remappers (kanata, AutoHotkey, ...),
/// which arrive flagged as injected too but are the user's real typing.
/// </summary>
public static class InjectedInput
{
    /// <summary>"KMOU", written into dwExtraInfo of every input we send.</summary>
    public const uint Signature = 0x4B4D4F55;
}
