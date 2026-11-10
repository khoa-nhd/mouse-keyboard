using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Tests;

/// <summary>
/// Windows report as they answer; a report must cover exactly the windows that
/// are done and must not repeat a batch the overlay already has.
/// </summary>
public class HintScanBatchTests
{
    [Fact]
    public void Report_HoldsOnlyTheWindowsThatAnswered()
    {
        var items = new List<RawClickableTarget>?[]
        {
            Done(0, 0),
            null,
            Done(100, 0)
        };

        int reported = 0;
        List<RawClickableTarget>? got = null;

        bool built = ClickableScanner.ReportReady(items, ref reported, batch => got = batch);

        Assert.True(built);
        Assert.NotNull(got);
        Assert.Equal(2, got!.Count);
        Assert.Equal(new Rectangle(0, 0, 40, 20), got[0].Bounds);
        Assert.Equal(new Rectangle(100, 0, 40, 20), got[1].Bounds);
        Assert.Equal(2, reported);
    }

    [Fact]
    public void Report_RepeatsNothingWhenNoWindowAnsweredSince()
    {
        var items = new List<RawClickableTarget>?[] { Done(0, 0), null };
        int reported = 1;

        bool built = ClickableScanner.ReportReady(
            items,
            ref reported,
            _ => Assert.Fail("this batch is already on screen"));

        Assert.False(built);
    }

    [Fact]
    public void Report_WithNothingReadyIsSkipped()
    {
        var items = new List<RawClickableTarget>?[] { Done(0, 0, []), null };
        int reported = 0;

        bool built = ClickableScanner.ReportReady(
            items,
            ref reported,
            _ => Assert.Fail("an empty batch would only flicker"));

        Assert.False(built);
    }

    private static List<RawClickableTarget> Done(int x, int y, List<RawClickableTarget>? items = null) =>
        items ?? [new RawClickableTarget(new Rectangle(x, y, 40, 20), false, true)];
}
