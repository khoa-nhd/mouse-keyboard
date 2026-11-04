using System;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Capture;

/// <summary>
/// Source-neutral facade over UI Automation and computer vision. The actual
/// orchestration lives in <see cref="HintScanCoordinator"/> so the scanner API
/// and the application's clickable-target adapter stay in sync.
/// </summary>
public sealed class HybridHintScanner : IHintScanner
{
    private readonly Lazy<IHintScanner> _uiScanner;
    private readonly Lazy<IHintScanner> _cvScanner;
    private readonly TargetFusionEngine _fusionEngine = new();

    public HybridHintScanner()
        : this(
            () => new UIAutomationScanner(),
            () => new ComputerVisionScanner(new ScreenCaptureService()))
    {
    }

    /// <summary>
    /// Test seam for real-window runs that need a longer UIA timeout than the
    /// production 400 ms. Application code uses the parameterless constructor.
    /// </summary>
    internal HybridHintScanner(
        Func<IHintScanner> uiScanner,
        Func<IHintScanner> cvScanner)
    {
        _uiScanner = new Lazy<IHintScanner>(
            uiScanner ?? throw new ArgumentNullException(nameof(uiScanner)));
        _cvScanner = new Lazy<IHintScanner>(
            cvScanner ?? throw new ArgumentNullException(nameof(cvScanner)));
    }

    public void BeginScan(HintScanRequest request, Action<HintScanResult> completed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completed);

        _ = Task.Run(async () =>
        {
            HintScanResult result;

            try
            {
                result = await HintScanCoordinator.RunAsync(
                    _uiScanner.Value,
                    _cvScanner.Value,
                    _fusionEngine,
                    request.Window,
                    request.Options,
                    request.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                result = HintScanResult.Failure(
                    HintScanStatus.Failed,
                    "The hybrid hint scan failed.");
            }

            completed(result);
        });
    }
}
