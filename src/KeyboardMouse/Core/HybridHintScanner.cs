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
    private readonly Lazy<UIAutomationScanner> _uiScanner;
    private readonly Lazy<ComputerVisionScanner> _cvScanner;
    private readonly TargetFusionEngine _fusionEngine = new();

    public HybridHintScanner()
    {
        _uiScanner = new Lazy<UIAutomationScanner>(() => new UIAutomationScanner());
        _cvScanner = new Lazy<ComputerVisionScanner>(() => new ComputerVisionScanner(new ScreenCaptureService()));
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
