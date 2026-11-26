namespace KeyboardMouse.Core;

/// <summary>How hint labels look. <see cref="Classic"/> is the original yellow tag.</summary>
public enum HintTheme
{
    Classic,
    Bubble,
    BlueOutline,
    Glass,
    Badge,
    Tag,
    Purple,
    Keycap
}

/// <summary>How the 3 x 3 grid looks. <see cref="Classic"/> is the original tinted grid.</summary>
public enum GridTheme
{
    Classic,
    Frosted,
    Spotlight,
    Thin,
    Cards,
    Bubble,
    Keyboard,
    Glow
}

/// <summary>
/// Ids stored in settings.json. Unknown or missing values read as the classic
/// look, so an old or hand-edited file never fails to load or changes the look.
/// </summary>
public static class OverlayThemeNames
{
    private static readonly (HintTheme Theme, string Id)[] HintIds =
    [
        (HintTheme.Classic, "classic"),
        (HintTheme.Bubble, "bubble"),
        (HintTheme.BlueOutline, "blueOutline"),
        (HintTheme.Glass, "glass"),
        (HintTheme.Badge, "badge"),
        (HintTheme.Tag, "tag"),
        (HintTheme.Purple, "purple"),
        (HintTheme.Keycap, "keycap")
    ];

    private static readonly (GridTheme Theme, string Id)[] GridIds =
    [
        (GridTheme.Classic, "classic"),
        (GridTheme.Frosted, "frosted"),
        (GridTheme.Spotlight, "spotlight"),
        (GridTheme.Thin, "thin"),
        (GridTheme.Cards, "cards"),
        (GridTheme.Bubble, "bubble"),
        (GridTheme.Keyboard, "keyboard"),
        (GridTheme.Glow, "glow")
    ];

    public const string Classic = "classic";

    public static IReadOnlyList<string> HintThemeIds => HintIds.Select(item => item.Id).ToArray();

    public static IReadOnlyList<string> GridThemeIds => GridIds.Select(item => item.Id).ToArray();

    public static string ToId(HintTheme theme) =>
        HintIds.FirstOrDefault(item => item.Theme == theme).Id ?? Classic;

    public static string ToId(GridTheme theme) =>
        GridIds.FirstOrDefault(item => item.Theme == theme).Id ?? Classic;

    public static HintTheme ParseHint(string? id)
    {
        foreach (var (theme, name) in HintIds)
        {
            if (string.Equals(name, id?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return HintTheme.Classic;
    }

    public static GridTheme ParseGrid(string? id)
    {
        foreach (var (theme, name) in GridIds)
        {
            if (string.Equals(name, id?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return GridTheme.Classic;
    }

    public static string NormalizeHint(string? id) => ToId(ParseHint(id));

    public static string NormalizeGrid(string? id) => ToId(ParseGrid(id));
}
