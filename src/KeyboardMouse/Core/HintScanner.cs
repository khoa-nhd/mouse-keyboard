namespace KeyboardMouse.Core;

/// <summary>Terminal state of one hint scan.</summary>
public enum HintScanStatus
{
    /// <summary>At least one target was returned.</summary>
    Success,

    /// <summary>The scan completed normally but found nothing.</summary>
    NoTargets,

    /// <summary>The scan deadline expired before a usable result was ready.</summary>
    TimedOut,

    /// <summary>The scan was canceled, usually because hint mode closed or restarted.</summary>
    Canceled,

    /// <summary>The scanner failed before it could produce a result.</summary>
    Failed
}

/// <summary>
/// Optional local-only counters and timings. Never include target text, UIA
/// names, screenshots, or pixel buffers in diagnostics.
/// </summary>
public sealed record HintScanDiagnostics(
    int CandidateCount = 0,
    int AcceptedCount = 0,
    long ElapsedMilliseconds = 0,
    bool UiaUsed = false,
    bool CvUsed = false);

/// <summary>
/// One scan request. The window handle is the foreground window at the time
/// hint mode opened; scanners must not inspect overlays or UI controls.
/// </summary>
public sealed record HintScanRequest(
    IntPtr Window,
    HintScanOptions Options,
    CancellationToken CancellationToken = default);

/// <summary>
/// The single result of a scan. A partial result is a success: if one source
/// fails, the other source's targets are returned normally.
/// </summary>
public sealed record HintScanResult(
    HintScanStatus Status,
    IReadOnlyList<VisualTarget> Targets,
    HintScanDiagnostics? Diagnostics = null,
    string? FailureReason = null)
{
    public bool IsSuccess => Status is HintScanStatus.Success or HintScanStatus.NoTargets;

    public bool HasTargets => Targets.Count > 0;

    public static HintScanResult FromTargets(
        IReadOnlyList<VisualTarget> targets,
        HintScanDiagnostics? diagnostics = null) =>
        new(
            targets.Count == 0 ? HintScanStatus.NoTargets : HintScanStatus.Success,
            targets,
            diagnostics);

    public static HintScanResult Failure(
        HintScanStatus status,
        string? reason = null) =>
        new(status, [], null, reason);
}

/// <summary>
/// Controller-facing scan port. <see cref="BeginScan"/> returns immediately and
/// invokes the completion callback exactly once from a background thread. The
/// callback must be dispatched to the UI thread by the caller before touching
/// overlays or controller state. Implementations do not access UI controls.
/// </summary>
public interface IHintScanner
{
    void BeginScan(HintScanRequest request, Action<HintScanResult> completed);
}
