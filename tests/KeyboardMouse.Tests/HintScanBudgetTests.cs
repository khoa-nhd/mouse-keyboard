using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Tests;

public class HintScanBudgetTests
{
    [Fact]
    public void AScanThatIsLateStillHasTimeToFinish()
    {
        foreach (var scope in new[] { HintScanScope.ForegroundWindow, HintScanScope.ActiveMonitor })
        {
            int soft = ClickableScanner.TotalTimeoutFor(scope, visualDetection: true);
            int hard = ClickableScanner.HardTimeoutFor(scope, visualDetection: true);

            // The old behaviour dropped a 506 ms scan because its 400 ms timer
            // had already fired. A late answer is still shown.
            Assert.Equal(soft + ClickableScanner.TimeoutGraceMs, hard);
            Assert.True(hard > soft);
        }
    }

    [Fact]
    public void ThePixelPassHasItsOwnBudgetOnTopOfAutomation()
    {
        int automation = ClickableScanner.TimeoutFor(HintScanScope.ActiveMonitor);
        int withPixels = ClickableScanner.TotalTimeoutFor(HintScanScope.ActiveMonitor, visualDetection: true);
        int withoutPixels = ClickableScanner.TotalTimeoutFor(HintScanScope.ActiveMonitor, visualDetection: false);

        Assert.Equal(automation + ClickableScanner.VisualBudgetMs, withPixels);
        Assert.Equal(automation, withoutPixels);
    }

    [Fact]
    public void TwoPixelBoxesOnOneControlProduceOneTarget()
    {
        static ControlCandidate Control(Rectangle bounds)
        {
            var click = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
            return new ControlCandidate(
                bounds,
                ControlType.Button,
                VisualShape.Rectangle,
                VisualDetector.BorderEdge,
                0.85,
                0.8,
                ControlEvidence.Border)
            {
                Geometry = new TargetGeometry(bounds, bounds, click, 0.9, GeometryIssue.None, "test")
            };
        }

        var first = Control(new Rectangle(10, 10, 40, 20));
        var second = Control(new Rectangle(12, 12, 40, 20));
        var result = new VisualDetectionResult([], [first, second], [1.0], 5);

        var added = VisualHintSupplement.Add(result, new Rectangle(0, 0, 400, 300), [], maxElements: 400);

        // The higher-ranked box wins; the offset copy is not a second label.
        var target = Assert.Single(added);
        Assert.Equal(new Rectangle(10, 10, 40, 20), target.Bounds);
    }
}
