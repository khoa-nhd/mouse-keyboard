using System.Drawing;
using KeyboardMouse.Controllers;
using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class JumpTests
{
    [Fact]
    public void X_JumpsToCurrentMonitorWorkAreaCenter()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.X));
        Assert.True(h.Up(PhysicalKey.X));

        Assert.Equal("move:960,520", h.Mouse.Calls[^1]);
        Assert.Equal(new Point(960, 520), h.Pointer);
    }

    [Fact]
    public void C_JumpsToForegroundWindowCenter()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Monitors.ForegroundBounds = new Rectangle(200, 100, 401, 301);

        Assert.True(h.Down(PhysicalKey.C));
        Assert.True(h.Up(PhysicalKey.C));

        Assert.Equal("move:400,250", h.Mouse.Calls[^1]);
    }

    [Fact]
    public void C_WithoutForegroundWindow_DoesNothing()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Monitors.ForegroundBounds = null;
        int moves = h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal));

        Assert.True(h.Down(PhysicalKey.C));

        Assert.Equal(moves, h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal)));
    }

    [Fact]
    public void N_JumpsToNextMonitorWorkAreaCenter_AndWraps()
    {
        var h = new RouterHarness(monitors: FakeMonitorService.Dual());
        h.TapCaps();
        h.Pointer = new Point(100, 100);

        Assert.True(h.Down(PhysicalKey.N));
        Assert.Equal("move:2560,492", h.Mouse.Calls[^1]);

        Assert.True(h.Up(PhysicalKey.N));
        Assert.True(h.Down(PhysicalKey.N));
        Assert.Equal("move:960,520", h.Mouse.Calls[^1]);
    }

    [Fact]
    public void Jump_ClearsHeldMovementDirections()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.I);
        Assert.Contains(Direction.Up, h.Movement.Active);

        h.Down(PhysicalKey.X);

        Assert.Empty(h.Movement.Active);
    }
}

public class MarkTests
{
    [Fact]
    public void MThenLetter_SavesTheCurrentPointer()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Pointer = new Point(321, 123);

        Assert.True(h.Down(PhysicalKey.M));
        Assert.True(h.Up(PhysicalKey.M));
        Assert.True(h.Router.MarkPending);

        Assert.True(h.Down(PhysicalKey.B));
        Assert.True(h.Up(PhysicalKey.B));

        Assert.False(h.Router.MarkPending);
        Assert.True(h.Marks.TryGet('b', out var saved));
        Assert.Equal(new Point(321, 123), saved);
    }

    [Fact]
    public void ApostropheThenLetter_JumpsToTheSavedMark()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Marks.Set('q', new Point(800, 600));
        h.Pointer = new Point(10, 10);

        Assert.True(h.Down(PhysicalKey.Apostrophe));
        Assert.True(h.Up(PhysicalKey.Apostrophe));
        Assert.True(h.Router.MarkPending);

        Assert.True(h.Down(PhysicalKey.Q));
        Assert.True(h.Up(PhysicalKey.Q));

        Assert.Equal("move:800,600", h.Mouse.Calls[^1]);
        Assert.False(h.Router.MarkPending);
    }

    [Fact]
    public void JumpingToAMissingMark_DoesNothing()
    {
        var h = new RouterHarness();
        h.TapCaps();
        int moves = h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal));

        h.Down(PhysicalKey.Apostrophe);
        h.Up(PhysicalKey.Apostrophe);
        Assert.True(h.Down(PhysicalKey.Z));

        Assert.Equal(moves, h.Mouse.Calls.Count(c => c.StartsWith("move:", StringComparison.Ordinal)));
        Assert.True(h.Up(PhysicalKey.Z));
    }

    [Fact]
    public void PendingState_ExpiresAfterTwoSeconds()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.M);
        h.Up(PhysicalKey.M);

        h.Now += PendingController.DefaultTimeoutMs - 1;
        h.Tick();
        Assert.True(h.Router.MarkPending);

        h.Now += 1;
        h.Tick();
        Assert.False(h.Router.MarkPending);
    }

    [Fact]
    public void AnotherKey_CancelsPendingAndStillReachesTheNormalLayer()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.M);
        h.Up(PhysicalKey.M);

        Assert.True(h.Down(PhysicalKey.Space));

        Assert.False(h.Router.MarkPending);
        Assert.Equal(1, h.Mouse.Count("left-down"));
        Assert.True(h.Up(PhysicalKey.Space));
    }

    [Fact]
    public void Escape_CancelsPendingAndLeavesMouseMode()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.M);
        h.Up(PhysicalKey.M);

        Assert.True(h.Down(PhysicalKey.Escape));

        Assert.False(h.Router.MarkPending);
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void TurningModeOff_CancelsPending()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.M);
        h.Up(PhysicalKey.M);

        h.Router.SetMode(false);

        Assert.False(h.Router.MarkPending);
    }

    [Fact]
    public void RemappedMarkKeys_AreUsed()
    {
        var h = new RouterHarness();
        h.KeySettings.MarkSave = "Y";
        h.KeySettings.MarkJump = "P";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();
        h.Pointer = new Point(44, 55);

        Assert.False(h.Down(PhysicalKey.M));
        Assert.False(h.Router.MarkPending); // M is no longer a mark key
        h.Up(PhysicalKey.M);

        Assert.True(h.Down(PhysicalKey.Y));
        Assert.True(h.Router.MarkPending);
        Assert.True(h.Down(PhysicalKey.C));

        Assert.True(h.Marks.TryGet('c', out var saved));
        Assert.Equal(new Point(44, 55), saved);
    }
}
