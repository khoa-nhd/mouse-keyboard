using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Config;

/// <summary>
/// Settings schema. Loaded from settings.json and saved back with camelCase
/// names (see SettingsStore). Phase 1 only consumes activation and speed;
/// the rest is here so the file is stable from the start.
/// </summary>
public sealed class Settings
{
    public ActivationSettings Activation { get; set; } = new();

    public KeysSettings Keys { get; set; } = new();

    public GridSettings Grid { get; set; } = new();

    public HintSettings Hint { get; set; } = new();

    public SpeedSettings Speed { get; set; } = new();

    public ScrollSettings Scroll { get; set; } = new();

    public FeedbackSettings Feedback { get; set; } = new();

    public AutoClickSettings AutoClick { get; set; } = new();

    public UpdateSettings Update { get; set; } = new();


    /// <summary>
    /// Repairs missing or out-of-range values so a hand-edited settings file
    /// can never crash the app. Full validation/duplicate-key checks come in
    /// Phase 8; this is the safety net.
    /// </summary>
    public void Normalize()
    {
        Activation ??= new ActivationSettings();
        Keys ??= new KeysSettings();
        Grid ??= new GridSettings();
        Hint ??= new HintSettings();
        Speed ??= new SpeedSettings();
        Scroll ??= new ScrollSettings();
        Feedback ??= new FeedbackSettings();
        AutoClick ??= new AutoClickSettings();
        Update ??= new UpdateSettings();

        AutoClick.IntervalMs = Math.Clamp(AutoClick.IntervalMs, AutoClicker.MinIntervalMs, AutoClicker.MaxIntervalMs);
        AutoClick.Button = AutoClicker.ParseButton(AutoClick.Button).ToString();

        Activation.TapTimeoutMs = Math.Clamp(Activation.TapTimeoutMs, 50, 1000);
        if (Activation.AutoOffSeconds < 0)
        {
            Activation.AutoOffSeconds = 0;
        }

        if (string.IsNullOrWhiteSpace(Activation.ToggleKey) ||
            !KeyNames.TryParse(Activation.ToggleKey, out var toggleKey) ||
            toggleKey == KeyboardMouse.Input.PhysicalKey.None ||
            toggleKey.IsModifier())
        {
            Activation.ToggleKey = "CapsLock";
        }

        Speed.Normal = NormalizeSpeed(Speed.Normal, SpeedConfig.Normal);
        Speed.Precision = NormalizeSpeed(Speed.Precision, SpeedConfig.Precision);
        Speed.Turbo = NormalizeSpeed(Speed.Turbo, SpeedConfig.Turbo);

        if (Grid.MaxDepth is < 1 or > 5)
        {
            Grid.MaxDepth = 3;
        }

        if (Grid.Keys is null || Grid.Keys.Length != 9)
        {
            Grid.Keys = new GridSettings().Keys;
        }

        if (string.IsNullOrWhiteSpace(Hint.Alphabet) || Hint.Alphabet.Distinct().Count() < 2)
        {
            Hint.Alphabet = "asdfjkl;";
        }

        Hint.MaxElements = Math.Clamp(Hint.MaxElements, 1, 2000);

        Scroll.Base = Math.Max(0, Scroll.Base);
        Scroll.Max = Math.Max(Scroll.Base, Scroll.Max);
        Scroll.Accel = Math.Max(0, Scroll.Accel);

        Feedback.DotSize = Math.Clamp(Feedback.DotSize, 2, 64);

        if (!CursorStyleNames.TryParse(Feedback.CursorStyle, out var cursorStyle))
        {
            cursorStyle = CursorStyle.Dot;
        }

        Feedback.CursorStyle = CursorStyleNames.ToId(cursorStyle);

        Feedback.CursorColor = CursorColors.Normalize(Feedback.CursorColor, CursorColors.DefaultHex);
        Feedback.ScrollColor = CursorColors.Normalize(Feedback.ScrollColor, CursorColors.DefaultScrollHex);
        Feedback.PrecisionColor = CursorColors.Normalize(Feedback.PrecisionColor, CursorColors.DefaultPrecisionHex);
        Feedback.TurboColor = CursorColors.Normalize(Feedback.TurboColor, CursorColors.DefaultTurboHex);
        Feedback.DragLockColor = CursorColors.Normalize(Feedback.DragLockColor, CursorColors.DefaultDragLockHex);
        Feedback.AutoClickColor = CursorColors.Normalize(Feedback.AutoClickColor, CursorColors.DefaultAutoClickHex);
        Feedback.BorderColor = CursorColors.Normalize(Feedback.BorderColor, CursorColors.DefaultHex);
        Feedback.BorderThickness = Math.Clamp(Feedback.BorderThickness, 2, 12);
        Feedback.BorderStyle = BorderStyles.ToId(BorderStyles.ParseOrDefault(Feedback.BorderStyle));

        if (!DateTimeOffset.TryParse(Update.LastCheckedUtc, out _))
        {
            Update.LastCheckedUtc = string.Empty;
        }

        Update.SkippedVersion = Update.SkippedVersion?.Trim() ?? string.Empty;
    }

    private static SpeedConfig NormalizeSpeed(SpeedConfig? config, SpeedConfig fallback)
    {
        config ??= fallback;

        config.Base = Math.Max(0, config.Base);
        config.Accel = Math.Max(0, config.Accel);
        config.TapStep = Math.Max(0, config.TapStep);
        if (config.Max < config.Base)
        {
            config.Max = config.Base;
        }

        return config;
    }
}

public sealed class ActivationSettings
{
    public string ToggleKey { get; set; } = "CapsLock";

    public int TapTimeoutMs { get; set; } = 200;

    public int AutoOffSeconds { get; set; }
}

public sealed class KeysSettings
{
    public string Up { get; set; } = "I";
    public string Left { get; set; } = "J";
    public string Down { get; set; } = "K";
    public string Right { get; set; } = "L";

    public string LeftClick { get; set; } = "Space";
    public string RightClick { get; set; } = "F";
    public string MiddleClick { get; set; } = "E";
    public string DoubleClick { get; set; } = "R";
    public string ClickAndExit { get; set; } = "T";
    public string DragLock { get; set; } = "V";

    public string Back { get; set; } = "U";
    public string Forward { get; set; } = "O";

    public string ScrollLayer { get; set; } = "D";
    public string PrecisionLayer { get; set; } = "S";
    public string TurboLayer { get; set; } = "A";

    public string Grid { get; set; } = "G";
    public string Hint { get; set; } = "H";

    public string CenterScreen { get; set; } = "X";
    public string CenterWindow { get; set; } = "C";
    public string NextMonitor { get; set; } = "N";

    public string MarkSave { get; set; } = "M";
    public string MarkJump { get; set; } = "Apostrophe";

    public string Copy { get; set; } = "Y";
    public string Paste { get; set; } = "P";
    public string Undo { get; set; } = "Z";
    public string Redo { get; set; } = "W";

    public string AutoClick { get; set; } = "B";
}

public sealed class AutoClickSettings
{
    /// <summary>Time between clicks (10–60000 ms). 100 ms = 10 clicks per second.</summary>
    public int IntervalMs { get; set; } = 100;

    /// <summary>"Left", "Right" or "Middle".</summary>
    public string Button { get; set; } = "Left";
}

public sealed class GridSettings
{
    public string[] Keys { get; set; } = ["U", "I", "O", "J", "K", "L", "M", "Comma", "Period"];

    public int MaxDepth { get; set; } = 3;
}

public sealed class HintSettings
{
    public string Alphabet { get; set; } = "asdfjkl;";

    public int MaxElements { get; set; } = 400;

    public bool IncludeTaskbar { get; set; } = true;
}

public sealed class SpeedSettings
{
    public SpeedConfig Normal { get; set; } = SpeedConfig.Normal;

    public SpeedConfig Precision { get; set; } = SpeedConfig.Precision;

    public SpeedConfig Turbo { get; set; } = SpeedConfig.Turbo;
}

public sealed class SpeedConfig
{
    public double Base { get; set; }

    public double Max { get; set; }

    public double Accel { get; set; }

    public double TapStep { get; set; }

    public static SpeedConfig Normal => new() { Base = 300, Max = 2000, Accel = 3000, TapStep = 5 };

    public static SpeedConfig Precision => new() { Base = 60, Max = 60, Accel = 0, TapStep = 1 };

    public static SpeedConfig Turbo => new() { Base = 1500, Max = 5000, Accel = 8000, TapStep = 40 };

    public SpeedProfile ToProfile() => new(Base, Max, Accel, TapStep);
}

public sealed class ScrollSettings
{
    public double Base { get; set; } = 400;

    public double Max { get; set; } = 3000;

    public double Accel { get; set; } = 4000;

    public bool Smooth { get; set; } = true;

    public SpeedProfile ToProfile() => new(Base, Max, Accel, tapStep: 0);
}

public sealed class FeedbackSettings
{
    public bool CursorDot { get; set; } = true;

    public bool ScreenBorder { get; set; } = true;

    public int DotSize { get; set; } = 12;

    /// <summary>One of the <see cref="CursorStyleNames"/> ids, e.g. "halo" or "buddy".</summary>
    public string CursorStyle { get; set; } = "dot";

    /// <summary>Colour of the indicator in the normal state, as six hex digits.</summary>
    public string CursorColor { get; set; } = CursorColors.DefaultHex;

    public string ScrollColor { get; set; } = CursorColors.DefaultScrollHex;

    public string PrecisionColor { get; set; } = CursorColors.DefaultPrecisionHex;

    public string TurboColor { get; set; } = CursorColors.DefaultTurboHex;

    public string DragLockColor { get; set; } = CursorColors.DefaultDragLockHex;

    public string AutoClickColor { get; set; } = CursorColors.DefaultAutoClickHex;

    /// <summary>One of the <see cref="BorderStyles"/> ids: "frame", "glow", "corners" or "none".</summary>
    public string BorderStyle { get; set; } = "frame";

    public string BorderColor { get; set; } = CursorColors.DefaultHex;

    /// <summary>Line thickness of the frame, in pixels.</summary>
    public int BorderThickness { get; set; } = 3;

    /// <summary>The six colours the pointer indicator can use.</summary>
    public CursorPalette ToPalette() => new(
        CursorColors.ToColor(CursorColor),
        CursorColors.ToColor(ScrollColor),
        CursorColors.ToColor(PrecisionColor),
        CursorColors.ToColor(TurboColor),
        CursorColors.ToColor(DragLockColor),
        CursorColors.ToColor(AutoClickColor));
}

/// <summary>
/// Auto-update is off by default: the app promises not to use the network
/// unless the user turns this on. Only the public GitHub release JSON is
/// fetched, never anything about the user.
/// </summary>
public sealed class UpdateSettings
{
    public bool CheckOnStart { get; set; }

    public string LastCheckedUtc { get; set; } = string.Empty;

    public string SkippedVersion { get; set; } = string.Empty;
}
