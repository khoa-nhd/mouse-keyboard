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
    int GridIndex = -1)
{
    public bool IsGrid => GridIndex >= 0;
}

/// <summary>
/// The single source of truth for every editable binding: the activation
/// toggle, the normal-layer actions, and the nine grid cells.
///
/// The normal pool (toggle + <see cref="KeysSettings"/>) and the grid pool are
/// independent, so a grid cell can reuse a normal-layer key. Inside a pool a
/// key belongs to exactly one action: assigning a key that is already taken
/// swaps the two actions instead of creating a duplicate.
/// </summary>
public static class KeyBindingCatalog
{
    public const string ToggleId = "toggle";

    private static readonly KeyBindingDefinition[] Definitions =
    [
        new(ToggleId, "system", "CapsLock"),

        new("up", "move", "I"),
        new("left", "move", "J"),
        new("down", "move", "K"),
        new("right", "move", "L"),

        new("turboLayer", "layer", "A"),
        new("precisionLayer", "layer", "S"),
        new("scrollLayer", "layer", "D"),

        new("leftClick", "click", "Space"),
        new("rightClick", "click", "F"),
        new("middleClick", "click", "E"),
        new("doubleClick", "click", "R"),
        new("clickAndExit", "click", "T"),
        new("dragLock", "click", "V"),
        new("autoClick", "click", "B"),

        new("back", "nav", "U"),
        new("forward", "nav", "O"),

        new("grid", "jump", "G"),
        new("hint", "jump", "H"),
        new("centerScreen", "jump", "X"),
        new("centerWindow", "jump", "C"),
        new("nextMonitor", "jump", "N"),
        new("markSave", "jump", "M"),
        new("markJump", "jump", "Apostrophe"),

        new("copy", "edit", "Y"),
        new("paste", "edit", "P"),
        new("undo", "edit", "Z"),
        new("redo", "edit", "W"),

        new("grid0", "grid", "U", 0),
        new("grid1", "grid", "I", 1),
        new("grid2", "grid", "O", 2),
        new("grid3", "grid", "J", 3),
        new("grid4", "grid", "K", 4),
        new("grid5", "grid", "L", 5),
        new("grid6", "grid", "M", 6),
        new("grid7", "grid", "Comma", 7),
        new("grid8", "grid", "Period", 8)
    ];

    public static IReadOnlyList<KeyBindingDefinition> All => Definitions;

    public static IEnumerable<KeyBindingDefinition> NormalPool =>
        Definitions.Where(definition => !definition.IsGrid);

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
            !TryParseBindingKey(keyName, out var key))
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

        return TryAssign(settings, id, definition.DefaultKey, out swappedId);
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

    /// <summary>Restores every editable binding, including grid cells, to defaults.</summary>
    public static void ResetAll(Settings settings)
    {
        foreach (var definition in Definitions)
        {
            Set(settings, definition, definition.DefaultKey);
        }
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

        if (settings.Grid.Keys is null || settings.Grid.Keys.Length != 9)
        {
            settings.Grid.Keys = new GridSettings().Keys;
        }

        foreach (var definition in Definitions)
        {
            NormalizeValue(settings, definition);
        }

        ResolveDuplicates(settings, NormalPool);
        ResolveDuplicates(settings, GridPool);
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

    private static string GetGrid(Settings settings, int index)
    {
        settings.Grid.Keys ??= new GridSettings().Keys;
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
                if (definition.GridIndex >= 0 && definition.GridIndex < settings.Grid.Keys.Length)
                {
                    settings.Grid.Keys[definition.GridIndex] = value;
                }

                break;
        }
    }

    private static void NormalizeValue(Settings settings, KeyBindingDefinition definition)
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

        Set(settings, definition, definition.DefaultKey);
    }

    private static void ResolveDuplicates(
        Settings settings,
        IEnumerable<KeyBindingDefinition> pool)
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

            if (TryParseBindingKey(definition.DefaultKey, out var fallback) &&
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

    private static IEnumerable<KeyBindingDefinition> Pool(KeyBindingDefinition definition) =>
        definition.IsGrid ? GridPool : NormalPool;

    private static bool IsUnbound(string? value) =>
        string.IsNullOrWhiteSpace(value);
}
