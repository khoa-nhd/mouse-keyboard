using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class HitConfirmationTests
{
    private static RawClickableTarget Item(int x, int y, string name = "") =>
        new(new Rectangle(x, y, 40, 40), false, true, name);

    [Fact]
    public void DropsAControlItsOwnWindowDrawsContentOver()
    {
        // The page (a big document) is what the hit test finds over the
        // collapsed tab strip's close button; the toolbar button answers itself.
        var page = new Rectangle(95, 74, 1259, 1043);
        var items = new[] { Item(10, 10, "Back"), Item(297, 432, "Close tab"), Item(60, 10, "Reload") };

        var kept = HitConfirmation.Filter(
            items,
            _ => true,
            point => items.Take(1).Concat(items.Skip(2)).FirstOrDefault(i => i.Bounds.Contains(point))?.Bounds ?? page);

        Assert.Equal(["Back", "Reload"], kept.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void KeepsAControlWhoseHitIsAPartOfIt()
    {
        var items = new[] { Item(100, 100, "Save") };

        var kept = HitConfirmation.Filter(items, _ => true, _ => new Rectangle(110, 110, 16, 16));

        Assert.Single(kept);
    }

    [Fact]
    public void KeepsAControlWhenTheHitIsItsOwnContainer()
    {
        // Chrome answers with the whole tab-strip pane for a visible tab-strip
        // button, and with the page for a hidden one.
        var strip = new Rectangle(0, 0, 84, 1128);
        var page = new Rectangle(96, 158, 1238, 938);
        var items = new[]
        {
            new RawClickableTarget(new Rectangle(18, 12, 48, 48), false, true, "Expand tabs"),
            new RawClickableTarget(new Rectangle(297, 432, 42, 42), false, true, "Hidden close"),
            new RawClickableTarget(new Rectangle(1802, 87, 42, 42), false, true, "Pop-out")
        };

        var kept = HitConfirmation.Filter(
            items,
            _ => true,
            point => strip.Contains(point) ? strip : page.Contains(point) ? page : items[2].Bounds,
            (target, hit) => target.Name == "Expand tabs" && hit == strip);

        Assert.Equal(["Expand tabs", "Pop-out"], kept.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void KeepsCaptionButtonsWhoseHitIsTheTitleBar()
    {
        // The hit test answers with the 66 px title bar, not an ancestor of the
        // button but only a strip it sits on.
        var titleBar = new Rectangle(0, 0, 1920, 66);
        var items = new[] { new RawClickableTarget(new Rectangle(1851, 0, 69, 54), false, true, "Close") };

        var kept = HitConfirmation.Filter(items, _ => true, _ => titleBar, (_, _) => false);

        Assert.Single(kept);
    }

    [Fact]
    public void DropsAHiddenBarButtonUnderAToolbarTwiceItsHeight()
    {
        // Chrome's saved tab-group bar, hidden under the PDF viewer's toolbar.
        var toolbar = new Rectangle(95, 74, 1259, 84);
        var items = new[]
        {
            new RawClickableTarget(new Rectangle(174, 78, 47, 30), false, true, "Hidden group"),
            new RawClickableTarget(new Rectangle(640, 92, 48, 48), false, true, "Zoom in")
        };

        var kept = HitConfirmation.Filter(
            items,
            _ => true,
            point => items[1].Bounds.Contains(point) ? items[1].Bounds : toolbar,
            (_, _) => false);

        Assert.Equal(["Zoom in"], kept.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void KeepsARowWhoseTruncatedLabelOverflowsIt()
    {
        // A sidebar link 296 px wide whose title text reports its full width.
        var items = new[] { new RawClickableTarget(new Rectangle(12, 537, 296, 48), false, true, "Chat") };

        var kept = HitConfirmation.Filter(items, _ => true, _ => new Rectangle(27, 546, 573, 30));

        Assert.Single(kept);
    }

    [Fact]
    public void TrustsAProviderWhoseHitTestFailsForMostTargets()
    {
        // A provider that answers with its whole pane everywhere.
        var items = Enumerable.Range(0, 8).Select(i => Item(50 * i, 10, $"b{i}")).ToArray();

        var kept = HitConfirmation.Filter(items, _ => true, _ => new Rectangle(0, 0, 800, 600));

        Assert.Equal(8, kept.Count);
    }

    [Fact]
    public void LeavesPointsAnotherWindowOwnsToTheWindowFilter()
    {
        var items = new[] { Item(100, 100, "Covered by another app") };

        var kept = HitConfirmation.Filter(items, _ => false, _ => new Rectangle(0, 0, 800, 600));

        Assert.Single(kept);
    }

    [Fact]
    public void KeepsTargetsWhenTheHitTestCannotAnswer()
    {
        var items = new[] { Item(100, 100), Item(200, 100) };

        var kept = HitConfirmation.Filter(items, _ => true, _ => throw new InvalidOperationException());

        Assert.Equal(2, kept.Count);
    }
}
