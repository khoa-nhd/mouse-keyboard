using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class ScreenScanPlannerTests
{
    private static readonly MonitorInfo Primary = new(
        new IntPtr(1),
        new Rectangle(0, 0, 1920, 1080),
        new Rectangle(0, 0, 1920, 1040),
        1.0);

    private static readonly MonitorInfo Left = new(
        new IntPtr(2),
        new Rectangle(-2560, 0, 2560, 1440),
        new Rectangle(-2560, 0, 2560, 1400),
        1.5);

    [Fact]
    public void ForegroundWindow_ClipsToMonitorUnion()
    {
        var window = new Rectangle(-50, -50, 2000, 1200);

        var regions = ScreenScanPlanner.RegionsForScope(
            HintScanScope.ForegroundWindow,
            window,
            [Primary],
            new Rectangle(0, 0, 1920, 1080));

        TargetBounds region = Assert.Single(regions);
        Assert.Equal(new TargetBounds(0, 0, 1920, 1080), region);
    }

    [Fact]
    public void ForegroundWindow_ReturnsEmpty_WhenWindowIsOffScreen()
    {
        var regions = ScreenScanPlanner.RegionsForScope(
            HintScanScope.ForegroundWindow,
            new Rectangle(5000, 5000, 300, 200),
            [Primary],
            new Rectangle(0, 0, 1920, 1080));

        Assert.Empty(regions);
    }

    [Fact]
    public void ForegroundWindow_ReturnsEmpty_WithoutWindowBounds()
    {
        var regions = ScreenScanPlanner.RegionsForScope(
            HintScanScope.ForegroundWindow,
            Rectangle.Empty,
            [Primary],
            new Rectangle(0, 0, 1920, 1080));

        Assert.Empty(regions);
    }

    [Fact]
    public void ActiveMonitor_FollowsTheWindowOntoANegativeOriginMonitor()
    {
        var regions = ScreenScanPlanner.RegionsForScope(
            HintScanScope.ActiveMonitor,
            new Rectangle(-2200, 100, 800, 600),
            [Primary, Left],
            new Rectangle(-2560, 0, 4480, 1440));

        TargetBounds region = Assert.Single(regions);
        Assert.Equal(new TargetBounds(-2560, 0, 2560, 1440), region);
    }

    [Fact]
    public void ActiveMonitor_StaysOnThePrimaryMonitor_ForAWindowThere()
    {
        var regions = ScreenScanPlanner.RegionsForScope(
            HintScanScope.ActiveMonitor,
            new Rectangle(200, 100, 800, 600),
            [Primary, Left],
            new Rectangle(-2560, 0, 4480, 1440));

        TargetBounds region = Assert.Single(regions);
        Assert.Equal(new TargetBounds(0, 0, 1920, 1080), region);
    }

    [Fact]
    public void VirtualDesktop_ReturnsEachMonitorSeparately_WithoutGapArea()
    {
        var regions = ScreenScanPlanner.RegionsForScope(
            HintScanScope.VirtualDesktop,
            Rectangle.Empty,
            [Left, Primary],
            new Rectangle(-2560, 0, 4480, 1440));

        Assert.Equal(2, regions.Count);
        Assert.Contains(new TargetBounds(-2560, 0, 2560, 1440), regions);
        Assert.Contains(new TargetBounds(0, 0, 1920, 1080), regions);

        // The union bounding box would be 4480x1440 and include the 1920x360
        // area underneath the shorter primary monitor. Capturing per monitor
        // must never produce a region taller than that monitor.
        Assert.DoesNotContain(
            regions,
            region => region.Bottom > region.Top + 1080 &&
                      region.Left >= 0);
    }

    [Fact]
    public void AllMonitors_MatchesVirtualDesktopCoverage()
    {
        var desktop = ScreenScanPlanner.RegionsForScope(
            HintScanScope.VirtualDesktop,
            Rectangle.Empty,
            [Left, Primary],
            Rectangle.Empty);
        var all = ScreenScanPlanner.RegionsForScope(
            HintScanScope.AllMonitors,
            Rectangle.Empty,
            [Left, Primary],
            Rectangle.Empty);

        Assert.Equal(desktop, all);
    }

    [Fact]
    public void Tile_CoversTheWholeRegion_WithOverlap_AndBoundedSize()
    {
        var region = new TargetBounds(-1920, -200, 4000, 2400);

        var tiles = ScreenScanPlanner.Tile(region);

        Assert.True(tiles.Count > 1);
        Assert.All(tiles, tile =>
        {
            Assert.True(tile.IsValid);
            Assert.True(tile.Width <= ScreenScanPlanner.MaxTileDimension);
            Assert.True(tile.Height <= ScreenScanPlanner.MaxTileDimension);
            Assert.True(tile.Left >= region.Left && tile.Top >= region.Top);
            Assert.True(tile.Right <= region.Right && tile.Bottom <= region.Bottom);
        });

        // Every sample point must be visible in at least one tile, including
        // the extreme bottom-right corner.
        for (int y = 0; y <= region.Height; y += 31)
        {
            for (int x = 0; x <= region.Width; x += 37)
            {
                var point = new Point(
                    region.Left + Math.Min(x, region.Width - 1),
                    region.Top + Math.Min(y, region.Height - 1));

                Assert.Contains(tiles, tile => tile.Contains(point));
            }
        }

        bool overlaps = false;
        foreach (TargetBounds first in tiles)
        {
            foreach (TargetBounds second in tiles)
            {
                if (first == second)
                {
                    continue;
                }

                TargetBounds shared = first.Intersect(second);
                if (shared.IsValid && shared.Width > 0 && shared.Height > 0)
                {
                    overlaps = true;
                    break;
                }
            }

            if (overlaps)
            {
                break;
            }
        }

        Assert.True(overlaps, "Adjacent tiles should overlap to avoid cut controls.");
    }

    [Fact]
    public void Tile_KeepsSmallRegionsWhole()
    {
        var region = new TargetBounds(-400, 120, 800, 600);

        TargetBounds tile = Assert.Single(ScreenScanPlanner.Tile(region));

        Assert.Equal(region, tile);
    }

    [Fact]
    public void Tile_ReturnsEmpty_ForAnInvalidRegion()
    {
        Assert.Empty(ScreenScanPlanner.Tile(TargetBounds.Empty));
        Assert.Empty(ScreenScanPlanner.Tile(new TargetBounds(0, 0, 0, 500)));
    }

    [Fact]
    public void Tile_NeverExceedsTheTileCap()
    {
        var region = new TargetBounds(0, 0, 100_000, 100_000);

        var tiles = ScreenScanPlanner.Tile(region);

        Assert.True(tiles.Count <= ScreenScanPlanner.MaxTiles);
    }

    [Theory]
    [InlineData("window", HintScanScope.ForegroundWindow, "window")]
    [InlineData(null, HintScanScope.ForegroundWindow, "window")]
    [InlineData("bogus", HintScanScope.ForegroundWindow, "window")]
    [InlineData("monitor", HintScanScope.ActiveMonitor, "monitor")]
    [InlineData("active monitor", HintScanScope.ActiveMonitor, "monitor")]
    [InlineData("desktop", HintScanScope.VirtualDesktop, "desktop")]
    [InlineData("virtual-desktop", HintScanScope.VirtualDesktop, "desktop")]
    [InlineData("all-monitors", HintScanScope.AllMonitors, "all-monitors")]
    [InlineData("ALL MONITORS", HintScanScope.AllMonitors, "all-monitors")]
    public void ScopeIds_ParseAndNormalize(
        string? value,
        HintScanScope expected,
        string expectedId)
    {
        Assert.Equal(expected, HintScanScopes.Parse(value));
        Assert.Equal(expected, HintScanScopes.Parse(HintScanScopes.Normalize(value)));
        Assert.Equal(expectedId, HintScanScopes.ToId(expected));
        Assert.Equal(expectedId, HintScanScopes.Normalize(value));
        Assert.False(string.IsNullOrWhiteSpace(HintScanScopes.DisplayName(expected)));
    }

    [Fact]
    public void ScopeBudgets_GrowWithTheScannedArea()
    {
        TimeSpan window = HintScanScopes.SourceBudgetFor(HintScanScope.ForegroundWindow);
        TimeSpan monitor = HintScanScopes.SourceBudgetFor(HintScanScope.ActiveMonitor);
        TimeSpan desktop = HintScanScopes.SourceBudgetFor(HintScanScope.VirtualDesktop);

        Assert.True(window < monitor);
        Assert.True(monitor < desktop);
        Assert.Equal(
            desktop,
            HintScanScopes.SourceBudgetFor(HintScanScope.AllMonitors));
        Assert.True(HintScanScopes.CoversDesktop(HintScanScope.ActiveMonitor));
        Assert.False(HintScanScopes.CoversDesktop(HintScanScope.ForegroundWindow));
    }
}
