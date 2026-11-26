using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class TargetVisibilityTests
{
    private static readonly IntPtr Back = new(1);
    private static readonly IntPtr Front = new(2);

    // Back window fills the left of the screen; the front window covers x >= 400.
    private static readonly Rectangle BackFrame = new(0, 0, 800, 600);
    private static readonly Rectangle FrontFrame = new(400, 0, 400, 600);

    private static TargetVisibility Desktop() => new(
        point => FrontFrame.Contains(point) ? Front : BackFrame.Contains(point) ? Back : IntPtr.Zero,
        new Dictionary<IntPtr, Rectangle> { [Back] = BackFrame, [Front] = FrontFrame });

    [Fact]
    public void KeepsAnUncoveredTarget()
    {
        var target = new Rectangle(100, 100, 40, 20);

        Assert.True(Desktop().TryResolve(Back, target, out var visible, out var click));
        Assert.Equal(target, visible);
        Assert.Null(click);
    }

    [Fact]
    public void DropsATargetHiddenBehindAnotherWindow()
    {
        Assert.False(Desktop().TryResolve(Back, new Rectangle(500, 100, 40, 20), out _, out _));
    }

    [Fact]
    public void DropsContentScrolledOutOfItsOwnWindow()
    {
        // The front window's list continues below its frame.
        Assert.False(Desktop().TryResolve(Front, new Rectangle(450, 700, 40, 20), out _, out _));
    }

    [Fact]
    public void ClipsATargetToItsWindowFrame()
    {
        Assert.True(Desktop().TryResolve(Front, new Rectangle(450, 580, 40, 40), out var visible, out _));
        Assert.True(visible.Bottom <= FrontFrame.Bottom + TargetVisibility.FrameTolerance);
    }

    [Fact]
    public void MovesTheClickOntoTheUncoveredPartOfAPartlyHiddenTarget()
    {
        // 300..500: the right part is under the front window.
        var target = new Rectangle(300, 100, 200, 30);

        Assert.True(Desktop().TryResolve(Back, target, out var visible, out var click));
        Assert.NotNull(click);
        Assert.True(click!.Value.X < FrontFrame.Left, $"click {click} is under the front window");
        Assert.True(visible.Right <= FrontFrame.Left + 1, $"label cell {visible} reaches under the front window");
    }

    [Fact]
    public void TrustsTargetsOfAnUnknownWindow()
    {
        var target = new Rectangle(500, 100, 40, 20);

        Assert.True(Desktop().TryResolve(IntPtr.Zero, target, out var visible, out _));
        Assert.Equal(target, visible);
    }

    [Fact]
    public void FilterDropsUnnamedPanelSizedWrappersAroundRealControls()
    {
        var monitor = new MonitorInfo(new IntPtr(9), BackFrame, BackFrame, 1.0);
        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(10, 10, 700, 300), false, true),
            new RawClickableTarget(new Rectangle(20, 20, 40, 20), false, true, "Copy"),
            new RawClickableTarget(new Rectangle(80, 20, 40, 20), false, true, "Share"),
            // A named wide row is a real target even with controls inside it.
            new RawClickableTarget(new Rectangle(10, 400, 700, 40), false, true, "Inbox row"),
            new RawClickableTarget(new Rectangle(20, 410, 20, 20), false, true, "Star"),
            new RawClickableTarget(new Rectangle(60, 410, 20, 20), false, true, "Select")
        };

        var result = HintTargetFilter.Apply(raw, [monitor], 100);

        Assert.Equal(["Copy", "Share", "Inbox row", "Star", "Select"], result.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void FilterDropsCoveredTargetsAndKeepsVisibleOnes()
    {
        var monitor = new MonitorInfo(new IntPtr(9), BackFrame, BackFrame, 1.0);
        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(100, 100, 40, 20), false, true, "visible") { Window = Back },
            new RawClickableTarget(new Rectangle(500, 100, 40, 20), false, true, "covered") { Window = Back },
            new RawClickableTarget(new Rectangle(500, 140, 40, 20), false, true, "front") { Window = Front }
        };

        var result = HintTargetFilter.Apply(raw, [monitor], 100, visibility: Desktop());

        Assert.Equal(["visible", "front"], result.Select(t => t.Name).ToArray());
    }
}
