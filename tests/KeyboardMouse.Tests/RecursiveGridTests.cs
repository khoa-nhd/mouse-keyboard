using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Controllers;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

/// <summary>
/// The recursive grid (the default style): a 3 x 3 grid that each cell key
/// narrows into one cell, until the last level parks the pointer and closes.
/// </summary>
public class RecursiveGridTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);

    private static RouterHarness OpenGrid(FakeMonitorService? monitors = null, int maxDepth = 3)
    {
        var h = new RouterHarness(monitors: monitors);
        h.GridSettings.Style = GridSettings.RecursiveStyle;
        h.GridSettings.MaxDepth = maxDepth;
        h.TapCaps();
        Press(h, PhysicalKey.G);
        return h;
    }

    private static void Press(RouterHarness h, PhysicalKey key)
    {
        Assert.True(h.Down(key));
        Assert.True(h.Up(key));
    }

    private static Rectangle Cell(Rectangle parent, int index) =>
        GridMath.SubRect(parent, index, GridController.RecursiveSide, GridController.RecursiveSide);

    private static string Move(Point point) => $"move:{point.X},{point.Y}";

    [Fact]
    public void RecursiveIsTheDefaultStyle()
    {
        var settings = new GridSettings();

        Assert.Equal(GridSettings.RecursiveStyle, settings.Style);
        Assert.True(settings.IsRecursive);
        Assert.Equal(3, settings.MaxDepth);
        Assert.Equal(["U", "I", "O", "J", "K", "L", "M", "Comma", "Period"], settings.RecursiveKeys);
    }

    [Fact]
    public void G_OpensAThreeByThreeGridOverTheMonitorWithoutMoving()
    {
        var h = OpenGrid();

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.True(state.Recursive);
        Assert.Equal(3, state.Columns);
        Assert.Equal(3, state.Rows);
        Assert.Equal(Screen, state.Root);
        Assert.Equal(0, state.Depth);
        Assert.Equal(3, state.MaxDepth);
        Assert.Equal(["U", "I", "O", "J", "K", "L", "M", ",", "."], state.Labels);
        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void ACellKeyMovesToThatCellAndMakesItTheNextGrid()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.O);

        var topRight = Cell(Screen, 2);
        Assert.Equal(new Rectangle(1280, 0, 640, 360), topRight);
        Assert.Equal([Move(GridMath.Center(topRight))], h.Mouse.Calls);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.True(h.Grid.Visible);
        Assert.Equal(topRight, state.Root);
        Assert.Equal(1, state.Depth);
    }

    [Fact]
    public void TheLastLevelParksThePointerAndClosesWithoutClicking()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.O);
        Press(h, PhysicalKey.J);
        Press(h, PhysicalKey.K);

        var level3 = Cell(Cell(Cell(Screen, 2), 3), 4);
        Assert.False(h.Grid.Visible);
        Assert.Equal(Move(GridMath.Center(level3)), h.Mouse.Calls[^1]);
        Assert.Equal(GridMath.Center(level3), h.Pointer);
        Assert.Equal(0, h.Mouse.Count("left-click"));
        Assert.Equal(0, h.Mouse.Count("right-click"));
    }

    [Fact]
    public void MaxDepthDecidesHowManyLevelsThereAre()
    {
        var h = OpenGrid(maxDepth: 2);

        Press(h, PhysicalKey.M);
        Assert.True(h.Grid.Visible);

        Press(h, PhysicalKey.Period);
        Assert.False(h.Grid.Visible);
        Assert.Equal(GridMath.Center(Cell(Cell(Screen, 6), 8)), h.Pointer);
    }

    [Fact]
    public void BackspaceGoesUpALevelAndFinallyBackToTheStart()
    {
        var h = OpenGrid();
        var start = h.Pointer;

        Press(h, PhysicalKey.O);
        Press(h, PhysicalKey.J);
        Press(h, PhysicalKey.Backspace);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(Cell(Screen, 2), state.Root);
        Assert.Equal(1, state.Depth);
        Assert.Equal(GridMath.Center(Cell(Screen, 2)), h.Pointer);

        Press(h, PhysicalKey.Backspace);
        state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(Screen, state.Root);
        Assert.Equal(0, state.Depth);
        Assert.Equal(start, h.Pointer);
        Assert.True(h.Grid.Visible);
    }

    [Fact]
    public void SpaceClicksWherethePointerIsAndCloses()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.I);
        Press(h, PhysicalKey.Space);

        Assert.False(h.Grid.Visible);
        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.Equal(GridMath.Center(Cell(Screen, 1)), h.Pointer);
    }

    [Fact]
    public void ShiftSpaceAndEnterRightClick()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.I);
        Assert.False(h.Down(PhysicalKey.LeftShift));
        Press(h, PhysicalKey.Space);
        Assert.False(h.Up(PhysicalKey.LeftShift));
        Assert.Equal(1, h.Mouse.Count("right-click"));

        var h2 = OpenGrid();
        Press(h2, PhysicalKey.K);
        Press(h2, PhysicalKey.Enter);
        Assert.Equal(1, h2.Mouse.Count("right-click"));
        Assert.False(h2.Grid.Visible);
    }

    [Theory]
    [InlineData(PhysicalKey.Tab)]
    [InlineData(PhysicalKey.G)]
    public void TabOrTheGridKeyStopsHereWithoutClicking(PhysicalKey key)
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.L);
        Press(h, key);

        Assert.False(h.Grid.Visible);
        Assert.Equal(GridMath.Center(Cell(Screen, 5)), h.Pointer);
        Assert.Equal(0, h.Mouse.Count("left-click"));
    }

    [Fact]
    public void EscapeRestoresTheOriginalPointer()
    {
        var h = OpenGrid();
        var start = h.Pointer;

        Press(h, PhysicalKey.U);
        Press(h, PhysicalKey.Escape);

        Assert.False(h.Grid.Visible);
        Assert.Equal(start, h.Pointer);
    }

    [Fact]
    public void NextMonitorKeyStartsOverOnTheOtherMonitor()
    {
        var h = OpenGrid(FakeMonitorService.Dual());

        Press(h, PhysicalKey.U);
        Press(h, PhysicalKey.N);

        var second = new Rectangle(1920, 0, 1280, 1024);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(second, state.Root);
        Assert.Equal(0, state.Depth);
        Assert.Equal(GridMath.Center(second), h.Pointer);
    }

    [Fact]
    public void KeysThatAreNotCellsOrControlsAreIgnored()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Z);

        Assert.True(h.Grid.Visible);
        Assert.Empty(h.Mouse.Calls);
        Assert.Equal(0, Assert.IsType<GridOverlayState>(h.Grid.State).Depth);
    }

    [Fact]
    public void RemappedCellKeysAreUsed()
    {
        var h = new RouterHarness();
        h.GridSettings.Style = GridSettings.RecursiveStyle;
        h.GridSettings.RecursiveKeys = ["Q", "W", "E", "A", "S", "D", "Z", "X", "C"];
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);

        Press(h, PhysicalKey.C);

        Assert.Equal(GridMath.Center(Cell(Screen, 8)), h.Pointer);
        Assert.Equal(["Q", "W", "E", "A", "S", "D", "Z", "X", "C"], h.Grid.State!.Labels);
    }

    [Fact]
    public void TheCodesStyleStillOpensTheFlatGrid()
    {
        var h = new RouterHarness();
        h.GridSettings.Style = GridSettings.CodesStyle;
        h.TapCaps();
        Press(h, PhysicalKey.G);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.False(state.Recursive);
        Assert.Equal(28, state.Columns);
        Assert.Equal(3, state.Labels[0].Length);
    }

    [Theory]
    [InlineData(null, "recursive")]
    [InlineData("", "recursive")]
    [InlineData("nonsense", "recursive")]
    [InlineData("Codes", "codes")]
    [InlineData(" codes ", "codes")]
    [InlineData("RECURSIVE", "recursive")]
    public void NormalizeMapsAnyStyleOntoAKnownOne(string? stored, string expected)
    {
        var settings = new Settings();
        settings.Grid.Style = stored!;

        settings.Normalize();

        Assert.Equal(expected, settings.Grid.Style);
    }

    [Fact]
    public void NormalizeRepairsDepthAndCellKeys()
    {
        var settings = new Settings();
        settings.Grid.MaxDepth = 42;
        settings.Grid.RecursiveKeys = ["U", "I"];

        settings.Normalize();

        Assert.Equal(GridSettings.MaxMaxDepth, settings.Grid.MaxDepth);
        Assert.Equal(GridSettings.DefaultRecursiveKeys(), settings.Grid.RecursiveKeys);

        settings.Grid.MaxDepth = 0;
        settings.Grid.RecursiveKeys = ["U", "U", "O", "J", "K", "L", "M", "Comma", "Space"];
        settings.Normalize();

        Assert.Equal(GridSettings.MinMaxDepth, settings.Grid.MaxDepth);
        // The duplicate returns to its default; a reserved key does too.
        Assert.Equal(GridSettings.DefaultRecursiveKeys(), settings.Grid.RecursiveKeys);
    }

    [Fact]
    public void CellKeysSwapInsideTheirOwnPoolOnly()
    {
        var settings = new Settings();

        Assert.True(KeyBindingCatalog.TryAssign(settings, "rgrid0", "K", out var swapped));

        Assert.Equal("rgrid4", swapped);
        Assert.Equal("K", settings.Grid.RecursiveKeys[0]);
        Assert.Equal("U", settings.Grid.RecursiveKeys[4]);
        // K is also the normal-layer Down key and a code-grid key: both untouched.
        Assert.Equal("K", settings.Keys.Down);
        Assert.Equal("K", settings.Grid.Keys[7]);
    }

    [Fact]
    public void CellKeysRejectGridReservedKeys()
    {
        var settings = new Settings();

        Assert.False(KeyBindingCatalog.TryAssign(settings, "rgrid0", "Space", out _));
        Assert.False(KeyBindingCatalog.TryAssign(settings, "rgrid0", "Tab", out _));
        Assert.Equal("U", settings.Grid.RecursiveKeys[0]);
    }
}
