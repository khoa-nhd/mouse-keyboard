using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class LayerTests
{
    [Fact]
    public void HoldA_UsesTurboProfile()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.A));
        Assert.Same(h.Profiles.Turbo, h.Movement.MoveProfile);

        Assert.True(h.Up(PhysicalKey.A));
        Assert.Same(h.Profiles.Normal, h.Movement.MoveProfile);
    }

    [Fact]
    public void HoldS_UsesPrecisionProfile()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.S));
        Assert.Same(h.Profiles.Precision, h.Movement.MoveProfile);

        Assert.True(h.Up(PhysicalKey.S));
        Assert.Same(h.Profiles.Normal, h.Movement.MoveProfile);
    }

    [Fact]
    public void PrecisionWinsOverTurbo()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.A);
        h.Down(PhysicalKey.S);
        Assert.Same(h.Profiles.Precision, h.Movement.MoveProfile);

        h.Up(PhysicalKey.S);
        Assert.Same(h.Profiles.Turbo, h.Movement.MoveProfile);

        h.Up(PhysicalKey.A);
        Assert.Same(h.Profiles.Normal, h.Movement.MoveProfile);
    }

    [Fact]
    public void RepeatedLayerDown_DoesNotToggle()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.A);
        h.Down(PhysicalKey.A, repeat: true);

        Assert.True(h.State.Turbo);
        Assert.Same(h.Profiles.Turbo, h.Movement.MoveProfile);

        h.Up(PhysicalKey.A);
        Assert.False(h.State.Turbo);
    }

    [Fact]
    public void HoldD_SwitchesEngineToScroll()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.D));
        Assert.True(h.State.Scroll);
        Assert.Equal(MovementMode.Scroll, h.Movement.Mode);
        Assert.False(h.Movement.ScrollPrecision);

        h.Down(PhysicalKey.I);
        Assert.Contains(Direction.Up, h.Movement.Active);

        h.Up(PhysicalKey.I);
        h.Up(PhysicalKey.D);

        Assert.False(h.State.Scroll);
        Assert.Equal(MovementMode.Move, h.Movement.Mode);
        Assert.Same(h.Profiles.Normal, h.Movement.MoveProfile);
    }

    [Fact]
    public void ScrollPlusS_EnablesPreciseScroll()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.D);
        Assert.False(h.Movement.ScrollPrecision);

        h.Down(PhysicalKey.S);
        Assert.True(h.Movement.ScrollPrecision);
        Assert.Equal(MovementMode.Scroll, h.Movement.Mode);

        h.Up(PhysicalKey.S);
        Assert.False(h.Movement.ScrollPrecision);

        h.Up(PhysicalKey.D);
    }

    [Fact]
    public void ScrollLayer_UAndOSendPageUpAndPageDown()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.D);
        h.Down(PhysicalKey.U);
        h.Down(PhysicalKey.O);

        Assert.Equal([PhysicalKey.PageUp, PhysicalKey.PageDown], h.Keys.Keys);
        Assert.Equal(0, h.Mouse.Count("x1-click"));
        Assert.Equal(0, h.Mouse.Count("x2-click"));
    }

    [Fact]
    public void NormalLayer_UAndOStillClickBackAndForward()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.U);
        h.Down(PhysicalKey.O);

        Assert.Equal(1, h.Mouse.Count("x1-click"));
        Assert.Equal(1, h.Mouse.Count("x2-click"));
        Assert.Empty(h.Keys.Keys);
    }

    [Fact]
    public void Escape_ClearsHeldLayers()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.D);
        h.Down(PhysicalKey.S);
        h.Down(PhysicalKey.A);
        Assert.True(h.State.Scroll);

        h.Down(PhysicalKey.Escape);
        Assert.False(h.State.MouseModeOn);
        Assert.False(h.State.Scroll);
        Assert.False(h.State.Precision);
        Assert.False(h.State.Turbo);
        Assert.Equal(MovementMode.Move, h.Movement.Mode);
        Assert.Same(h.Profiles.Normal, h.Movement.MoveProfile);

        // Their matching key-ups stay swallowed because the downs were.
        Assert.True(h.Up(PhysicalKey.D));
        Assert.True(h.Up(PhysicalKey.S));
        Assert.True(h.Up(PhysicalKey.A));
    }

    [Fact]
    public void UpdateProfiles_ReappliesActiveLayer()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.A);

        var replacement = new SpeedProfiles(
            new SpeedProfile(100, 500, 1000, 2),
            new SpeedProfile(10, 10, 0, 1),
            new SpeedProfile(900, 3000, 4000, 20),
            new SpeedProfile(200, 1000, 1500, 0),
            SmoothScroll: false);

        h.Router.UpdateProfiles(replacement);

        Assert.Same(replacement.Turbo, h.Movement.MoveProfile);
        Assert.Same(replacement.Scroll, h.Movement.ScrollProfile);
        Assert.False(h.Movement.SmoothScroll);
    }
}
