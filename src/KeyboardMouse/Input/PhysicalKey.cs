namespace KeyboardMouse.Input;

/// <summary>
/// A key identified by its physical scan code (set 1), independent of the
/// active keyboard layout.
/// </summary>
public enum PhysicalKey
{
    None = 0,

    Escape,
    D1, D2, D3, D4, D5, D6, D7, D8, D9, D0,
    Minus,
    Equal,
    Backspace,
    Tab,
    Enter,

    Q, W, E, R, T, Y, U, I, O, P,
    LeftBracket,
    RightBracket,

    LeftCtrl,
    RightCtrl,

    A, S, D, F, G, H, J, K, L,
    Semicolon,
    Apostrophe,
    Backtick,

    LeftShift,
    Backslash,

    Z, X, C, V, B, N, M,
    Comma,
    Period,
    Slash,
    RightShift,

    NumpadMultiply,
    LeftAlt,
    RightAlt,
    Space,
    CapsLock,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10,
    NumLock,
    ScrollLock,
    F11,
    F12,

    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4,
    Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    NumpadDecimal,
    NumpadPlus,
    NumpadMinus,
    NumpadDivide,
    NumpadEnter,

    PrintScreen,
    LeftWin,
    RightWin,
    Apps,

    Insert,
    Delete,
    Home,
    End,
    PageUp,
    PageDown,

    Up,
    Down,
    Left,
    Right,

    Oem102
}

public static class PhysicalKeyExtensions
{
    /// <summary>
    /// Maps a set-1 scan code (plus the extended flag) to a physical key.
    /// Unknown codes return <see cref="PhysicalKey.None"/> and pass through.
    /// </summary>
    public static PhysicalKey FromScanCode(uint scanCode, bool extended)
    {
        if (extended)
        {
            return scanCode switch
            {
                0x1C => PhysicalKey.NumpadEnter,
                0x1D => PhysicalKey.RightCtrl,
                0x35 => PhysicalKey.NumpadDivide,
                0x37 => PhysicalKey.PrintScreen,
                0x38 => PhysicalKey.RightAlt,
                0x47 => PhysicalKey.Home,
                0x48 => PhysicalKey.Up,
                0x49 => PhysicalKey.PageUp,
                0x4B => PhysicalKey.Left,
                0x4D => PhysicalKey.Right,
                0x4F => PhysicalKey.End,
                0x50 => PhysicalKey.Down,
                0x51 => PhysicalKey.PageDown,
                0x52 => PhysicalKey.Insert,
                0x53 => PhysicalKey.Delete,
                0x5B => PhysicalKey.LeftWin,
                0x5C => PhysicalKey.RightWin,
                0x5D => PhysicalKey.Apps,
                _ => PhysicalKey.None
            };
        }

        return scanCode switch
        {
            0x01 => PhysicalKey.Escape,
            0x02 => PhysicalKey.D1,
            0x03 => PhysicalKey.D2,
            0x04 => PhysicalKey.D3,
            0x05 => PhysicalKey.D4,
            0x06 => PhysicalKey.D5,
            0x07 => PhysicalKey.D6,
            0x08 => PhysicalKey.D7,
            0x09 => PhysicalKey.D8,
            0x0A => PhysicalKey.D9,
            0x0B => PhysicalKey.D0,
            0x0C => PhysicalKey.Minus,
            0x0D => PhysicalKey.Equal,
            0x0E => PhysicalKey.Backspace,
            0x0F => PhysicalKey.Tab,

            0x10 => PhysicalKey.Q,
            0x11 => PhysicalKey.W,
            0x12 => PhysicalKey.E,
            0x13 => PhysicalKey.R,
            0x14 => PhysicalKey.T,
            0x15 => PhysicalKey.Y,
            0x16 => PhysicalKey.U,
            0x17 => PhysicalKey.I,
            0x18 => PhysicalKey.O,
            0x19 => PhysicalKey.P,
            0x1A => PhysicalKey.LeftBracket,
            0x1B => PhysicalKey.RightBracket,

            0x1C => PhysicalKey.Enter,
            0x1D => PhysicalKey.LeftCtrl,

            0x1E => PhysicalKey.A,
            0x1F => PhysicalKey.S,
            0x20 => PhysicalKey.D,
            0x21 => PhysicalKey.F,
            0x22 => PhysicalKey.G,
            0x23 => PhysicalKey.H,
            0x24 => PhysicalKey.J,
            0x25 => PhysicalKey.K,
            0x26 => PhysicalKey.L,
            0x27 => PhysicalKey.Semicolon,
            0x28 => PhysicalKey.Apostrophe,
            0x29 => PhysicalKey.Backtick,

            0x2A => PhysicalKey.LeftShift,
            0x2B => PhysicalKey.Backslash,

            0x2C => PhysicalKey.Z,
            0x2D => PhysicalKey.X,
            0x2E => PhysicalKey.C,
            0x2F => PhysicalKey.V,
            0x30 => PhysicalKey.B,
            0x31 => PhysicalKey.N,
            0x32 => PhysicalKey.M,
            0x33 => PhysicalKey.Comma,
            0x34 => PhysicalKey.Period,
            0x35 => PhysicalKey.Slash,
            0x36 => PhysicalKey.RightShift,

            0x37 => PhysicalKey.NumpadMultiply,
            0x38 => PhysicalKey.LeftAlt,
            0x39 => PhysicalKey.Space,
            0x3A => PhysicalKey.CapsLock,

            0x3B => PhysicalKey.F1,
            0x3C => PhysicalKey.F2,
            0x3D => PhysicalKey.F3,
            0x3E => PhysicalKey.F4,
            0x3F => PhysicalKey.F5,
            0x40 => PhysicalKey.F6,
            0x41 => PhysicalKey.F7,
            0x42 => PhysicalKey.F8,
            0x43 => PhysicalKey.F9,
            0x44 => PhysicalKey.F10,

            0x45 => PhysicalKey.NumLock,
            0x46 => PhysicalKey.ScrollLock,

            0x47 => PhysicalKey.Numpad7,
            0x48 => PhysicalKey.Numpad8,
            0x49 => PhysicalKey.Numpad9,
            0x4A => PhysicalKey.NumpadMinus,
            0x4B => PhysicalKey.Numpad4,
            0x4C => PhysicalKey.Numpad5,
            0x4D => PhysicalKey.Numpad6,
            0x4E => PhysicalKey.NumpadPlus,
            0x4F => PhysicalKey.Numpad1,
            0x50 => PhysicalKey.Numpad2,
            0x51 => PhysicalKey.Numpad3,
            0x52 => PhysicalKey.Numpad0,
            0x53 => PhysicalKey.NumpadDecimal,

            0x56 => PhysicalKey.Oem102,
            0x57 => PhysicalKey.F11,
            0x58 => PhysicalKey.F12,

            0x5B => PhysicalKey.LeftWin,
            0x5C => PhysicalKey.RightWin,
            0x5D => PhysicalKey.Apps,

            _ => PhysicalKey.None
        };
    }

    public static bool IsModifier(this PhysicalKey key) => key is
        PhysicalKey.LeftShift or PhysicalKey.RightShift or
        PhysicalKey.LeftCtrl or PhysicalKey.RightCtrl or
        PhysicalKey.LeftAlt or PhysicalKey.RightAlt or
        PhysicalKey.LeftWin or PhysicalKey.RightWin;

    /// <summary>
    /// Reverse mapping used by KeyOutput. Returns scan code 0 for keys that
    /// cannot be synthesized.
    /// </summary>
    public static (ushort ScanCode, bool Extended) ToScanCode(this PhysicalKey key) => key switch
    {
        PhysicalKey.Escape => (0x01, false),
        PhysicalKey.D1 => (0x02, false),
        PhysicalKey.D2 => (0x03, false),
        PhysicalKey.D3 => (0x04, false),
        PhysicalKey.D4 => (0x05, false),
        PhysicalKey.D5 => (0x06, false),
        PhysicalKey.D6 => (0x07, false),
        PhysicalKey.D7 => (0x08, false),
        PhysicalKey.D8 => (0x09, false),
        PhysicalKey.D9 => (0x0A, false),
        PhysicalKey.D0 => (0x0B, false),
        PhysicalKey.Minus => (0x0C, false),
        PhysicalKey.Equal => (0x0D, false),
        PhysicalKey.Backspace => (0x0E, false),
        PhysicalKey.Tab => (0x0F, false),
        PhysicalKey.Enter => (0x1C, false),

        PhysicalKey.Q => (0x10, false),
        PhysicalKey.W => (0x11, false),
        PhysicalKey.E => (0x12, false),
        PhysicalKey.R => (0x13, false),
        PhysicalKey.T => (0x14, false),
        PhysicalKey.Y => (0x15, false),
        PhysicalKey.U => (0x16, false),
        PhysicalKey.I => (0x17, false),
        PhysicalKey.O => (0x18, false),
        PhysicalKey.P => (0x19, false),
        PhysicalKey.LeftBracket => (0x1A, false),
        PhysicalKey.RightBracket => (0x1B, false),

        PhysicalKey.LeftCtrl => (0x1D, false),
        PhysicalKey.A => (0x1E, false),
        PhysicalKey.S => (0x1F, false),
        PhysicalKey.D => (0x20, false),
        PhysicalKey.F => (0x21, false),
        PhysicalKey.G => (0x22, false),
        PhysicalKey.H => (0x23, false),
        PhysicalKey.J => (0x24, false),
        PhysicalKey.K => (0x25, false),
        PhysicalKey.L => (0x26, false),
        PhysicalKey.Semicolon => (0x27, false),
        PhysicalKey.Apostrophe => (0x28, false),
        PhysicalKey.Backtick => (0x29, false),

        PhysicalKey.LeftShift => (0x2A, false),
        PhysicalKey.Backslash => (0x2B, false),
        PhysicalKey.Z => (0x2C, false),
        PhysicalKey.X => (0x2D, false),
        PhysicalKey.C => (0x2E, false),
        PhysicalKey.V => (0x2F, false),
        PhysicalKey.B => (0x30, false),
        PhysicalKey.N => (0x31, false),
        PhysicalKey.M => (0x32, false),
        PhysicalKey.Comma => (0x33, false),
        PhysicalKey.Period => (0x34, false),
        PhysicalKey.Slash => (0x35, false),
        PhysicalKey.RightShift => (0x36, false),

        PhysicalKey.NumpadMultiply => (0x37, false),
        PhysicalKey.LeftAlt => (0x38, false),
        PhysicalKey.Space => (0x39, false),
        PhysicalKey.CapsLock => (0x3A, false),

        PhysicalKey.F1 => (0x3B, false),
        PhysicalKey.F2 => (0x3C, false),
        PhysicalKey.F3 => (0x3D, false),
        PhysicalKey.F4 => (0x3E, false),
        PhysicalKey.F5 => (0x3F, false),
        PhysicalKey.F6 => (0x40, false),
        PhysicalKey.F7 => (0x41, false),
        PhysicalKey.F8 => (0x42, false),
        PhysicalKey.F9 => (0x43, false),
        PhysicalKey.F10 => (0x44, false),

        PhysicalKey.NumLock => (0x45, false),
        PhysicalKey.ScrollLock => (0x46, false),
        PhysicalKey.Numpad7 => (0x47, false),
        PhysicalKey.Numpad8 => (0x48, false),
        PhysicalKey.Numpad9 => (0x49, false),
        PhysicalKey.NumpadMinus => (0x4A, false),
        PhysicalKey.Numpad4 => (0x4B, false),
        PhysicalKey.Numpad5 => (0x4C, false),
        PhysicalKey.Numpad6 => (0x4D, false),
        PhysicalKey.NumpadPlus => (0x4E, false),
        PhysicalKey.Numpad1 => (0x4F, false),
        PhysicalKey.Numpad2 => (0x50, false),
        PhysicalKey.Numpad3 => (0x51, false),
        PhysicalKey.Numpad0 => (0x52, false),
        PhysicalKey.NumpadDecimal => (0x53, false),

        PhysicalKey.Oem102 => (0x56, false),
        PhysicalKey.F11 => (0x57, false),
        PhysicalKey.F12 => (0x58, false),

        PhysicalKey.NumpadEnter => (0x1C, true),
        PhysicalKey.RightCtrl => (0x1D, true),
        PhysicalKey.NumpadDivide => (0x35, true),
        PhysicalKey.PrintScreen => (0x37, true),
        PhysicalKey.RightAlt => (0x38, true),
        PhysicalKey.Home => (0x47, true),
        PhysicalKey.Up => (0x48, true),
        PhysicalKey.PageUp => (0x49, true),
        PhysicalKey.Left => (0x4B, true),
        PhysicalKey.Right => (0x4D, true),
        PhysicalKey.End => (0x4F, true),
        PhysicalKey.Down => (0x50, true),
        PhysicalKey.PageDown => (0x51, true),
        PhysicalKey.Insert => (0x52, true),
        PhysicalKey.Delete => (0x53, true),
        PhysicalKey.LeftWin => (0x5B, true),
        PhysicalKey.RightWin => (0x5C, true),
        PhysicalKey.Apps => (0x5D, true),

        _ => (0, false)
    };
}
