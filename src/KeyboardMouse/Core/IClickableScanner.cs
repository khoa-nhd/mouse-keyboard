namespace KeyboardMouse.Core;

public sealed record HintScanOptions(int MaxElements, bool IncludeTaskbar)
{
    /// <summary>
    /// Sources a hybrid scanner should query. The legacy UIA scanner ignores
    /// this and always scans with UI Automation.
    /// </summary>
    public TargetSource Sources { get; init; } = TargetSource.Uia;

    /// <summary>
    /// Overall scan budget for source-neutral scanners. Legacy scanners keep
    /// their own hard cap.
    /// </summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Requested source strategy. <see cref="HintScanMode.Auto"/> keeps the
    /// legacy behaviour: UIA first, CV only when UIA finds nothing.
    /// </summary>
    public HintScanMode Mode { get; init; } = HintScanMode.Auto;

    /// <summary>
    /// Screen area to scan. Only CV can leave the foreground window; UIA keeps
    /// its single-window scan and is fused in when the mode asks for it.
    /// </summary>
    public HintScanScope Scope { get; init; } = HintScanScope.ForegroundWindow;

    /// <summary>
    /// Screen regions that belong to Mouse Keyboard's own overlays. CV drops
    /// candidates that fall inside them so hint labels never detect themselves.
    /// </summary>
    public IReadOnlyList<TargetBounds> ExcludedRegions { get; init; } = [];
}

/// <summary>
/// Starts a hint scan without blocking the caller. Exactly one of the two
/// callbacks is invoked, from a background thread.
/// </summary>
public interface IClickableScanner
{
    void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut);
}

internal sealed class NullClickableScanner : IClickableScanner
{
    public static readonly NullClickableScanner Instance = new();

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut) => completed([]);
}
