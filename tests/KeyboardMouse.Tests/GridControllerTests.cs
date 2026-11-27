using System.Drawing;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

/// <summary>
/// Grid mode is one flat grid whose cells each show a three-character code from
/// the moment it opens: block, column in the block, row in the block. On the
/// 28 x 18 default the blocks are 3 x 2: columns 0-8, 9-17, 18-27 and rows
/// 0-8, 9-17, named A S D (top) and F G H (bottom).
/// </summary>
public class GridControllerTests
{
    private static readonly PhysicalKey[] GridKeys =
    [
        PhysicalKey.A,
        PhysicalKey.S,
        PhysicalKey.D,
        PhysicalKey.F,
        PhysicalKey.G,
        PhysicalKey.H,
        PhysicalKey.J,
        PhysicalKey.K,
        PhysicalKey.L,
        PhysicalKey.Semicolon
    ];

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

    private static void Type(RouterHarness h, string code)
    {
        foreach (char character in code)
        {
            int index = Array.IndexOf(
                new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";" },
                character.ToString());
            Assert.InRange(index, 0, GridKeys.Length - 1);
            Press(h, GridKeys[index]);
        }
    }

    [Fact]
    public void G_OpensTheWholeGridWithAThreeCharacterCodeInEveryCell()
    {
        var h = OpenGrid();

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(28, state.Columns);
        Assert.Equal(18, state.Rows);
        Assert.Equal(28 * 18, state.Labels.Count);
        Assert.All(state.Labels, label => Assert.Equal(3, label.Length));
        Assert.Equal(state.Labels.Count, state.Labels.Distinct().Count());
        Assert.Equal(3, state.BlocksAcross);
        Assert.Equal(2, state.BlocksDown);

        // Every code is readable before typing: block, column, row.
        Assert.Equal("AAA", state.Labels[0]);
        Assert.Equal("ASA", state.Labels[1]);
        Assert.Equal("AGA", state.Labels[4]);
        Assert.Equal("AAS", state.Labels[28]);
        Assert.Equal("AGS", state.Labels[(1 * 28) + 4]);
        Assert.Equal("SAA", state.Labels[9]);
        Assert.Equal("FAA", state.Labels[9 * 28]);

        Assert.Equal(string.Empty, state.Typed);
        Assert.Equal(-1, state.Highlight);
        Assert.Equal(h.Pointer, state.Target);
        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void EachCharacterNarrowsOneBandWithoutClicking()
    {
        var h = OpenGrid();

        Press(h, PhysicalKey.A);

        var afterBand = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal("A", afterBand.Typed);
        Assert.Equal(0, afterBand.Highlight);
        Assert.Equal(new Point(34, 30), afterBand.Target);

        Press(h, PhysicalKey.S);

        var afterColumn = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal("AS", afterColumn.Typed);
        Assert.Equal(1, afterColumn.Highlight);
        Assert.Equal(new Point(102, 30), afterColumn.Target);

        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void TypingAllThreeCharactersOnlyMovesThePointerAndClosesTheGrid()
    {
        var h = OpenGrid();

        // Column 13, row 7: block S (columns 9-17), column 4 of it, row 7 (K).
        Type(h, "SGK");

        Assert.False(h.Grid.Visible);
        Assert.Equal(["move:925,450"], h.Mouse.Calls);
    }

    [Fact]
    public void SpaceAfterACompleteCodeClicksThroughMouseMode()
    {
        var h = OpenGrid();
        Type(h, "SGK");

        // The grid is closed, so Space is mouse mode's own left click.
        Press(h, PhysicalKey.Space);

        Assert.Equal(["move:925,450", "left-down", "left-up"], h.Mouse.Calls);
    }

    [Fact]
    public void EveryCell_IsReachedByItsOwnCodeAndClickedWithSpace()
    {
        foreach (var (code, expected) in new[]
        {
            ("AAA", new Point(34, 30)),
            ("ASA", new Point(102, 30)),
            ("SGK", new Point(925, 450)),
            ("HHJ", new Point(1611, 930))
        })
        {
            var h = OpenGrid();
            Type(h, code);

            Assert.False(h.Grid.Visible);
            Assert.Equal($"move:{expected.X},{expected.Y}", h.Mouse.Calls[^1]);
            Assert.DoesNotContain("left-click", h.Mouse.Calls);

            Press(h, PhysicalKey.Space);

            Assert.Contains("left-down", h.Mouse.Calls);
        }
    }

    [Fact]
    public void UnknownKey_IsIgnored()
    {
        var h = OpenGrid();
        int shows = h.Grid.ShowCount;

        Press(h, PhysicalKey.Q);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal(string.Empty, h.Grid.State!.Typed);
    }

    [Fact]
    public void CharacterNoRemainingCellAccepts_IsIgnored()
    {
        var h = OpenGrid();
        Type(h, "A");
        int shows = h.Grid.ShowCount;

        // Block A is nine columns wide, so the tenth key has no column there.
        Press(h, PhysicalKey.Semicolon);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal("A", h.Grid.State!.Typed);
    }

    [Fact]
    public void BackspaceTakesACharacterBack()
    {
        var h = OpenGrid();
        Type(h, "AS");

        Press(h, PhysicalKey.Backspace);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal("A", state.Typed);
        Assert.Equal(0, state.Highlight);
        Assert.Equal(new Point(34, 30), state.Target);

        Press(h, PhysicalKey.Backspace);

        var cleared = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(string.Empty, cleared.Typed);
        Assert.Equal(-1, cleared.Highlight);
        Assert.Equal(h.Pointer, cleared.Target);
    }

    [Fact]
    public void SpaceClicksTheHighlightedCell()
    {
        var h = OpenGrid();
        Type(h, "AS");

        Assert.True(h.Down(PhysicalKey.Space));

        Assert.False(h.Grid.Visible);
        Assert.Equal(["move:102,30", "left-click"], h.Mouse.Calls);
    }

    [Fact]
    public void TabWhileTyping_MovesWithoutClicking()
    {
        var h = OpenGrid();
        Type(h, "SG");

        Press(h, PhysicalKey.Tab);

        Assert.False(h.Grid.Visible);
        Assert.Equal(["move:925,30"], h.Mouse.Calls);
    }

    [Fact]
    public void SpaceWithShift_RightClicks()
    {
        var h = OpenGrid();

        Assert.False(h.Down(PhysicalKey.LeftShift));
        Assert.True(h.Down(PhysicalKey.Space));

        Assert.False(h.Grid.Visible);
        Assert.Contains("right-click", h.Mouse.Calls);
    }

    [Fact]
    public void ShiftOnTheLastCharacter_StillOnlyMoves()
    {
        var h = OpenGrid();
        Type(h, "SG");
        Assert.False(h.Down(PhysicalKey.LeftShift));

        Press(h, PhysicalKey.K);

        Assert.False(h.Grid.Visible);
        Assert.Equal(["move:925,450"], h.Mouse.Calls);
    }

    [Fact]
    public void Enter_RightClicksTheTarget()
    {
        var h = OpenGrid();

        Assert.True(h.Down(PhysicalKey.Enter));

        Assert.False(h.Grid.Visible);
        Assert.Contains("right-click", h.Mouse.Calls);
    }

    [Fact]
    public void Escape_CancelsAndRestoresThePointer()
    {
        var h = OpenGrid();
        Type(h, "SF");

        Assert.True(h.Down(PhysicalKey.Escape));

        Assert.False(h.Grid.Visible);
        Assert.Equal("move:100,100", h.Mouse.Calls[^1]);
        Assert.Equal(new Point(100, 100), h.Pointer);
        Assert.DoesNotContain("left-click", h.Mouse.Calls);
    }

    [Fact]
    public void CtrlGridKey_ClosesKeepingThePosition()
    {
        var h = OpenGrid();
        Type(h, "SF");

        Assert.False(h.Down(PhysicalKey.LeftCtrl));
        Assert.True(h.Down(PhysicalKey.G));

        Assert.False(h.Grid.Visible);
        Assert.Empty(h.Mouse.Calls);

        h.Up(PhysicalKey.G);
        h.Up(PhysicalKey.LeftCtrl);
    }

    [Fact]
    public void TabBeforeTypingLeavesTheGridOpenAndDoesNotSwitchMonitor()
    {
        var h = OpenGrid(FakeMonitorService.Dual());

        Press(h, PhysicalKey.Tab);

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), state.Monitor.Bounds);
        Assert.Equal(string.Empty, state.Typed);
        Assert.Equal(-1, state.Highlight);
        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void MonitorKeyMovesToTheNextMonitor()
    {
        var h = OpenGrid(FakeMonitorService.Dual());

        Press(h, PhysicalKey.N);

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(new Rectangle(1920, 0, 1280, 1024), state.Monitor.Bounds);
        Assert.Equal(string.Empty, state.Typed);
        Assert.Equal(-1, state.Highlight);
        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void MonitorKeyIsIgnoredWhileItIsAlsoAGridCharacter()
    {
        var h = new RouterHarness(monitors: FakeMonitorService.Dual());
        h.KeySettings.NextMonitor = "S"; // one of the ten cell keys
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);

        Press(h, PhysicalKey.S); // the configured monitor key types the cell instead
        Press(h, PhysicalKey.N); // no monitor key left, and N is not a cell key

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), state.Monitor.Bounds);
        Assert.Equal("S", state.Typed);
    }

    [Fact]
    public void ConfiguredSize_ChangesTheGridAndItsCodes()
    {
        var h = new RouterHarness();
        h.GridSettings.Columns = 10;
        h.GridSettings.Rows = 10;
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();
        Press(h, PhysicalKey.G);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(10, state.Columns);
        Assert.Equal(10, state.Rows);
        Assert.Equal(100, state.Labels.Count);
        // Ten keys cover 10 x 10 cells with a single block.
        Assert.Equal(1, state.BlocksAcross * state.BlocksDown);
        Assert.Equal("AAS", state.Labels[10]);

        Type(h, "AAS");

        Assert.False(h.Grid.Visible);
        Assert.Equal(["move:96,162"], h.Mouse.Calls);

        Press(h, PhysicalKey.Space);

        Assert.Contains("left-down", h.Mouse.Calls);
    }

    [Fact]
    public void UnboundKeyMakesItsCellsUnreachable()
    {
        var h = new RouterHarness();
        h.GridSettings.Keys[0] = "";
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
    public void HeldKeyRepeat_DoesNotTypeTwice()
    {
        var h = OpenGrid();

        Assert.True(h.Down(PhysicalKey.A, repeat: true));
        Assert.True(h.Down(PhysicalKey.S, repeat: true));

        Assert.Equal(string.Empty, h.Grid.State!.Typed);
    }
}
