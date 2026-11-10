using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class HintTargetFilterTests
{
    private static readonly MonitorInfo Monitor = new(
        new IntPtr(1),
        new Rectangle(0, 0, 100, 100),
        new Rectangle(0, 0, 100, 100),
        1.0);

    [Fact]
    public void FiltersOffscreenDisabledEmptyAndOutsideTargets()
    {
        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(10, 10, 20, 20), IsOffscreen: true, IsEnabled: true),
            new RawClickableTarget(new Rectangle(10, 10, 20, 20), IsOffscreen: false, IsEnabled: false),
            new RawClickableTarget(new Rectangle(10, 10, 0, 20), IsOffscreen: false, IsEnabled: true),
            new RawClickableTarget(new Rectangle(200, 200, 20, 20), IsOffscreen: false, IsEnabled: true),
            new RawClickableTarget(new Rectangle(10, 10, 20, 20), IsOffscreen: false, IsEnabled: true)
        };

        var result = HintTargetFilter.Apply(raw, new[] { Monitor }, maxElements: 100);

        var single = Assert.Single(result);
        Assert.Equal(new Rectangle(10, 10, 20, 20), single.Bounds);
    }

    [Fact]
    public void ClipsTargetsToTheMonitor_AndCentersOnTheVisiblePart()
    {
        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(-20, -20, 60, 60), IsOffscreen: false, IsEnabled: true)
        };

        var result = HintTargetFilter.Apply(raw, new[] { Monitor }, maxElements: 100);

        var target = Assert.Single(result);
        Assert.Equal(new Rectangle(0, 0, 40, 40), target.Bounds);
        Assert.Equal(new Point(20, 20), target.Center);
    }

    [Fact]
    public void SortsTopToBottom_ThenLeftToRight()
    {
        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(50, 50, 20, 20), false, true),
            new RawClickableTarget(new Rectangle(5, 50, 20, 20), false, true),
            new RawClickableTarget(new Rectangle(50, 5, 20, 20), false, true)
        };

        var result = HintTargetFilter.Apply(raw, new[] { Monitor }, maxElements: 100);

        Assert.Equal(new Point(60, 15), result[0].Center);
        Assert.Equal(new Point(15, 60), result[1].Center);
        Assert.Equal(new Point(60, 60), result[2].Center);
    }

    [Fact]
    public void DeduplicatesTargetsWhoseCentersAreWithinFourPixels()
    {
        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(10, 10, 20, 20), false, true),
            new RawClickableTarget(new Rectangle(12, 12, 18, 18), false, true),
            new RawClickableTarget(new Rectangle(10, 60, 20, 20), false, true)
        };

        var result = HintTargetFilter.Apply(raw, new[] { Monitor }, maxElements: 100);

        Assert.Equal(2, result.Count);
        Assert.Equal(new Point(20, 20), result[0].Center);
        Assert.Equal(new Point(20, 70), result[1].Center);
    }

    [Fact]
    public void CapsTheNumberOfTargets_AcrossTheWholeScreen()
    {
        // Ten targets on a diagonal, capped at three. Sampling the ordered list
        // keeps the top, the middle and the bottom: taking the first three left
        // the rest of the screen without hints.
        var raw = Enumerable.Range(0, 10)
            .Select(i => new RawClickableTarget(
                new Rectangle(5 + (i * 9), 5 + (i * 9), 5, 5),
                false,
                true))
            .ToArray();

        var result = HintTargetFilter.Apply(raw, new[] { Monitor }, maxElements: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(new Point(7, 7), result[0].Center);
        Assert.Equal(new Point(34, 34), result[1].Center);
        Assert.Equal(new Point(70, 70), result[2].Center);
    }

    [Fact]
    public void AcceptsTargetsOnAnyMonitor()
    {
        var second = new MonitorInfo(
            new IntPtr(2),
            new Rectangle(100, 0, 100, 100),
            new Rectangle(100, 0, 100, 100),
            1.0);

        var raw = new[]
        {
            new RawClickableTarget(new Rectangle(150, 20, 20, 20), false, true)
        };

        var result = HintTargetFilter.Apply(raw, new[] { Monitor, second }, maxElements: 100);

        var target = Assert.Single(result);
        Assert.Equal(new Point(160, 30), target.Center);
    }
}
