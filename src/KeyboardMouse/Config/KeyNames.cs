using KeyboardMouse.Input;

namespace KeyboardMouse.Config;

/// <summary>
/// Parses key names from settings.json to physical keys and back to compact
/// display labels. Enum names are accepted case-insensitively ("I", "Space",
/// "Apostrophe") plus common punctuation aliases.
/// </summary>
public static class KeyNames
{
    public static bool TryParse(string? name, out PhysicalKey key)
    {
        key = PhysicalKey.None;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string text = name.Trim();

        key = text switch
        {
            ";" => PhysicalKey.Semicolon,
            "," => PhysicalKey.Comma,
            "." => PhysicalKey.Period,
            "'" => PhysicalKey.Apostrophe,
            "`" => PhysicalKey.Backtick,
            "[" => PhysicalKey.LeftBracket,
            "]" => PhysicalKey.RightBracket,
            "-" => PhysicalKey.Minus,
            "=" => PhysicalKey.Equal,
            "/" => PhysicalKey.Slash,
            "\\" => PhysicalKey.Backslash,
            _ => PhysicalKey.None
        };

        if (key != PhysicalKey.None)
        {
            return true;
        }

        // Never accept a numeric string: Enum.TryParse would turn "5" into an
        // arbitrary enum value.
        if (int.TryParse(text, out _))
        {
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && key != PhysicalKey.None;
    }

    public static string Display(PhysicalKey key) => key switch
    {
        PhysicalKey.D1 => "1",
        PhysicalKey.D2 => "2",
        PhysicalKey.D3 => "3",
        PhysicalKey.D4 => "4",
        PhysicalKey.D5 => "5",
        PhysicalKey.D6 => "6",
        PhysicalKey.D7 => "7",
        PhysicalKey.D8 => "8",
        PhysicalKey.D9 => "9",
        PhysicalKey.D0 => "0",
        PhysicalKey.Minus => "-",
        PhysicalKey.Equal => "=",
        PhysicalKey.LeftBracket => "[",
        PhysicalKey.RightBracket => "]",
        PhysicalKey.Semicolon => ";",
        PhysicalKey.Apostrophe => "'",
        PhysicalKey.Backtick => "`",
        PhysicalKey.Backslash => "\\",
        PhysicalKey.Comma => ",",
        PhysicalKey.Period => ".",
        PhysicalKey.Slash => "/",
        PhysicalKey.Space => "Space",
        PhysicalKey.Escape => "Esc",
        PhysicalKey.PageUp => "PgUp",
        PhysicalKey.PageDown => "PgDn",
        PhysicalKey.LeftCtrl => "LCtrl",
        PhysicalKey.RightCtrl => "RCtrl",
        PhysicalKey.LeftShift => "LShift",
        PhysicalKey.RightShift => "RShift",
        PhysicalKey.LeftAlt => "LAlt",
        PhysicalKey.RightAlt => "RAlt",
        _ => key.ToString()
    };

    /// <summary>
    /// Maps letter keys (A–Z) to the lowercase character used by marks and
    /// hint labels. Everything else returns false.
    /// </summary>
    public static bool TryGetLetter(PhysicalKey key, out char letter)
    {
        string display = Display(key);
        if (display.Length == 1 && display[0] is >= 'A' and <= 'Z')
        {
            letter = char.ToLowerInvariant(display[0]);
            return true;
        }

        letter = '\0';
        return false;
    }

    /// <summary>
    /// Maps a key to the character it types for hint labels. Unlike marks this
    /// also accepts the punctuation in the default home-row hint alphabet.
    /// </summary>
    public static bool TryGetCharacter(PhysicalKey key, out char character)
    {
        if (TryGetLetter(key, out character))
        {
            return true;
        }

        character = key switch
        {
            PhysicalKey.Semicolon => ';',
            PhysicalKey.Apostrophe => '\'',
            PhysicalKey.Comma => ',',
            PhysicalKey.Period => '.',
            PhysicalKey.Slash => '/',
            PhysicalKey.Minus => '-',
            PhysicalKey.Equal => '=',
            PhysicalKey.LeftBracket => '[',
            PhysicalKey.RightBracket => ']',
            PhysicalKey.Backslash => '\\',
            PhysicalKey.Backtick => '`',
            _ => '\0'
        };

        return character != '\0';
    }
}
