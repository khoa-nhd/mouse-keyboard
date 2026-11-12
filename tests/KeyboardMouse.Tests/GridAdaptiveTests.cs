using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

/// <summary>
/// The grid divides the monitor into as many cells as it takes to keep a cell
/// about the size of the 28 x 18 default, so a bigger screen gets more cells.
/// </summary>
public class GridAdaptiveTests
{
    private static RouterHarness OpenGridOn(int width, int height)
    {
        var h = new RouterHarness(monitors: Monitor(width, height));
        h.GridSettings.AdaptToScreen = true;
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);
        return h;
    }

    private static FakeMonitorService Monitor(int width, int height) =>
        new(new MonitorInfo(
            new IntPtr(1),
            new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, width, height),
            1.0));

    [Theory]
    [InlineData(1920, 1200, 28, 18)] // the monitor the default was tuned on
    [InlineData(1280, 800, 19, 12)]  // smaller screen: fewer, not smaller cells
    [InlineData(2560, 1440, 37, 22)]
    [InlineData(3840, 2160, 56, 32)] // 4K: more cells of the same size
    public void LatticeFollowsTheMonitorSize(int width, int height, int columns, int rows)
    {
        var state = Assert.IsType<GridOverlayState>(OpenGridOn(width, height).Grid.State);

        Assert.Equal(columns, state.Columns);
        Assert.Equal(rows, state.Rows);
        Assert.Equal(columns * rows, state.Labels.Count);
    }

    [Fact]
    public void CellsKeepTheirSizeAcrossMonitors()
    {
        var laptop = Assert.IsType<GridOverlayState>(OpenGridOn(1920, 1200).Grid.State);
        var desktop = Assert.IsType<GridOverlayState>(OpenGridOn(3840, 2160).Grid.State);

        // Twice the screen is twice the columns, so a cell stays about as wide.
        Assert.Equal(laptop.Columns * 2, desktop.Columns);
        Assert.InRange(desktop.Root.Width / (double)desktop.Columns, 60, 76);
        Assert.InRange(laptop.Root.Width / (double)laptop.Columns, 60, 76);
    }

    [Fact]
    public void EveryCellStillCarriesAFourCharacterCodeOnABigMonitor()
    {
        var h = OpenGridOn(3840, 2160);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);

        // 56 columns need column bands 0..5: the first character is a band, the
        // second the column inside it.
        Assert.Equal("SFAK", state.Labels[(7 * state.Columns) + 13]);
        Assert.Equal(state.Columns * state.Rows, state.Labels.Distinct().Count());
    }

    [Fact]
    public void TheLatticeFollowsTheMonitorKey()
    {
        var h = new RouterHarness(monitors: new FakeMonitorService(
            new MonitorInfo(new IntPtr(1), new Rectangle(0, 0, 1920, 1200), new Rectangle(0, 0, 1920, 1200), 1.0),
            new MonitorInfo(new IntPtr(2), new Rectangle(1920, 0, 3840, 2160), new Rectangle(1920, 0, 3840, 2160), 1.0)));
        h.GridSettings.AdaptToScreen = true;
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);

        Assert.Equal(28, h.Grid.State!.Columns);

        h.Down(PhysicalKey.N);
        h.Up(PhysicalKey.N);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(new Rectangle(1920, 0, 3840, 2160), state.Monitor.Bounds);
        Assert.Equal(56, state.Columns);
        Assert.Equal(32, state.Rows);
    }

    [Fact]
    public void ACodeReachesTheCentreOfItsCellOnABigMonitor()
    {
        var h = OpenGridOn(3840, 2160);

        // Column 13, row 7 out of 56 x 32.
        foreach (var key in new[] { PhysicalKey.S, PhysicalKey.F, PhysicalKey.A, PhysicalKey.K })
        {
            h.Down(key);
            h.Up(key);
        }

        var expected = new Point(
            (int)(13.5 * (3840 / 56.0)),
            (int)(7.5 * (2160 / 32.0)));
        Assert.Equal($"move:{expected.X},{expected.Y}", h.Mouse.Calls[^1]);
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void TurningAdaptationOffUsesTheConfiguredSize()
    {
        var h = new RouterHarness(monitors: Monitor(3840, 2160));
        h.GridSettings.AdaptToScreen = false;
        h.GridSettings.Columns = 30;
        h.GridSettings.Rows = 20;
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(30, state.Columns);
        Assert.Equal(20, state.Rows);
    }

    [Fact]
    public void AdaptiveSizeIsTheDefault()
    {
        Assert.True(new GridSettings().AdaptToScreen);
        Assert.Equal(28, GridSettings.ColumnsFor(GridSettings.ReferenceWidth));
        Assert.Equal(18, GridSettings.RowsFor(GridSettings.ReferenceHeight));
    }
}
