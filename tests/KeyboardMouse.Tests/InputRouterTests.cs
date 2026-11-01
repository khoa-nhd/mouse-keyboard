using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class InputRouterTests
{
    [Fact]
    public void TapCaps_TogglesMouseModeOnAndOff()
    {
        var h = new RouterHarness();
        Assert.False(h.State.MouseModeOn);

        h.TapCaps();
        Assert.True(h.State.MouseModeOn);

        h.Now += 1000;
        h.TapCaps();
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void CapsHold_IsMomentary()
    {
        var h = new RouterHarness();
        h.Now = 0;

        Assert.True(h.Down(PhysicalKey.CapsLock));
        h.Now = 250;
        h.Tick();
        Assert.True(h.State.MouseModeOn);

        Assert.True(h.Down(PhysicalKey.J));
        Assert.Contains(Direction.Left, h.Movement.Active);

        Assert.True(h.Up(PhysicalKey.J));
        Assert.True(h.Up(PhysicalKey.CapsLock));
        Assert.False(h.State.MouseModeOn);
        Assert.Empty(h.Movement.Active);
    }

    [Fact]
    public void CapsHold_DoesNotTurnOffAnAlreadyToggledMode()
    {
        var h = new RouterHarness();
        h.TapCaps();
        Assert.True(h.State.MouseModeOn);

        h.Now += 1000;
        Assert.True(h.Down(PhysicalKey.CapsLock));
        h.Now += 250;
        h.Tick();
        Assert.True(h.Up(PhysicalKey.CapsLock));

        Assert.True(h.State.MouseModeOn);
    }

    [Fact]
    public void AnotherKeyWhileCapsHeld_StartsMomentaryModeImmediately()
    {
        var h = new RouterHarness();
        h.Now = 0;
        Assert.True(h.Down(PhysicalKey.CapsLock));

        h.Now = 50;
        Assert.True(h.Down(PhysicalKey.J));
        Assert.True(h.State.MouseModeOn);
        Assert.Contains(Direction.Left, h.Movement.Active);

        Assert.True(h.Up(PhysicalKey.J));
        Assert.True(h.Up(PhysicalKey.CapsLock));
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void ShiftCaps_PassesThroughAsRealCapsLock()
    {
        var h = new RouterHarness();

        Assert.False(h.Down(PhysicalKey.LeftShift));
        Assert.False(h.Down(PhysicalKey.CapsLock));
        Assert.False(h.Up(PhysicalKey.CapsLock));
        Assert.False(h.Up(PhysicalKey.LeftShift));

        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void SpaceDownUp_IsLeftButtonDownUp()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.Space));
        Assert.True(h.Up(PhysicalKey.Space));

        Assert.Equal(1, h.Mouse.Count("left-down"));
        Assert.Equal(1, h.Mouse.Count("left-up"));
    }

    [Fact]
    public void RepeatedSpaceDown_DoesNotPressTwice()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.Space));
        Assert.True(h.Down(PhysicalKey.Space, repeat: true));

        Assert.Equal(1, h.Mouse.Count("left-down"));
    }

    [Fact]
    public void RightButton_HoldsAndReleases()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.F));
        Assert.True(h.Up(PhysicalKey.F));

        Assert.Equal(1, h.Mouse.Count("right-down"));
        Assert.Equal(1, h.Mouse.Count("right-up"));
    }

    [Fact]
    public void DragLock_KeepsLeftButtonDownUntilSpace()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.V));
        Assert.True(h.Up(PhysicalKey.V));
        Assert.True(h.State.DragLock);
        Assert.Equal(1, h.Mouse.Count("left-down"));

        Assert.True(h.Down(PhysicalKey.Space));
        Assert.False(h.State.DragLock);
        Assert.Equal(1, h.Mouse.Count("left-up"));

        Assert.True(h.Up(PhysicalKey.Space));
    }

    [Fact]
    public void ClickAndExit_LeavesMouseMode()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.T));
        Assert.False(h.State.MouseModeOn);
        Assert.Equal(1, h.Mouse.Count("left-click"));

        Assert.True(h.Up(PhysicalKey.T));
    }

    [Fact]
    public void Escape_LeavesMouseModeAndSwallowsBothEvents()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.Escape));
        Assert.False(h.State.MouseModeOn);
        Assert.True(h.Up(PhysicalKey.Escape));
    }

    [Fact]
    public void KeyUp_StaysSwallowedAfterModeTurnsOff()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.J));
        Assert.True(h.Down(PhysicalKey.Escape));
        Assert.False(h.State.MouseModeOn);

        Assert.True(h.Up(PhysicalKey.J));
    }

    [Fact]
    public void KeyHeldBeforeModeStarts_PassesItsUpThrough()
    {
        var h = new RouterHarness();

        Assert.False(h.Down(PhysicalKey.J)); // mode off: app sees the down
        h.TapCaps();
        Assert.True(h.State.MouseModeOn);

        Assert.False(h.Up(PhysicalKey.J)); // app must see the matching up
    }

    [Fact]
    public void UnmappedKey_PassesThrough()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.Q));
        Assert.False(h.Up(PhysicalKey.Q));
    }

    [Fact]
    public void ModifierKeys_PassThrough_WhileClickIsSwallowed()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.LeftCtrl));
        Assert.True(h.Down(PhysicalKey.Space));
        Assert.True(h.Up(PhysicalKey.Space));
        Assert.False(h.Up(PhysicalKey.LeftCtrl));

        Assert.Equal(1, h.Mouse.Count("left-down"));
    }

    [Fact]
    public void Panic_ReleasesEverythingAndLeavesMode()
    {
        var h = new RouterHarness();
        h.TapCaps();
        Assert.True(h.Down(PhysicalKey.Space));
        Assert.False(h.Down(PhysicalKey.LeftCtrl));
        Assert.False(h.Down(PhysicalKey.LeftAlt));

        h.Now += 10;
        Assert.True(h.Down(PhysicalKey.Backspace));

        Assert.False(h.State.MouseModeOn);
        Assert.Contains("release-all", h.Mouse.Calls);
        Assert.True(h.Up(PhysicalKey.Backspace));
    }

    [Fact]
    public void Paused_PassesAllKeysThrough()
    {
        var h = new RouterHarness();
        h.Router.SetPaused(true);

        Assert.False(h.Down(PhysicalKey.CapsLock));
        Assert.False(h.Up(PhysicalKey.CapsLock));
        Assert.False(h.Down(PhysicalKey.J));
        Assert.False(h.Up(PhysicalKey.J));
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void TickWithoutPendingCaps_DoesNothing()
    {
        var h = new RouterHarness();
        h.Now = 5000;
        h.Tick();
        Assert.False(h.State.MouseModeOn);
    }
}
