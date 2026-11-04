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
    {
        _monitors = monitors;
        _uiaScanner = new UIAutomationScanner(monitors);
        _cvScanner = new ComputerVisionScanner(new ScreenCaptureService());
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

        // A larger target that contains a smaller one already covers it. This
        // removes CV text boxes inside UIA buttons and duplicate contours.
        var kept = new List<VisualTarget>(targets.Count);
        foreach (VisualTarget target in targets
                     .Where(target => target.IsActionable)
                     .OrderByDescending(target => (long)target.Bounds.Width * target.Bounds.Height))
        {
            bool covered = false;
            foreach (VisualTarget existing in kept)
            {
                if (existing.Bounds.Contains(target.Bounds) ||
                    (existing.Bounds.IntersectsWith(target.Bounds) &&
                     existing.Bounds.Contains(target.Bounds.Center)))
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
            {
                kept.Add(target);
            }
        }

        var raw = kept
            .OrderBy(target => target.Bounds.Top)
            .ThenBy(target => target.Bounds.Left)
            .Select(target => new RawClickableTarget(
                target.Bounds.ToRectangle(),
                IsOffscreen: false,
                IsEnabled: target.IsEnabled,
                target.Text))
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
