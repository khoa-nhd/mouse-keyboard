using System.Drawing;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

/// <summary>
/// Grid mode is one flat grid whose cells each show a four-character code from
/// the moment it opens; typing the four characters clicks that cell.
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
    public void G_OpensTheWholeGridWithAFourCharacterCodeInEveryCell()
    {
        var h = OpenGrid();

        Assert.True(h.Grid.Visible);
        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal(28, state.Columns);
        Assert.Equal(18, state.Rows);
        Assert.Equal(28 * 18, state.Labels.Count);
        Assert.All(state.Labels, label => Assert.Equal(4, label.Length));

        // Every code is readable before typing: column band, column, row band, row.
        Assert.Equal("AAAA", state.Labels[0]);
        Assert.Equal("ASAA", state.Labels[1]);
        Assert.Equal("AGAA", state.Labels[4]);
        Assert.Equal("AAAS", state.Labels[28]);
        Assert.Equal("AGAS", state.Labels[(1 * 28) + 4]);

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

        Press(h, PhysicalKey.A);

        var afterRowBand = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal("ASA", afterRowBand.Typed);
        Assert.Equal(new Point(102, 30), afterRowBand.Target);

        Assert.Empty(h.Mouse.Calls);
    }

    [Fact]
    public void TypingAllFourCharactersOnlyMovesThePointer()
    {
        var h = OpenGrid();

        Type(h, "SFAK");

        Assert.True(h.Grid.Visible);
        Assert.Equal(["move:925,450"], h.Mouse.Calls);
    }

    [Fact]
    public void SpaceAfterACompleteCodeClicksWhereThePointerStands()
    {
        var h = OpenGrid();
        Type(h, "SFAK");

        Assert.True(h.Down(PhysicalKey.Space));

        Assert.False(h.Grid.Visible);
        Assert.Equal(["move:925,450", "move:925,450", "left-click"], h.Mouse.Calls);
    }

    [Fact]
    public void ShiftSpaceAfterACompleteCodeRightClicks()
    {
        var h = OpenGrid();
        Type(h, "SFAK");
        Assert.False(h.Down(PhysicalKey.LeftShift));

        Assert.True(h.Down(PhysicalKey.Space));

        Assert.False(h.Grid.Visible);
        Assert.Equal("right-click", h.Mouse.Calls[^1]);
        Assert.DoesNotContain("left-click", h.Mouse.Calls);
    }

    [Fact]
    public void EveryCell_IsReachedByItsOwnCodeAndClickedWithSpace()
    {
        foreach (var (code, expected) in new[]
        {
            ("AAAA", new Point(34, 30)),
            ("ASAA", new Point(102, 30)),
            ("SFAK", new Point(925, 450)),
            ("DFSH", new Point(1611, 930))
        })
        {
            var h = OpenGrid();
            Type(h, code);

            Assert.Equal($"move:{expected.X},{expected.Y}", h.Mouse.Calls[^1]);
            Assert.DoesNotContain("left-click", h.Mouse.Calls);

            Assert.True(h.Down(PhysicalKey.Space));

            Assert.False(h.Grid.Visible);
            Assert.Equal("left-click", h.Mouse.Calls[^1]);
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
        Type(h, "AS");
        int shows = h.Grid.ShowCount;

        // After "AS" only the two row bands A and S are still possible.
        Press(h, PhysicalKey.D);

        Assert.Equal(shows, h.Grid.ShowCount);
        Assert.Equal("AS", h.Grid.State!.Typed);
    }

    [Fact]
    public void BackspaceTakesACharacterBack()
    {
        var h = OpenGrid();
        Type(h, "ASA");

        Press(h, PhysicalKey.Backspace);

        var state = Assert.IsType<GridOverlayState>(h.Grid.State);
        Assert.Equal("AS", state.Typed);
        Assert.Equal(1, state.Highlight);
        Assert.Equal(new Point(102, 30), state.Target);

        Press(h, PhysicalKey.Backspace);
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
        Type(h, "SFA");

        Press(h, PhysicalKey.Tab);

        Assert.True(h.Grid.Visible);
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
        Type(h, "SFA");
        Assert.False(h.Down(PhysicalKey.LeftShift));

        Press(h, PhysicalKey.K);

        Assert.True(h.Grid.Visible);
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
        Assert.Equal("AAAS", state.Labels[10]);

        Type(h, "AAAS");

        Assert.Equal(["move:96,162"], h.Mouse.Calls);

        Assert.True(h.Down(PhysicalKey.Space));

        Assert.False(h.Grid.Visible);
        Assert.Equal("left-click", h.Mouse.Calls[^1]);
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
