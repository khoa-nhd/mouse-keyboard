using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

public class KeymapRuntimeTests
{
    private static void OpenHint(RouterHarness h)
    {
        h.TapCaps();
        Assert.True(h.Down(PhysicalKey.H));
        Assert.True(h.Up(PhysicalKey.H));
    }

    private static ClickableTarget Target(int x, int y) =>
        new(new Rectangle(x - 10, y - 10, 20, 20));

    [Fact]
    public void UnboundNormalAction_DoesNotFire()
    {
        var h = new RouterHarness();
        h.KeySettings.Up = "";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.I));
        Assert.DoesNotContain(h.Movement.Calls, c => c.Direction == Direction.Up);
    }

    [Fact]
    public void UnboundToggle_DisablesMouseModeToggle()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.None);

        Assert.False(h.Down(PhysicalKey.CapsLock));
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void RemappedGridKey_OpensAndCtrlCloses()
    {
        var h = new RouterHarness();
        h.KeySettings.Grid = "Q";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.Q));
        Assert.True(h.Grid.Visible);
        Assert.True(h.Up(PhysicalKey.Q));

        Assert.False(h.Down(PhysicalKey.LeftCtrl));
        Assert.True(h.Down(PhysicalKey.Q));
        Assert.False(h.Grid.Visible);

        h.Up(PhysicalKey.Q);
        h.Up(PhysicalKey.LeftCtrl);
    }

    [Fact]
    public void GridSwitchesMonitorOnlyWithTheNextMonitorKey()
    {
        var h = new RouterHarness(monitors: FakeMonitorService.Dual());
        h.KeySettings.NextMonitor = "N";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), h.Grid.State!.Monitor.Bounds);

        // Tab is the no-click move now, so it must not change monitor.
        h.Down(PhysicalKey.Tab);
        h.Up(PhysicalKey.Tab);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), h.Grid.State!.Monitor.Bounds);

        h.Down(PhysicalKey.N);
        h.Up(PhysicalKey.N);
        Assert.Equal(new Rectangle(1920, 0, 1280, 1024), h.Grid.State!.Monitor.Bounds);
    }

    [Fact]
    public void GridClickKeysAreFixedToSpaceAndEnter()
    {
        var h = new RouterHarness();
        h.KeySettings.LeftClick = "Q";
        h.KeySettings.RightClick = "W";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);
        Assert.True(h.Down(PhysicalKey.Space));
        Assert.Equal(1, h.Mouse.Count("left-click"));
        Assert.False(h.Grid.Visible);

        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);
        Assert.True(h.Down(PhysicalKey.Enter));
        Assert.Equal(1, h.Mouse.Count("right-click"));
        Assert.False(h.Grid.Visible);
    }

    [Fact]
    public void EmptyGridCell_IsUnselectable()
    {
        var h = new RouterHarness();
        h.GridSettings.Keys[0] = "";
        h.Router.UpdateGrid(h.GridSettings);
        h.TapCaps();

        h.Down(PhysicalKey.G);
        h.Up(PhysicalKey.G);
        int showBefore = h.Grid.ShowCount;
        int movesBefore = h.Mouse.Calls.Count;

        Assert.True(h.Down(PhysicalKey.A));
        Assert.True(h.Up(PhysicalKey.A));
        Assert.Equal(showBefore, h.Grid.ShowCount);
        Assert.Equal(movesBefore, h.Mouse.Calls.Count);
    }

    [Fact]
    public void RemappedHintRightClick_Works()
    {
        var h = new RouterHarness();
        h.KeySettings.RightClick = "R";
        h.Router.UpdateKeys(h.KeySettings);
        OpenHint(h);

        h.HintScanner.Complete(Target(100, 100), Target(300, 100));

        h.Down(PhysicalKey.R);
        Assert.True(h.Down(PhysicalKey.A));
        Assert.True(h.Up(PhysicalKey.A));
        Assert.True(h.Up(PhysicalKey.R));

        Assert.Contains("right-click", h.Mouse.Calls);
    }

    [Fact]
    public void TextInputBypass_LetsKeysThrough()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Router.SetTextInput(true);

        Assert.False(h.Down(PhysicalKey.J));
        Assert.DoesNotContain(h.Movement.Calls, c => c.Direction == Direction.Left);
    }
}