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
/// Level of detail included in <see cref="HintScanDiagnostics"/>.
/// </summary>
public enum DiagnosticsLevel
{
    /// <summary>Basic counters only (candidate count, elapsed time, source usage).</summary>
    Basic,

    /// <summary>Per-candidate diagnostics: source, detector, confidence, rejection reason.</summary>
    Extended,

    /// <summary>Full diagnostics: include captured images and OCR text (disabled in release builds).</summary>
    Full
}

/// <summary>
/// Optional local-only counters and timings. Never include target text, UIA
/// names, screenshots, or pixel buffers in diagnostics.  Per-candidate
/// diagnostics are included only when <see cref="DiagnosticsLevel"/> is set
/// to a value greater than <see cref="DiagnosticsLevel.Basic"/>.
/// </summary>
public sealed record HintScanDiagnostics(
    int CandidateCount = 0,
    int AcceptedCount = 0,
    long ElapsedMilliseconds = 0,
    bool UiaUsed = false,
    bool CvUsed = false,
    int GeometryRejectedCount = 0,
    int LowGeometryConfidenceCount = 0,
    DiagnosticsLevel DiagnosticsLevel = DiagnosticsLevel.Basic,
    IReadOnlyList<CandidateDiagnostics>? CandidateDiagnostics = null);

/// <summary>
/// Diagnostic information for a single candidate detected during CV scanning.
/// Included in <see cref="HintScanDiagnostics"/> only when
/// <see cref="DiagnosticsLevel"/> is <see cref="DiagnosticsLevel.Extended"/> or higher.
/// </summary>
public sealed record CandidateDiagnostics
{
    /// <summary>The <see cref="TargetSource"/> that produced this candidate.</summary>
    public TargetSource Source { get; init; } = TargetSource.CvContour;

    /// <summary>The detector name used during candidate generation.</summary>
    public string Detector { get; init; } = "edge-components";

    /// <summary>Per-pixel confidence of the candidate (0.0 = weak, 1.0 = strong).</summary>
    public double PerimeterDensity { get; init; }

    /// <summary>Geometry confidence after validation (0.0 = rejected, 1.0 = accepted).</summary>
    public double GeometryConfidence { get; init; }

    /// <summary>Rejection reason, if the candidate was filtered out.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Merge reason, if the candidate was merged with another.</summary>
    public string? MergeReason { get; init; }
}

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