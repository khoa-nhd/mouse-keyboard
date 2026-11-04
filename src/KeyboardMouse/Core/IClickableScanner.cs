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
