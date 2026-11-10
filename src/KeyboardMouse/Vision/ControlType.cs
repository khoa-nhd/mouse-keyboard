namespace KeyboardMouse.Vision;

/// <summary>
/// What a detected box appears to be. The list deliberately contains the
/// non-interactive classes too: deciding "this is a decorative icon" is part of
/// keeping precision up, and a classifier that can only say "button" has to
/// guess when it should not.
/// </summary>
public enum ControlType
{
    Unknown,
    Button,
    IconButton,
    ToggleButton,
    Tab,
    Link,
    TextField,
    Dropdown,
    Checkbox,
    RadioButton,
    Switch,
    Slider,
    Scrollbar,
    MenuItem,
    ToolbarItem,
    Card,
    Image,
    Separator,
    StaticLabel,
    DecorativeIcon
}

/// <summary>Which pieces of evidence a classification rested on. Reported so a
/// wrong answer can be explained instead of guessed at.</summary>
[Flags]
public enum ControlEvidence
{
    None = 0,
    TextInside = 1 << 0,
    LabelBelow = 1 << 1,
    Border = 1 << 2,
    FlatFill = 1 << 3,
    IconCluster = 1 << 4,
    Underline = 1 << 5,
    Repetition = 1 << 6,
    RowAlignment = 1 << 7,
    ColumnAlignment = 1 << 8,
    InsideContainer = 1 << 9,
    RoundedCorner = 1 << 10,
    HighContrast = 1 << 11,
    LowInteriorDetail = 1 << 12,
    Divider = 1 << 13,
    Knob = 1 << 14,
    Chevron = 1 << 15,
    ThinBar = 1 << 16
}

/// <summary>
/// A visual candidate plus what it looks like. Detection confidence says "a
/// shape is really there"; interaction confidence says "clicking it does
/// something". They are kept apart because this project has no hover, cursor or
/// automation signal to confirm the second one — a crisp decorative icon has
/// high detection confidence and must still score low here.
/// </summary>
public sealed record ControlCandidate(
    Rectangle Bounds,
    ControlType Type,
    VisualShape Shape,
    VisualDetector Detector,
    double DetectionConfidence,
    double InteractionConfidence,
    ControlEvidence Evidence)
{
    /// <summary>Bounds, safe region and click point. Null before the geometry pass.</summary>
    public TargetGeometry? Geometry { get; init; }

    public int Area => Bounds.Width * Bounds.Height;

    public Point Center => new(
        Bounds.Left + (Bounds.Width / 2),
        Bounds.Top + (Bounds.Height / 2));

    public bool IsInteractive => InteractionConfidence >= ControlThresholds.InteractiveThreshold(Type);

    /// <summary>
    /// Interaction confidence discounted by geometry confidence: an uncertain box
    /// ranks below a clearly bounded one with the same evidence.
    /// </summary>
    public double Rank => Geometry is { } geometry
        ? InteractionConfidence * (0.55 + (0.45 * geometry.Confidence))
        : InteractionConfidence;

    /// <summary>Interactive and usable as a click target.</summary>
    public bool IsOfferable => IsInteractive && (Geometry?.IsClickable ?? true);
}

/// <summary>
/// Type-specific interaction thresholds. An icon-only control needs more
/// evidence than a bordered text button, because "there is a small drawing here"
/// is also true of every decorative glyph on the screen.
/// </summary>
public static class ControlThresholds
{
    /// <summary>Minimum interaction confidence before the hint layer offers a target.</summary>
    public static double InteractiveThreshold(ControlType type) => type switch
    {
        ControlType.Button => 0.62,
        ControlType.IconButton => 0.66,
        ControlType.ToolbarItem => 0.66,
        ControlType.ToggleButton => 0.68,
        ControlType.Tab => 0.66,
        ControlType.Link => 0.66,
        ControlType.TextField => 0.64,
        ControlType.Dropdown => 0.68,
        ControlType.Checkbox => 0.62,
        ControlType.RadioButton => 0.62,
        ControlType.MenuItem => 0.68,
        ControlType.Switch => 0.70,
        ControlType.Slider => 0.70,
        ControlType.Scrollbar => 0.72,
        ControlType.Card => 0.75,
        ControlType.Image => 0.80,
        ControlType.Separator => 0.95,
        ControlType.StaticLabel => 0.95,
        ControlType.DecorativeIcon => 0.95,
        _ => 0.90
    };
}
