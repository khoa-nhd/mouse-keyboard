using System.Drawing;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

public class GridControllerTests
{
    private static RouterHarness OpenGrid(FakeMonitorService? monitors = null)
    {
        var h = new RouterHarness(monitors: monitors);
        h.TapCaps();
        Assert.True(h.Down(PhysicalKey.G));
        Assert.True(h.Up(PhysicalKey.G));
        return h;
    }

    private static void Press(RouterHarness h, PhysicalKey key)
    {
        Assert.True(h.Down(key));
        Assert.True(h.Up(key));
    }

    /// <summary>Opens the grid and types the two keys of the first cell.</summary>
    private static RouterHarness OpenFirstCell()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);
        return h;
    }

    [Fact]
    public void G_OpensTheWholePrimaryLattice()
    {
        var h = OpenGrid();

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Primary, state.Phase);
        Assert.Equal(
            new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";" },
            state.Level1Labels);
        Assert.Equal(30, state.Level2Labels.Count);
        Assert.Equal(9, state.MicroLabels.Count);
        Assert.Equal(string.Empty, state.Typed);
        Assert.Null(state.Column);
        Assert.Null(state.Cell);
        Assert.Null(state.Subcell);
        Assert.Equal(h.Pointer, state.Target);
    }

    [Fact]
    public void ColumnKey_HighlightsTheColumnWithoutClicking()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.A);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Primary, state.Phase);
        Assert.Equal(new Rectangle(0, 0, 192, 1080), state.Column);
        Assert.Null(state.Cell);
        Assert.Equal("A", state.Typed);
        Assert.Equal(new Point(96, 540), state.Target);
        Assert.Equal(0, h.Mouse.Count("left-click"));
    }

    [Fact]
    public void ColumnThenRow_ZoomsIntoTheCell()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Subgrid, state.Phase);
        Assert.Equal(new Rectangle(0, 0, 192, 36), state.Cell);
        Assert.Equal("AQ", state.Typed);
        Assert.Equal(new Point(96, 18), state.Target);
        Assert.Equal(0, h.Mouse.Count("left-click"));
    }

    [Fact]
    public void SubgridKey_ZoomsIntoTheMicroGrid()
    {
        var h = OpenFirstCell();

        Press(h, PhysicalKey.Q);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Micro, state.Phase);
        Assert.Equal(new Rectangle(0, 0, 19, 12), state.Subcell);
        Assert.Equal("AQQ", state.Typed);
        Assert.Equal(new Point(9, 6), state.Target);
        Assert.Equal(0, h.Mouse.Count("left-click"));
    }

    [Fact]
    public void MicroKey_ClicksAtTheMicrocellCenter()
    {
        var h = OpenFirstCell();
        Press(h, PhysicalKey.Q);

        Press(h, PhysicalKey.Q);

        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.Equal(new Point(3, 2), h.Pointer);
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void SpaceClicksAtTheVirtualCursor_InEveryStage()
    {
        var primary = OpenGrid();
        Press(primary, PhysicalKey.Space);
        Assert.Equal(1, primary.Mouse.Count("left-click"));
        Assert.Equal(new Point(100, 100), primary.Pointer);

        var column = OpenGrid();
        Press(column, PhysicalKey.A);
        Press(column, PhysicalKey.Space);
        Assert.Equal(1, column.Mouse.Count("left-click"));
        Assert.Equal(new Point(96, 540), column.Pointer);

        var subgrid = OpenFirstCell();
        Press(subgrid, PhysicalKey.Space);
        Assert.Equal(1, subgrid.Mouse.Count("left-click"));
        Assert.Equal(new Point(96, 18), subgrid.Pointer);

        var micro = OpenFirstCell();
        Press(micro, PhysicalKey.Q);
        Press(micro, PhysicalKey.Space);
        Assert.Equal(1, micro.Mouse.Count("left-click"));
        Assert.Equal(new Point(9, 6), micro.Pointer);
    }

    [Fact]
    public void EnterAndShiftClickWithTheRightButton()
    {
        var enter = OpenGrid();
        Press(enter, PhysicalKey.Enter);
        Assert.Equal(1, enter.Mouse.Count("right-click"));
        Assert.Equal(new Point(100, 100), enter.Pointer);

        var shiftSpace = OpenGrid();
        Assert.False(shiftSpace.Down(PhysicalKey.LeftShift));
        Press(shiftSpace, PhysicalKey.Space);
        Assert.Equal(1, shiftSpace.Mouse.Count("right-click"));
        Assert.Equal(0, shiftSpace.Mouse.Count("left-click"));
        shiftSpace.Up(PhysicalKey.LeftShift);

        var micro = OpenFirstCell();
        Press(micro, PhysicalKey.Q);
        micro.Down(PhysicalKey.LeftShift);
        Press(micro, PhysicalKey.Q);
        micro.Up(PhysicalKey.LeftShift);

        Assert.Equal(1, micro.Mouse.Count("right-click"));
        Assert.Equal(0, micro.Mouse.Count("left-click"));
        Assert.Equal(new Point(3, 2), micro.Pointer);
    }

    [Fact]
    public void Backspace_StepsBackOneStage()
    {
        var h = OpenFirstCell();
        Press(h, PhysicalKey.Q);

        Assert.True(h.Down(PhysicalKey.Backspace));
        var subgrid = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Subgrid, subgrid.Phase);
        Assert.Null(subgrid.Subcell);
        Assert.Equal("AQ", subgrid.Typed);
        Assert.Equal(new Point(96, 18), subgrid.Target);

        Assert.True(h.Down(PhysicalKey.Backspace));
        var primary = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Primary, primary.Phase);
        Assert.Null(primary.Cell);
        Assert.Equal("A", primary.Typed);
        Assert.Equal(new Point(96, 540), primary.Target);

        Assert.True(h.Down(PhysicalKey.Backspace));
        var cleared = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(string.Empty, cleared.Typed);
        Assert.Null(cleared.Column);
        Assert.Equal(new Point(100, 100), cleared.Target);

        int shows = h.Grid.ShowCount;
        Assert.True(h.Down(PhysicalKey.Backspace));
        Assert.Equal(shows, h.Grid.ShowCount);
    }

    [Fact]
    public void ActionLevelSubgrid_ClicksOnTheSubcell()
    {
        var h = new RouterHarness();
        h.GridSettings.ActionLevel = "subgrid";
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);

        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);
        Press(h, PhysicalKey.Q);

        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.Equal(new Point(9, 6), h.Pointer);
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void ActionLevelTwo_ClicksAsSoonAsTheRowIsChosen()
    {
        var h = new RouterHarness();
        h.GridSettings.ActionLevel = "2";
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);

        Press(h, PhysicalKey.A);
        Assert.Equal(0, h.Mouse.Count("left-click"));
        Assert.True(h.Grid.Visible);

        Press(h, PhysicalKey.Q);

        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.Equal(new Point(96, 18), h.Pointer);
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void ActionLevelOne_ClicksAsSoonAsTheColumnIsChosen()
    {
        var h = new RouterHarness();
        h.GridSettings.ActionLevel = "1";
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);

        Press(h, PhysicalKey.A);

        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.Equal(new Point(96, 540), h.Pointer);
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void Tab_MovesToTheNextMonitor_AndResetsSelection()
    {
        var h = OpenGrid(FakeMonitorService.Dual());
        Press(h, PhysicalKey.A);

        Press(h, PhysicalKey.Tab);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(new Rectangle(1920, 0, 1280, 1024), state.Monitor.Bounds);
        Assert.Equal(GridOverlayPhase.Primary, state.Phase);
        Assert.Null(state.Column);
        Assert.Equal(string.Empty, state.Typed);

        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);
        Press(h, PhysicalKey.Q);
        Press(h, PhysicalKey.Q);

        Assert.True(h.Pointer.X >= 1920 && h.Pointer.X < 3200);
        Assert.True(h.Pointer.Y >= 0 && h.Pointer.Y < 1024);
    }

    [Fact]
    public void RepeatedColumnKey_DoesNotAdvanceTwice()
    {
        var h = OpenGrid();

        Assert.True(h.Down(PhysicalKey.A));
        Assert.True(h.Down(PhysicalKey.A, repeat: true));

        Assert.Equal(2, h.Grid.ShowCount);
        Assert.Equal(GridOverlayPhase.Primary, h.Grid.State!.Phase);
        Assert.Equal("A", h.Grid.State!.Typed);
    }

    [Fact]
    public void ModeOff_ClosesGridWithoutRestoringPointer()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.A);
        int moves = h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal));

        h.Router.SetMode(false);

        Assert.False(h.Grid.Visible);
        Assert.Equal(moves, h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Escape_CancelsAndRestoresThePointer()
    {
        var h = OpenFirstCell();

        Assert.True(h.Down(PhysicalKey.Escape));

        Assert.False(h.Grid.Visible);
        Assert.Equal("move:100,100", h.Mouse.Calls[^1]);
        Assert.Equal(new Point(100, 100), h.Pointer);
    }

    [Fact]
    public void CtrlGridKey_ClosesKeepingThePosition()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.A);

        Assert.False(h.Down(PhysicalKey.LeftCtrl));
        Assert.True(h.Down(PhysicalKey.G));

        Assert.False(h.Grid.Visible);
        Assert.Empty(h.Mouse.Calls);

        h.Up(PhysicalKey.G);
        h.Up(PhysicalKey.LeftCtrl);
    }

    [Fact]
    public void UnboundColumnKey_IsUnselectable()
    {
        var h = new RouterHarness();
        h.GridSettings.Level1Keys[0] = "";
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);
        int shows = h.Grid.ShowCount;

        Press(h, PhysicalKey.A);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal(string.Empty, h.Grid.State!.Typed);
        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void RowKey_DoesNothingBeforeAColumnIsChosen()
    {
        var h = OpenGrid();
        int shows = h.Grid.ShowCount;

        Press(h, PhysicalKey.Q);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal(GridOverlayPhase.Primary, h.Grid.State!.Phase);
        Assert.Equal(string.Empty, h.Grid.State!.Typed);
    }
}
