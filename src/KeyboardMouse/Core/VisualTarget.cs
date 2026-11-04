using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Where a hint target came from. A target may carry more than one flag after
/// fusion; a single-source target keeps just its source flag.
/// </summary>
[Flags]
public enum TargetSource
{
    None = 0,
    Uia = 1 << 0,
    CvOcr = 1 << 1,
    CvContour = 1 << 2,
    Cv = CvOcr | CvContour,
    All = Uia | Cv
}

/// <summary>UI semantic kind of a target, when one can be determined.</summary>
public enum TargetType
{
    Unknown,
    Button,
    Hyperlink,
    MenuItem,
    TabItem,
    ListItem,
    TreeItem,
    CheckBox,
    RadioButton,
    ComboBox,
    Edit,
    Text,
    Icon,
    Other
}

/// <summary>The action a hint label performs against a target.</summary>
public enum TargetActionType
{
    LeftClick,
    RightClick,
    DoubleClick,
    Hover
}

/// <summary>
/// A rectangle in the canonical hint coordinate space: physical pixels in the
/// Windows virtual-desktop coordinate system. The origin is the virtual-screen
/// origin (SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN) and may be negative; X grows
/// right and Y grows down. Right and bottom are exclusive.
///
/// Bounds are never DIPs, monitor-local pixels, or normalized 0-1 values. DPI
/// scale is metadata for sizing thresholds only, not a coordinate conversion.
/// Scanners that work in frame-local pixels must convert through
/// <see cref="HintCoordinates"/> before constructing a target.
/// </summary>
public readonly record struct TargetBounds(int X, int Y, int Width, int Height)
{
    public static TargetBounds Empty => default;

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool IsValid => Width > 0 && Height > 0;

    public Point Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(Point point) =>
        IsValid &&
        point.X >= Left && point.X < Right &&
        point.Y >= Top && point.Y < Bottom;

    public bool Contains(TargetBounds other) =>
        IsValid &&
        other.IsValid &&
        other.Left >= Left && other.Top >= Top &&
        other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(TargetBounds other) =>
        IsValid &&
        other.IsValid &&
        other.Left < Right && other.Right > Left &&
        other.Top < Bottom && other.Bottom > Top;

    public TargetBounds Intersect(TargetBounds other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);

        return right > left && bottom > top
            ? new TargetBounds(left, top, right - left, bottom - top)
            : Empty;
    }

    public TargetBounds ClipTo(Rectangle clip) => Intersect(FromRectangle(clip));

    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static TargetBounds FromRectangle(Rectangle bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}

/// <summary>
/// Optional, presentation-free metadata about how a target was observed.
/// Text and ids live here or on <see cref="VisualTarget"/>, never in logs.
/// </summary>
public sealed record TargetMetadata
{
    public static TargetMetadata Empty { get; } = new();

    public UiaTargetMetadata? Uia { get; init; }

    public CvTargetMetadata? Cv { get; init; }

    public bool IsEmpty => Uia is null && Cv is null;
}

/// <summary>
/// UIA interaction patterns that a hint target supports. Kept as neutral flags
/// so shared models do not depend on FlaUI's pattern interfaces.
/// </summary>
[Flags]
public enum UiaPatternFlags
{
    None = 0,
    Invoke = 1 << 0,
    SelectionItem = 1 << 1,
    Toggle = 1 << 2,
    ExpandCollapse = 1 << 3,
    Value = 1 << 4,
    ScrollItem = 1 << 5,
    Selection = 1 << 6,
    GridItem = 1 << 7,
    RangeValue = 1 << 8,
    All = Invoke | SelectionItem | Toggle | ExpandCollapse | Value |
          ScrollItem | Selection | GridItem | RangeValue
}

/// <summary>
/// Plain UIA data. Deliberately contains no COM element, so shared models stay
/// independent of FlaUI and UI Automation lifetimes. SourceBounds is the raw
/// UIA rectangle before monitor clipping.
/// </summary>
public sealed record UiaTargetMetadata(
    string? AutomationId = null,
    string? RuntimeId = null,
    string? ControlType = null,
    string? ClassName = null,
    bool SupportsInvoke = false,
    Rectangle? SourceBounds = null,
    bool IsKeyboardFocusable = false,
    bool IsOffscreen = false,
    UiaPatternFlags SupportedPatterns = UiaPatternFlags.None);

/// <summary>
/// Plain computer-vision data. SourceBounds is frame-local; Bounds on the
/// target is the canonical converted rectangle.
/// </summary>
public sealed record CvTargetMetadata(
    string? OcrText = null,
    double? OcrConfidence = null,
    int FrameId = 0,
    string? Detector = null,
    Rectangle? SourceBounds = null);

/// <summary>
/// One target that can be displayed in hint mode, regardless of whether it was
/// found by UI Automation, OCR, or a classical CV detector.
///
/// All geometry is in the canonical physical virtual-desktop space. The click
/// point must be inside the bounds. Text is optional and in-memory only; it is
/// never serialized or logged.
/// </summary>
public sealed record VisualTarget
{
    public VisualTarget(
        string id,
        TargetSource source,
        TargetBounds bounds,
        Point clickPoint,
        TargetType type = TargetType.Unknown,
        string? text = null,
        double detectionConfidence = 1.0,
        double interactionConfidence = 1.0,
        bool isEnabled = true,
        bool isVisible = true,
        TargetMetadata? metadata = null,
        TargetGeometry? geometry = null,
        double classificationConfidence = 1.0)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A target id is required.", nameof(id));
        }

        if (source == TargetSource.None || (source & ~TargetSource.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "A target must have at least one known source.");
        }

        if (!bounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds,
                "Target bounds must have a positive width and height.");
        }

        if (!bounds.Contains(clickPoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clickPoint),
                clickPoint,
                "The click point must be inside the target bounds.");
        }

        if (!IsConfidenceInRange(detectionConfidence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionConfidence),
                detectionConfidence,
                "Detection confidence must be between 0 and 1.");
        }

        if (!IsConfidenceInRange(interactionConfidence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interactionConfidence),
                interactionConfidence,
                "Interaction confidence must be between 0 and 1.");
        }

        if (!IsConfidenceInRange(geometry?.Confidence ?? 1.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometry),
                geometry?.Confidence,
                "Geometry confidence must be between 0 and 1.");
        }

        if (!IsConfidenceInRange(classificationConfidence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(classificationConfidence),
                classificationConfidence,
                "Classification confidence must be between 0 and 1.");
        }

        Id = id;
        Source = source;
        Bounds = bounds;
        ClickPoint = clickPoint;
        Type = type;
        Text = text ?? string.Empty;
        DetectionConfidence = detectionConfidence;
        InteractionConfidence = interactionConfidence;
        IsEnabled = isEnabled;
        IsVisible = isVisible;
        Metadata = metadata ?? TargetMetadata.Empty;
        Geometry = geometry ?? TargetGeometry.Default;
        ClassificationConfidence = classificationConfidence;
    }

    /// <summary>Scan-local stable id. Never persisted.</summary>
    public string Id { get; init; }

    public TargetSource Source { get; init; }

    public TargetType Type { get; init; }

    /// <summary>Optional visible text or accessible name. In-memory only.</summary>
    public string Text { get; init; }

    /// <summary>Visible bounds in canonical physical virtual-desktop pixels.</summary>
    public TargetBounds Bounds { get; init; }

    /// <summary>Point the pointer moves to. Always inside <see cref="Bounds"/>.</summary>
    public Point ClickPoint { get; init; }

    /// <summary>How sure the scanner is that this is a real target, in [0, 1].</summary>
    public double DetectionConfidence { get; init; }

    /// <summary>How sure the scanner is that this target is interactive, in [0, 1].</summary>
    public double InteractionConfidence { get; init; }

    /// <summary>
    /// Geometry quality and click-point diagnostics. UIA targets default to a
    /// clear geometry; CV targets carry the pixel-refinement result.
    /// </summary>
    public TargetGeometry Geometry { get; init; }

    /// <summary>
    /// How sure the source is about the semantic <see cref="Type"/> in [0, 1].
    /// A high OCR confidence is not a classification confidence: text regions
    /// can be read perfectly and still not be clickable.
    /// </summary>
    public double ClassificationConfidence { get; init; }

    /// <summary>
    /// Detection confidence tempered by geometry confidence. Kept as a simple
    /// source-level signal; full ordering uses <c>TargetRanker</c>, which also
    /// considers interaction, classification, source reliability, ambiguity and
    /// duplicate likelihood.
    /// </summary>
    public double RankingConfidence =>
        DetectionConfidence * (0.25 + (0.75 * Geometry.Confidence));

    public bool IsEnabled { get; init; }

    public bool IsVisible { get; init; }

    public TargetMetadata Metadata { get; init; }

    public bool IsActionable =>
        IsEnabled &&
        IsVisible &&
        Bounds.IsValid &&
        Bounds.Contains(ClickPoint);

    public static bool IsConfidenceInRange(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;

    public override string ToString() =>
        $"VisualTarget({Id}, {Bounds}, {Source}, {DetectionConfidence:0.00})";
}
