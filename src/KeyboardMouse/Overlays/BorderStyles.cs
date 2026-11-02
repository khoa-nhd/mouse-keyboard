namespace KeyboardMouse.Overlays;

/// <summary>How the screen frame around each monitor is drawn.</summary>
public enum ScreenBorderStyle
{
    Frame,
    Glow,
    Corners,
    None
}

/// <summary>Parses and names <see cref="ScreenBorderStyle"/> values for the settings file and the UI.</summary>
public static class BorderStyles
{
    private static readonly (ScreenBorderStyle Style, string Id, string Name)[] Entries =
    {
        (ScreenBorderStyle.Frame, "frame", "Frame"),
        (ScreenBorderStyle.Glow, "glow", "Glow"),
        (ScreenBorderStyle.Corners, "corners", "Corners"),
        (ScreenBorderStyle.None, "none", "None")
    };

    public static IReadOnlyList<ScreenBorderStyle> All { get; } = Entries.Select(e => e.Style).ToArray();

    public static string ToId(ScreenBorderStyle style)
    {
        foreach (var entry in Entries)
        {
            if (entry.Style == style)
            {
                return entry.Id;
            }
        }

        return "frame";
    }

    public static string DisplayName(ScreenBorderStyle style)
    {
        foreach (var entry in Entries)
        {
            if (entry.Style == style)
            {
                return entry.Name;
            }
        }

        return "Frame";
    }

    public static ScreenBorderStyle ParseOrDefault(string? value) =>
        TryParse(value, out var style) ? style : ScreenBorderStyle.Frame;

    public static bool TryParse(string? value, out ScreenBorderStyle style)
    {
        style = ScreenBorderStyle.Frame;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                style = entry.Style;
                return true;
            }
        }

        return false;
    }
}
