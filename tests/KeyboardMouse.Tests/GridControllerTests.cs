using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class GridControllerTests
{
    [Fact]
    public void G_OpensGrid_AndEscapeRestoresPointer()
    {
        var h = new RouterHarness();
        h.TapCaps();
        var original = h.Pointer;

        Assert.True(h.Down(PhysicalKey.G));
        Assert.True(h.Grid.Visible);
        Assert.True(h.Up(PhysicalKey.G));

        Assert.True(h.Down(PhysicalKey.U));
        Assert.True(h.Up(PhysicalKey.U));
        Assert.True(h.Grid.ShowCount >= 2);

        int movesBeforeEscape = h.Mouse.Calls.Count;
        Assert.True(h.Down(PhysicalKey.Escape));

        Assert.False(h.Grid.Visible);
        Assert.True(h.Mouse.Calls.Count > movesBeforeEscape);
        Assert.Equal($"move:{original.X},{original.Y}", h.Mouse.Calls[^1]);
        Assert.True(h.Up(PhysicalKey.Escape));
    }

    [Fact]
    public void ThreeCellKeys_CloseTheGridAutomatically()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.G);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(h.Down(PhysicalKey.K));
            Assert.True(h.Up(PhysicalKey.K));
        }

        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void RepeatedCellDown_DoesNotSubdivideTwice()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.G);

        h.Down(PhysicalKey.K);
        h.Down(PhysicalKey.K, repeat: true);

        // One selection shows the smaller grid once; the repeat is ignored.
        Assert.Equal(2, h.Grid.ShowCount);
    }

    [Fact]
    public void SpaceClicksAndCloses_RightClickToo()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.G);

        Assert.True(h.Down(PhysicalKey.Space));
        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.False(h.Grid.Visible);

        h.Down(PhysicalKey.G);
        Assert.True(h.Down(PhysicalKey.F));
        Assert.Equal(1, h.Mouse.Count("right-click"));
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void GridKeyG_ClosesWithoutMoving()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.G);
        int moves = h.Mouse.Calls.Count;

        Assert.True(h.Down(PhysicalKey.G));
        Assert.False(h.Grid.Visible);
        Assert.Equal(moves, h.Mouse.Calls.Count);
    }

    [Fact]
    public void Backspace_GoesUpOneLevel()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.G);
        h.Down(PhysicalKey.U);
        h.Down(PhysicalKey.K, repeat: false);

        int showBefore = h.Grid.ShowCount;
        Assert.True(h.Down(PhysicalKey.Backspace));

        Assert.True(h.Grid.Visible);
        Assert.True(h.Grid.ShowCount > showBefore);
    }

    [Fact]
    public void ModeOff_ClosesGridWithoutRestoringPointer()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.G);
        int moves = h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal));

        h.Router.SetMode(false);

        Assert.False(h.Grid.Visible);
        Assert.Equal(moves, h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal)));
    }
}
