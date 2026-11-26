using KeyboardMouse.Input;

namespace KeyboardMouse.Config;

/// <summary>
/// One editable key binding: a stable id the guide uses, the group it belongs
/// to, and the key it falls back to.
/// </summary>
public sealed record KeyBindingDefinition(
    string Id,
    string Group,
    string DefaultKey,
    int GridIndex = -1,
    int RecursiveIndex = -1,
    string? LeftKey = null)
{
    /// <summary>The default key for the given hand layout.</summary>
    public string DefaultFor(string? hand) =>
        HandLayouts.IsLeft(hand) && LeftKey is not null ? LeftKey : DefaultKey;

    public bool IsGrid => GridIndex >= 0;

    public bool IsRecursiveGrid => RecursiveIndex >= 0;
}

/// <summary>
/// The single source of truth for every editable binding: the activation
/// toggle, the normal-layer actions, the nine cell keys of the recursive grid,
/// and the ten keys the code grid prints in its cells (one key per character
/// of a cell code).
///
/// The normal pool (toggle + <see cref="KeysSettings"/>), the recursive grid
/// pool and the code grid pool are independent, so a grid cell can reuse a
/// normal-layer key. Inside a pool a
/// key belongs to exactly one action: assigning a key that is already taken
/// swaps the two actions instead of creating a duplicate.
/// </summary>
public static class KeyBindingCatalog
{
    public const string ToggleId = "toggle";

    private static readonly KeyBindingDefinition[] Definitions =
    [
        new(ToggleId, "system", "CapsLock"),

        new("up", "move", "I", LeftKey: "W"),
        new("left", "move", "J", LeftKey: "A"),
        new("down", "move", "K", LeftKey: "S"),
        new("right", "move", "L", LeftKey: "D"),

        new("turboLayer", "layer", "A", LeftKey: "Semicolon"),
        new("precisionLayer", "layer", "S", LeftKey: "L"),
        new("scrollLayer", "layer", "D", LeftKey: "K"),

        new("leftClick", "click", "Space", LeftKey: "Space"),
        new("rightClick", "click", "F", LeftKey: "J"),
        new("middleClick", "click", "E", LeftKey: "I"),
        new("doubleClick", "click", "R", LeftKey: "U"),
        new("clickAndExit", "click", "T", LeftKey: "Y"),
        new("dragLock", "click", "V", LeftKey: "M"),
        new("autoClick", "click", "B", LeftKey: "N"),

        new("back", "nav", "U", LeftKey: "Q"),
        new("forward", "nav", "O", LeftKey: "E"),

        new("grid", "jump", "G", LeftKey: "G"),
        new("hint", "jump", "H", LeftKey: "H"),
        new("centerScreen", "jump", "X", LeftKey: "Period"),
        new("centerWindow", "jump", "C", LeftKey: "Comma"),
        new("nextMonitor", "jump", "N", LeftKey: "Slash"),
        new("markSave", "jump", "M", LeftKey: "O"),
        new("markJump", "jump", "Apostrophe", LeftKey: "Apostrophe"),

        new("copy", "edit", "Y", LeftKey: "C"),
        new("paste", "edit", "P", LeftKey: "V"),
        new("undo", "edit", "Z", LeftKey: "Z"),
        new("redo", "edit", "W", LeftKey: "X"),

        new("rgrid0", "recursiveGrid", "U", RecursiveIndex: 0, LeftKey: "Q"),
        new("rgrid1", "recursiveGrid", "I", RecursiveIndex: 1, LeftKey: "W"),
        new("rgrid2", "recursiveGrid", "O", RecursiveIndex: 2, LeftKey: "E"),
        new("rgrid3", "recursiveGrid", "J", RecursiveIndex: 3, LeftKey: "A"),
        new("rgrid4", "recursiveGrid", "K", RecursiveIndex: 4, LeftKey: "S"),
        new("rgrid5", "recursiveGrid", "L", RecursiveIndex: 5, LeftKey: "D"),
        new("rgrid6", "recursiveGrid", "M", RecursiveIndex: 6, LeftKey: "Z"),
        new("rgrid7", "recursiveGrid", "Comma", RecursiveIndex: 7, LeftKey: "X"),
        new("rgrid8", "recursiveGrid", "Period", RecursiveIndex: 8, LeftKey: "C"),

        new("grid0", "grid", "A", 0),
        new("grid1", "grid", "S", 1),
        new("grid2", "grid", "D", 2),
        new("grid3", "grid", "F", 3),
        new("grid4", "grid", "G", 4),
        new("grid5", "grid", "H", 5),
        new("grid6", "grid", "J", 6),
        new("grid7", "grid", "K", 7),
        new("grid8", "grid", "L", 8),
        new("grid9", "grid", "Semicolon", 9)
    ];

    public static IReadOnlyList<KeyBindingDefinition> All => Definitions;

    public static IEnumerable<KeyBindingDefinition> NormalPool =>
        Definitions.Where(definition => !definition.IsGrid && !definition.IsRecursiveGrid);

    public static IEnumerable<KeyBindingDefinition> RecursiveGridPool =>
        Definitions.Where(definition => definition.IsRecursiveGrid);

    public static IEnumerable<KeyBindingDefinition> GridPool =>
        Definitions.Where(definition => definition.IsGrid);

    public static bool TryGet(string? id, out KeyBindingDefinition definition)
    {
        foreach (var candidate in Definitions)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    public static string Get(Settings settings, KeyBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return definition.Id switch
        {
            ToggleId => settings.Activation.ToggleKey,
            "up" => settings.Keys.Up,
            "left" => settings.Keys.Left,
            "down" => settings.Keys.Down,
            "right" => settings.Keys.Right,
            "turboLayer" => settings.Keys.TurboLayer,
            "precisionLayer" => settings.Keys.PrecisionLayer,
            "scrollLayer" => settings.Keys.ScrollLayer,
            "leftClick" => settings.Keys.LeftClick,
            "rightClick" => settings.Keys.RightClick,
            "middleClick" => settings.Keys.MiddleClick,
            "doubleClick" => settings.Keys.DoubleClick,
            "clickAndExit" => settings.Keys.ClickAndExit,
            "dragLock" => settings.Keys.DragLock,
            "autoClick" => settings.Keys.AutoClick,
            "back" => settings.Keys.Back,
            "forward" => settings.Keys.Forward,
            "grid" => settings.Keys.Grid,
            "hint" => settings.Keys.Hint,
            "centerScreen" => settings.Keys.CenterScreen,
            "centerWindow" => settings.Keys.CenterWindow,
            "nextMonitor" => settings.Keys.NextMonitor,
            "markSave" => settings.Keys.MarkSave,
            "markJump" => settings.Keys.MarkJump,
            "copy" => settings.Keys.Copy,
            "paste" => settings.Keys.Paste,
            "undo" => settings.Keys.Undo,
            "redo" => settings.Keys.Redo,
            _ when definition.IsRecursiveGrid => GetRecursiveGrid(settings, definition.RecursiveIndex),
            _ => GetGrid(settings, definition.GridIndex)
        };
    }

    public static string Get(Settings settings, string id) =>
        TryGet(id, out var definition) ? Get(settings, definition) : string.Empty;

    public static bool TryAssign(
        Settings settings,
        string? id,
        string? keyName,
        out string? swappedId)
    {
        swappedId = null;
        if (!TryGet(id, out var definition) ||
            !TryParseBindingKey(keyName, out var key) ||
            ((definition.IsGrid || definition.IsRecursiveGrid) && IsReservedGridKey(key)))
        {
            return false;
        }

        string canonical = KeyNames.Canonical(key);
        string previous = Get(settings, definition);
        Set(settings, definition, canonical);

        foreach (var other in Pool(definition))
        {
            if (other.Id == definition.Id)
            {
                continue;
            }

            string value = Get(settings, other);
            if (TryParseBindingKey(value, out var otherKey) && otherKey == key)
            {
                // The other action receives the key the edited action had. An
                // empty previous value therefore unbinds it, which is the
                // honest result of swapping with an unbound row.
                Set(settings, other, IsUnbound(previous) ? string.Empty : previous);
                swappedId = other.Id;
                break;
            }
        }

        return true;
    }

    public static bool Reset(Settings settings, string? id, out string? swappedId)
    {
        swappedId = null;
        if (!TryGet(id, out var definition))
        {
            return false;
        }

        return TryAssign(settings, id, definition.DefaultFor(settings.Keys?.Hand), out swappedId);
    }

    public static bool Unbind(Settings settings, string? id)
    {
        if (!TryGet(id, out var definition))
        {
            return false;
        }

        Set(settings, definition, string.Empty);
        return true;
    }

    /// <summary>Restores every editable binding to the default key of the current hand layout.</summary>
    public static void ResetAll(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Keys ??= new KeysSettings();
        settings.Grid ??= new GridSettings();
        EnsureGridKeys(settings);
        string hand = settings.Keys.Hand;

        foreach (var definition in Definitions)
        {
            Set(settings, definition, definition.DefaultFor(hand));
        }
    }

    /// <summary>
    /// Switches to the right- or left-hand layout: every mouse-mode key and the
    /// 3 x 3 grid keys move to that layout's defaults. The toggle key and the
    /// code grid keys stay as they are.
    /// </summary>
    public static bool ApplyHand(Settings settings, string? hand)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!HandLayouts.TryParse(hand, out string id))
        {
            return false;
        }

        settings.Keys ??= new KeysSettings();
        settings.Grid ??= new GridSettings();
        EnsureGridKeys(settings);
        settings.Keys.Hand = id;

        foreach (var definition in Definitions)
        {
            if (definition.Id == ToggleId || definition.IsGrid)
            {
                continue;
            }

            Set(settings, definition, definition.DefaultFor(id));
        }

        return true;
    }

    /// <summary>
    /// Canonicalises every binding and removes duplicates. Invalid values,
    /// modifiers and Escape are repaired to the default (Escape is fixed as
    /// the shared way out); an empty value is preserved as deliberately
    /// unbound. When two bindings share a key the first one keeps it; the
    /// later one returns to its default if that is free, otherwise it becomes
    /// unbound.
    /// </summary>
    public static void Normalize(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Activation ??= new ActivationSettings();
        settings.Keys ??= new KeysSettings();
        settings.Grid ??= new GridSettings();

        EnsureGridKeys(settings);
        settings.Keys.Hand = HandLayouts.Normalize(settings.Keys.Hand);
        string hand = settings.Keys.Hand;
        settings.Grid.Style = GridSettings.NormalizeStyle(settings.Grid.Style);
        settings.Grid.MaxDepth = Math.Clamp(
            settings.Grid.MaxDepth,
            GridSettings.MinMaxDepth,
            GridSettings.MaxMaxDepth);

        foreach (var definition in NormalPool)
        {
            NormalizeValue(settings, definition, hand);
        }

        ResolveDuplicates(settings, NormalPool, hand);

        NormalizeGridLevel(settings.Grid.Keys, GridSettings.DefaultKeys());
        NormalizeGridLevel(
            settings.Grid.RecursiveKeys,
            RecursiveGridPool.Select(definition => definition.DefaultFor(hand)).ToArray());
        ClampGridSize(settings.Grid);
    }

    public static bool TryParseBindingKey(string? name, out PhysicalKey key)
    {
        key = PhysicalKey.None;
        if (string.IsNullOrWhiteSpace(name) ||
            !KeyNames.TryParse(name, out key) ||
            key == PhysicalKey.None ||
            key == PhysicalKey.Escape ||
            key.IsModifier())
        {
            key = PhysicalKey.None;
            return false;
        }

        return true;
    }

    private static string GetRecursiveGrid(Settings settings, int index)
    {
        settings.Grid.RecursiveKeys ??= GridSettings.DefaultRecursiveKeys();
        string[] keys = settings.Grid.RecursiveKeys;
        return index >= 0 && index < keys.Length ? keys[index] : string.Empty;
    }

    private static string GetGrid(Settings settings, int index)
    {
        settings.Grid.Keys ??= GridSettings.DefaultKeys();
        string[] keys = settings.Grid.Keys;
        return index >= 0 && index < keys.Length ? keys[index] : string.Empty;
    }

    private static void Set(Settings settings, KeyBindingDefinition definition, string value)
    {
        switch (definition.Id)
        {
            case ToggleId: settings.Activation.ToggleKey = value; break;
            case "up": settings.Keys.Up = value; break;
            case "left": settings.Keys.Left = value; break;
            case "down": settings.Keys.Down = value; break;
            case "right": settings.Keys.Right = value; break;
            case "turboLayer": settings.Keys.TurboLayer = value; break;
            case "precisionLayer": settings.Keys.PrecisionLayer = value; break;
            case "scrollLayer": settings.Keys.ScrollLayer = value; break;
            case "leftClick": settings.Keys.LeftClick = value; break;
            case "rightClick": settings.Keys.RightClick = value; break;
            case "middleClick": settings.Keys.MiddleClick = value; break;
            case "doubleClick": settings.Keys.DoubleClick = value; break;
            case "clickAndExit": settings.Keys.ClickAndExit = value; break;
            case "dragLock": settings.Keys.DragLock = value; break;
            case "autoClick": settings.Keys.AutoClick = value; break;
            case "back": settings.Keys.Back = value; break;
            case "forward": settings.Keys.Forward = value; break;
            case "grid": settings.Keys.Grid = value; break;
            case "hint": settings.Keys.Hint = value; break;
            case "centerScreen": settings.Keys.CenterScreen = value; break;
            case "centerWindow": settings.Keys.CenterWindow = value; break;
            case "nextMonitor": settings.Keys.NextMonitor = value; break;
            case "markSave": settings.Keys.MarkSave = value; break;
            case "markJump": settings.Keys.MarkJump = value; break;
            case "copy": settings.Keys.Copy = value; break;
            case "paste": settings.Keys.Paste = value; break;
            case "undo": settings.Keys.Undo = value; break;
            case "redo": settings.Keys.Redo = value; break;
            default:
                if (definition.RecursiveIndex >= 0 &&
                    settings.Grid.RecursiveKeys is { } recursiveKeys &&
                    definition.RecursiveIndex < recursiveKeys.Length)
                {
                    recursiveKeys[definition.RecursiveIndex] = value;
                    break;
                }

                if (definition.GridIndex >= 0 &&
                    settings.Grid.Keys is { } gridKeys &&
                    definition.GridIndex < gridKeys.Length)
                {
                    gridKeys[definition.GridIndex] = value;
                }

                break;
        }
    }

    private static void NormalizeValue(Settings settings, KeyBindingDefinition definition, string hand)
    {
        string current = Get(settings, definition);
        if (IsUnbound(current))
        {
            Set(settings, definition, string.Empty);
            return;
        }

        if (TryParseBindingKey(current, out var key))
        {
            Set(settings, definition, KeyNames.Canonical(key));
            return;
        }

        Set(settings, definition, definition.DefaultFor(hand));
    }

    private static void ResolveDuplicates(
        Settings settings,
        IEnumerable<KeyBindingDefinition> pool,
        string hand)
    {
        var used = new HashSet<PhysicalKey>();

        foreach (var definition in pool)
        {
            if (!TryParseBindingKey(Get(settings, definition), out var key))
            {
                continue;
            }

            if (used.Add(key))
            {
                continue;
            }

            if (TryParseBindingKey(definition.DefaultFor(hand), out var fallback) &&
                used.Add(fallback))
            {
                Set(settings, definition, KeyNames.Canonical(fallback));
            }
            else
            {
                Set(settings, definition, string.Empty);
            }
        }
    }

    private static void EnsureGridKeys(Settings settings)
    {
        if (settings.Grid.Keys is null ||
            settings.Grid.Keys.Length != GridSettings.DefaultKeys().Length)
        {
            settings.Grid.Keys = GridSettings.DefaultKeys();
        }

        if (settings.Grid.RecursiveKeys is null ||
            settings.Grid.RecursiveKeys.Length != GridSettings.DefaultRecursiveKeys().Length)
        {
            settings.Grid.RecursiveKeys = GridSettings.DefaultRecursiveKeys();
        }
    }

    /// <summary>
    /// A code needs two characters per axis, so the grid can never ask for more
    /// columns or rows than the ten keys can address (ten bands of ten).
    /// </summary>
    private static void ClampGridSize(GridSettings grid)
    {
        grid.Columns = Math.Clamp(
            grid.Columns,
            GridSettings.MinColumns,
            GridSettings.MaxColumns);
        grid.Rows = Math.Clamp(
            grid.Rows,
            GridSettings.MinRows,
            GridSettings.MaxRows);
    }

    private static void NormalizeGridLevel(string[] keys, string[] defaults)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            string current = keys[i] ?? string.Empty;
            if (IsUnbound(current))
            {
                keys[i] = string.Empty;
                continue;
            }

            if (TryParseBindingKey(current, out var key) && !IsReservedGridKey(key))
            {
                keys[i] = KeyNames.Canonical(key);
            }
            else
            {
                keys[i] = defaults[i];
            }
        }

        ResolveGridDuplicates(keys, defaults);
    }

    private static void ResolveGridDuplicates(string[] keys, string[] defaults)
    {
        var used = new HashSet<PhysicalKey>();

        for (int i = 0; i < keys.Length; i++)
        {
            if (!TryParseBindingKey(keys[i], out var key) || IsReservedGridKey(key))
            {
                continue;
            }

            if (used.Add(key))
            {
                continue;
            }

            if (TryParseBindingKey(defaults[i], out var fallback) &&
                !IsReservedGridKey(fallback) &&
                used.Add(fallback))
            {
                keys[i] = KeyNames.Canonical(fallback);
            }
            else
            {
                keys[i] = string.Empty;
            }
        }
    }

    private static bool IsReservedGridKey(PhysicalKey key) =>
        key is PhysicalKey.None
            or PhysicalKey.Escape
            or PhysicalKey.Backspace
            or PhysicalKey.Space
            or PhysicalKey.Enter
            or PhysicalKey.Tab
            or PhysicalKey.CapsLock ||
        key.IsModifier();

    private static IEnumerable<KeyBindingDefinition> Pool(KeyBindingDefinition definition) =>
        definition.IsGrid ? GridPool
            : definition.IsRecursiveGrid ? RecursiveGridPool
            : NormalPool;

    private static bool IsUnbound(string? value) =>
        string.IsNullOrWhiteSpace(value);
}

/// <summary>
/// The two built-in layouts. Right hand (the default) moves with I J K L and
/// keeps the layers under the left hand; left hand moves with W A S D and
/// mirrors everything else onto the right hand.
/// </summary>
public static class HandLayouts
{
    public const string Right = "right";
    public const string Left = "left";

    public static bool IsLeft(string? hand) =>
        string.Equals(hand?.Trim(), Left, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? hand) => IsLeft(hand) ? Left : Right;

    public static bool TryParse(string? hand, out string id)
    {
        string value = hand?.Trim() ?? string.Empty;
        if (string.Equals(value, Left, StringComparison.OrdinalIgnoreCase))
        {
            id = Left;
            return true;
        }

        if (string.Equals(value, Right, StringComparison.OrdinalIgnoreCase))
        {
            id = Right;
            return true;
        }

        id = Right;
        return false;
    }
}
