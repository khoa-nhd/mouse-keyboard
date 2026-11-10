using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// What one hint scan should cover. <see cref="Anchor"/> is the pointer at the
/// moment H was pressed, used by scopes phrased as "the monitor I am on".
/// </summary>
public sealed record HintScanOptions(
    int MaxElements,
    bool IncludeTaskbar,
    HintScanScope Scope = HintScanScope.ForegroundWindow,
    Point Anchor = default,
    bool VisualDetection = false);

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
