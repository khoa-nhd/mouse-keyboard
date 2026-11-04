using System.Drawing;
using KeyboardMouse.Capture;
using KeyboardMouse.Core;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Hints;

/// <summary>
/// Bridges the source-neutral hint scanners into the existing hint controller,
/// which consumes <see cref="ClickableTarget"/> values.
///
/// Modes are read from <see cref="HintScanOptions.Mode"/> on every scan, so a
/// settings reload takes effect immediately:
/// Auto - UIA first, CV when UIA finds nothing inside the client area.
/// Uia  - UI Automation only (legacy behaviour).
/// Cv   - screen-capture pixel analysis only.
/// Hybrid - UIA and CV fused by <see cref="TargetFusionEngine"/>.
/// </summary>
public sealed class HybridClickableScanner : IClickableScanner
{
    private readonly IMonitorService? _monitors;
    private readonly IHintScanner _uiaScanner;
    private readonly IHintScanner _cvScanner;
    private readonly TargetFusionEngine _fusionEngine = new();

    public HybridClickableScanner(IMonitorService? monitors = null)
        : this(monitors, new UIAutomationScanner(monitors))
    {
    }

    /// <summary>
    /// Test seam for real-window runs that need a longer UIA timeout than the
    /// production 400 ms. Application code uses the public constructor.
    /// </summary>
    internal HybridClickableScanner(IMonitorService? monitors, IHintScanner uiaScanner)
    {
        _monitors = monitors;
        _uiaScanner = uiaScanner ?? throw new ArgumentNullException(nameof(uiaScanner));
        _cvScanner = new ComputerVisionScanner(
            new ScreenCaptureService(),
            monitors: monitors,
            diagnosticsLevel: DiagnosticsLevel.Basic);
    }

    public void BeginScan(
        IntPtr window,
        HintScanOptions options,
        Action<IReadOnlyList<ClickableTarget>> completed,
        Action timedOut)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(timedOut);

        if (window == IntPtr.Zero)
        {
            completed([]);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                HintScanResult result = await HintScanCoordinator.RunAsync(
                    _uiaScanner,
                    _cvScanner,
                    _fusionEngine,
                    window,
                    options,
                    CancellationToken.None).ConfigureAwait(false);

                if (result.Status == HintScanStatus.TimedOut)
                {
                    Log.Info("Hint scan timed out before any source produced a result.");
                    timedOut();
                    return;
                }

                if (result.Status == HintScanStatus.Canceled)
                {
                    completed([]);
                    return;
                }

                completed(Convert(result.Targets, options));
            }
            catch (Exception ex)
            {
                Log.Error("Hint scan failed", ex);
                completed([]);
            }
        });
    }

    private IReadOnlyList<ClickableTarget> Convert(
        IReadOnlyList<VisualTarget> targets,
        HintScanOptions options)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        // Apply the same final rules to single-source and fused results. A
        // larger target that contains a smaller one usually covers it, but a
        // large container must not hide a clearly interactive child control.
        IReadOnlyList<VisualTarget> kept = TargetPipeline.Finalize(targets);

        var raw = kept
            .OrderBy(target => target.Bounds.Top)
            .ThenBy(target => target.Bounds.Left)
            .Select(target => new RawClickableTarget(
                target.Bounds.ToRectangle(),
                IsOffscreen: false,
                IsEnabled: target.IsEnabled,
                target.Text,
                ClickPoint: target.ClickPoint,
                Geometry: target.Geometry,
                Type: target.Type))
            .ToList();

        return HintTargetFilter.Apply(raw, GetMonitors(), options.MaxElements);
    }

    private IReadOnlyList<MonitorInfo> GetMonitors()
    {
        if (_monitors is not null)
        {
            return _monitors.All;
        }

        Rectangle virtualScreen = SystemInformation.VirtualScreen;

        return virtualScreen.Width > 0 && virtualScreen.Height > 0
            ? [new MonitorInfo(IntPtr.Zero, virtualScreen, virtualScreen, 1.0)]
            : [];
    }
}
