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

    [Fact]
    public void G_OpensMouselessLevel1_WithTenColumnKeys()
    {
        var h = OpenGrid();

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Level1, state.Phase);
        Assert.Equal(
            new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";" },
            state.Level1Labels);
        Assert.Equal(h.Pointer, state.Target);
        Assert.Null(state.Column);
        Assert.Null(state.Cell);
    }

    [Fact]
    public void Level1ThenLevel2_SelectsColumnAndBand_WithoutClicking()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.A);

        var level2 = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Level2, level2.Phase);
        Assert.Equal(new Rectangle(0, 0, 192, 1080), level2.Column);
        Assert.Null(level2.Cell);
        Assert.Equal(new Point(96, 540), level2.Target);
        Assert.Equal(0, h.Mouse.Count("left-click"));

        Press(h, PhysicalKey.Q);

        var subgrid = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Subgrid, subgrid.Phase);
        Assert.Equal(new Rectangle(0, 0, 192, 36), subgrid.Cell);
        Assert.Equal(new Point(96, 18), subgrid.Target);
        Assert.Equal(0, h.Mouse.Count("left-click"));
    }

    [Fact]
    public void SubgridKey_MovesAndLeftClicksAtSubcellCenter()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);

        Press(h, PhysicalKey.Q);

        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.Equal(new Point(9, 6), h.Pointer);
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void SpaceClicksAtTheVirtualCursor_InEveryPhase()
    {
        var level1 = OpenGrid();
        Press(level1, PhysicalKey.Space);
        Assert.Equal(1, level1.Mouse.Count("left-click"));
        Assert.Equal(new Point(100, 100), level1.Pointer);

        var level2 = OpenGrid();
        Press(level2, PhysicalKey.A);
        Press(level2, PhysicalKey.Space);
        Assert.Equal(1, level2.Mouse.Count("left-click"));
        Assert.Equal(new Point(96, 540), level2.Pointer);

        var subgrid = OpenGrid();
        Press(subgrid, PhysicalKey.A);
        Press(subgrid, PhysicalKey.Q);
        Press(subgrid, PhysicalKey.Space);
        Assert.Equal(1, subgrid.Mouse.Count("left-click"));
        Assert.Equal(new Point(96, 18), subgrid.Pointer);
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

        var shiftSubgrid = OpenGrid();
        Press(shiftSubgrid, PhysicalKey.A);
        Press(shiftSubgrid, PhysicalKey.Q);
        shiftSubgrid.Down(PhysicalKey.LeftShift);
        Press(shiftSubgrid, PhysicalKey.Q);
        shiftSubgrid.Up(PhysicalKey.LeftShift);

        Assert.Equal(1, shiftSubgrid.Mouse.Count("right-click"));
        Assert.Equal(new Point(9, 6), shiftSubgrid.Pointer);
    }

    [Fact]
    public void Backspace_StepsBackOneLevelAtATime()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);

        Assert.True(h.Down(PhysicalKey.Backspace));
        var level2 = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Level2, level2.Phase);
        Assert.Null(level2.Cell);
        Assert.Equal(new Point(96, 540), level2.Target);

        Assert.True(h.Down(PhysicalKey.Backspace));
        var level1 = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(GridOverlayPhase.Level1, level1.Phase);
        Assert.Null(level1.Column);
        Assert.Equal(new Point(100, 100), level1.Target);

        int shows = h.Grid.ShowCount;
        Assert.True(h.Down(PhysicalKey.Backspace));
        Assert.Equal(shows, h.Grid.ShowCount);
    }

    [Fact]
    public void Escape_CancelsAndRestoresThePointer()
    {
        var h = OpenGrid();
        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);

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
    public void ActionLevelTwo_ClicksAsSoonAsTheBandIsChosen()
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
        Assert.Equal(GridOverlayPhase.Level1, state.Phase);
        Assert.Null(state.Column);

        Press(h, PhysicalKey.A);
        Press(h, PhysicalKey.Q);
        Press(h, PhysicalKey.Q);

        Assert.True(h.Pointer.X >= 1920 && h.Pointer.X < 3200);
        Assert.True(h.Pointer.Y >= 0 && h.Pointer.Y < 1024);
    }

    [Fact]
    public void RepeatedCellDown_DoesNotAdvanceTwice()
    {
        var h = OpenGrid();

        Assert.True(h.Down(PhysicalKey.A));
        Assert.True(h.Down(PhysicalKey.A, repeat: true));

        Assert.Equal(2, h.Grid.ShowCount);
        Assert.Equal(GridOverlayPhase.Level2, h.Grid.State!.Phase);
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
    public void UnboundLevel1Key_IsUnselectable()
    {
        var h = new RouterHarness();
        h.GridSettings.Level1Keys[0] = "";
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);
        int shows = h.Grid.ShowCount;

        Press(h, PhysicalKey.A);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal(GridOverlayPhase.Level1, h.Grid.State!.Phase);
        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void Level2Key_DoesNothingAtLevel1()
    {
        var h = OpenGrid();
        int shows = h.Grid.ShowCount;

        Press(h, PhysicalKey.Q);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal(GridOverlayPhase.Level1, h.Grid.State!.Phase);
    }
}
