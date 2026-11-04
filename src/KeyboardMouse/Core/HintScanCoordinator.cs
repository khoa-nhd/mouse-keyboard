using System.Drawing;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Core;

/// <summary>
/// Shared orchestration for source-neutral hint scanning. Used by the
/// UIA/CV/hybrid <see cref="IHintScanner"/> facade and by the application's
/// <see cref="IClickableScanner"/> adapter, so both entry points behave the
/// same way.
///
/// Every mode starts the fastest useful source first:
/// Auto runs UIA and CV concurrently and only waits for CV when UIA found no
/// target inside the window's client area (window chrome does not count),
/// Uia/Cv run a single source, and Hybrid fuses both. The whole scan has one
/// hard deadline and exactly one terminal result.
/// </summary>
internal static class HintScanCoordinator
{
    public static async Task<HintScanResult> RunAsync(
        IHintScanner uia,
        IHintScanner cv,
        TargetFusionEngine fusion,
        IntPtr window,
        HintScanOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uia);
        ArgumentNullException.ThrowIfNull(cv);
        ArgumentNullException.ThrowIfNull(fusion);
        ArgumentNullException.ThrowIfNull(options);

        if (cancellationToken.IsCancellationRequested)
        {
            return HintScanResult.Failure(
                HintScanStatus.Canceled,
                "The hint scan was canceled.");
        }

        if (window == IntPtr.Zero)
        {
            return HintScanResult.FromTargets([]);
        }

        HintScanMode mode = ResolveMode(options);

        TimeSpan budget = options.Budget > TimeSpan.Zero &&
                          options.Budget <= TimeSpan.FromSeconds(10)
            ? options.Budget
            : HintScanModes.DefaultBudget;

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(budget);

        try
        {
            return mode switch
            {
                HintScanMode.Uia => await ScanSingleAsync(
                    uia, window, options, HintScanMode.Uia, TargetSource.Uia, overall.Token)
                    .ConfigureAwait(false),
                HintScanMode.Cv => await ScanSingleAsync(
                    cv, window, options, HintScanMode.Cv, TargetSource.Cv, overall.Token)
                    .ConfigureAwait(false),
                HintScanMode.Hybrid => await ScanHybridAsync(
                    uia, cv, fusion, window, options, overall.Token)
                    .ConfigureAwait(false),
                _ => await ScanAutoAsync(uia, cv, fusion, window, options, overall.Token)
                    .ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && overall.IsCancellationRequested)
        {
            return HintScanResult.Failure(
                HintScanStatus.TimedOut,
                "The hint scan timed out.");
        }
    }

    private static HintScanMode ResolveMode(HintScanOptions options)
    {
        HintScanMode mode = options.Mode;
        TargetSource sources = options.Sources & TargetSource.All;

        // Legacy callers set Sources (and leave Mode at its Auto default).
        // Honour their source selection instead of scanning both sources.
        if (mode == HintScanMode.Auto && sources != TargetSource.None)
        {
            if (sources == TargetSource.Uia)
            {
                return HintScanMode.Uia;
            }

            if ((sources & TargetSource.Uia) == 0)
            {
                return HintScanMode.Cv;
            }
        }

        return mode;
    }

    /// <summary>
    /// UIA first for speed. CV is started at the same time but is only awaited
    /// when UIA produced nothing, or when every UIA target is window chrome
    /// (caption buttons). A custom-drawn window exposes its Minimize/Close
    /// buttons to UIA, so "UIA is not empty" alone must not suppress CV.
    /// </summary>
    private static async Task<HintScanResult> ScanAutoAsync(
        IHintScanner uia,
        IHintScanner cv,
        TargetFusionEngine fusion,
        IntPtr window,
        HintScanOptions options,
        CancellationToken token)
    {
        using var uiaCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var cvCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        Task<HintScanResult> uiaTask = ScanSingleAsync(
            uia, window, options, HintScanMode.Uia, TargetSource.Uia, uiaCts.Token);
        Task<HintScanResult> cvTask = ScanSingleAsync(
            cv, window, options, HintScanMode.Cv, TargetSource.Cv, cvCts.Token);

        HintScanResult uiaResult = await uiaTask.ConfigureAwait(false);

        if (uiaResult.HasTargets && HasContentTargets(uiaResult.Targets, window))
        {
            cvCts.Cancel();
            return uiaResult;
        }

        HintScanResult cvResult = await cvTask.ConfigureAwait(false);

        if (!uiaResult.HasTargets)
        {
            return cvResult.HasTargets || cvResult.Status == HintScanStatus.Success
                ? cvResult
                : HintScanResult.FromTargets(
                    [],
                    new HintScanDiagnostics(
                        CandidateCount: 0,
                        AcceptedCount: 0,
                        ElapsedMilliseconds: 0,
                        UiaUsed: uiaResult.Diagnostics?.UiaUsed ?? false,
                        CvUsed: cvResult.Diagnostics?.CvUsed ?? false));
        }

        // UIA found only caption buttons. Keep them and add the CV content so
        // the user can still reach Close/Minimize.
        if (!cvResult.HasTargets)
        {
            return uiaResult;
        }

        IReadOnlyList<VisualTarget> targets = fusion
            .Fuse(uiaResult.Targets, cvResult.Targets)
            .Targets;

        return HintScanResult.FromTargets(
            targets,
            new HintScanDiagnostics(
                CandidateCount: uiaResult.Targets.Count + cvResult.Targets.Count,
                AcceptedCount: targets.Count,
                ElapsedMilliseconds: Math.Max(
                    uiaResult.Diagnostics?.ElapsedMilliseconds ?? 0,
                    cvResult.Diagnostics?.ElapsedMilliseconds ?? 0),
                UiaUsed: true,
                CvUsed: true));
    }

    /// <summary>
    /// True when at least one target intersects the window's client area.
    /// When the client rectangle cannot be resolved the caller should keep
    /// trusting UIA rather than pay for an unnecessary CV scan.
    /// </summary>
    private static bool HasContentTargets(
        IReadOnlyList<VisualTarget> targets,
        IntPtr window)
    {
        if (targets.Count == 0)
        {
            return false;
        }

        if (window == IntPtr.Zero ||
            !User32.GetClientRect(window, out RECT client))
        {
            return true;
        }

        var origin = new POINT(client.Left, client.Top);
        if (!User32.ClientToScreen(window, ref origin))
        {
            return true;
        }

        var clientBounds = new Rectangle(
            origin.X,
            origin.Y,
            client.Width,
            client.Height);

        if (clientBounds.Width <= 0 || clientBounds.Height <= 0)
        {
            return true;
        }

        foreach (VisualTarget target in targets)
        {
            if (clientBounds.IntersectsWith(target.Bounds.ToRectangle()))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<HintScanResult> ScanHybridAsync(
        IHintScanner uia,
        IHintScanner cv,
        TargetFusionEngine fusion,
        IntPtr window,
        HintScanOptions options,
        CancellationToken token)
    {
        using var uiaCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var cvCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        Task<HintScanResult> uiaTask = ScanSingleAsync(
            uia, window, options, HintScanMode.Uia, TargetSource.Uia, uiaCts.Token);
        Task<HintScanResult> cvTask = ScanSingleAsync(
            cv, window, options, HintScanMode.Cv, TargetSource.Cv, cvCts.Token);

        HintScanResult[] results = await Task.WhenAll(uiaTask, cvTask).ConfigureAwait(false);
        HintScanResult uiaResult = results[0];
        HintScanResult cvResult = results[1];

        IReadOnlyList<VisualTarget> targets;
        if (uiaResult.HasTargets && cvResult.HasTargets)
        {
            targets = fusion.Fuse(uiaResult.Targets, cvResult.Targets).Targets;
        }
        else if (uiaResult.HasTargets)
        {
            targets = uiaResult.Targets;
        }
        else
        {
            targets = cvResult.Targets;
        }

        return HintScanResult.FromTargets(
            targets,
            new HintScanDiagnostics(
                CandidateCount: uiaResult.Targets.Count + cvResult.Targets.Count,
                AcceptedCount: targets.Count,
                ElapsedMilliseconds: Math.Max(
                    uiaResult.Diagnostics?.ElapsedMilliseconds ?? 0,
                    cvResult.Diagnostics?.ElapsedMilliseconds ?? 0),
                UiaUsed: uiaResult.Diagnostics?.UiaUsed ?? uiaResult.HasTargets,
                CvUsed: cvResult.Diagnostics?.CvUsed ?? cvResult.HasTargets));
    }

    private static async Task<HintScanResult> ScanSingleAsync(
        IHintScanner scanner,
        IntPtr window,
        HintScanOptions baseOptions,
        HintScanMode mode,
        TargetSource sources,
        CancellationToken token)
    {
        var sourceOptions = new HintScanOptions(
            Math.Clamp(baseOptions.MaxElements, 1, 2000),
            baseOptions.IncludeTaskbar)
        {
            Mode = mode,
            Sources = sources,
            Budget = baseOptions.Budget > TimeSpan.Zero &&
                     baseOptions.Budget < HintScanModes.SourceBudget
                ? baseOptions.Budget
                : HintScanModes.SourceBudget
        };

        var completion = new TaskCompletionSource<HintScanResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            scanner.BeginScan(
                new HintScanRequest(window, sourceOptions, token),
                result => completion.TrySetResult(result));
        }
        catch (Exception ex)
        {
            Log.Error("Could not start a hint source scan", ex);
            return HintScanResult.Failure(
                HintScanStatus.Failed,
                "The hint source could not start.");
        }

        try
        {
            return await completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return HintScanResult.Failure(
                HintScanStatus.TimedOut,
                "The hint source timed out.");
        }
    }
}
