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
/// One delivery from a scan. A scan may report several times: an early result
/// with <see cref="Complete"/> false as soon as the fast windows have answered,
/// then the full result with <see cref="Complete"/> true. Each result holds the
/// targets found so far, so the caller can add the new ones without moving the
/// labels it already shows. <see cref="Complete"/> true means no more results
/// will follow for this scan.
/// </summary>
public sealed record HintScanResult(IReadOnlyList<ClickableTarget> Targets, bool Complete);

/// <summary>
/// Starts a hint scan without blocking the caller. Results and the timeout are
/// reported from a background thread.
/// </summary>
public interface IClickableScanner
{
    void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<HintScanResult> reported,
        Action timedOut);
}

internal sealed class NullClickableScanner : IClickableScanner
{
    public static readonly NullClickableScanner Instance = new();

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<HintScanResult> reported,
        Action timedOut) => reported(new HintScanResult([], true));
}
