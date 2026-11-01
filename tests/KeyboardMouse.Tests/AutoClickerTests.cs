using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

/// <summary>B toggles the auto clicker in mouse mode.</summary>
public class AutoClickerTests
{
    [Fact]
    public void B_StartsClickingAtTheConfiguredInterval_AndBAgainStops()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.B));
        h.Up(PhysicalKey.B);

        Assert.True(h.AutoClickTimer.Running);
        Assert.Equal(100, h.AutoClickTimer.IntervalMs);
        Assert.True(h.State.AutoClicking);

        h.AutoClickTimer.Fire(3);
        Assert.Equal(3, h.Mouse.Count("left-click"));

        h.Down(PhysicalKey.B);
        h.Up(PhysicalKey.B);

        Assert.False(h.AutoClickTimer.Running);
        Assert.False(h.State.AutoClicking);
    }

    [Fact]
    public void Repeat_DoesNotToggleAgain()
    {
        var h = new RouterHarness();
        h.TapCaps();

        h.Down(PhysicalKey.B);
        h.Down(PhysicalKey.B, repeat: true);
        h.Down(PhysicalKey.B, repeat: true);

        Assert.True(h.AutoClickTimer.Running);
        Assert.Equal(1, h.AutoClickTimer.Starts);
    }

    [Fact]
    public void LeavingMouseMode_StopsClicking()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.B);
        h.Up(PhysicalKey.B);

        h.Down(PhysicalKey.Escape);

        Assert.False(h.State.MouseModeOn);
        Assert.False(h.AutoClickTimer.Running);
        Assert.False(h.State.AutoClicking);
    }

    [Fact]
    public void Panic_StopsClicking()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.B);
        h.Up(PhysicalKey.B);

        h.Router.Panic();

        Assert.False(h.AutoClickTimer.Running);
    }

    [Fact]
    public void LateTickAfterStop_DoesNotClick()
    {
        var state = new AppState();
        var mouse = new FakeMouseOutput();
        Action? captured = null;
        var clicker = new AutoClicker(state, mouse, schedule: (_, tick) =>
        {
            captured = tick;
            return new NoopDisposable();
        });

        clicker.Start();
        clicker.Stop();
        captured!.Invoke();

        Assert.Equal(0, mouse.Count("left-click"));
    }

    [Theory]
    [InlineData("Right", "right-click")]
    [InlineData("middle", "middle-click")]
    [InlineData("nonsense", "left-click")]
    public void Button_FollowsSettings(string button, string expectedCall)
    {
        var h = new RouterHarness();
        h.AutoClickSettings.Button = button;
        h.Router.UpdateAutoClick(h.AutoClickSettings);
        h.TapCaps();

        h.Down(PhysicalKey.B);
        h.AutoClickTimer.Fire();

        Assert.Equal(1, h.Mouse.Count(expectedCall));
    }

    [Fact]
    public void ChangingIntervalWhileRunning_RestartsWithTheNewInterval()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.B);

        h.Router.UpdateAutoClick(new AutoClickSettings { IntervalMs = 250 });

        Assert.True(h.AutoClickTimer.Running);
        Assert.Equal(250, h.AutoClickTimer.IntervalMs);
    }

    [Fact]
    public void StartingWhileDragLocked_ReleasesTheDragFirst()
    {
        var h = new RouterHarness();
        h.TapCaps();
        h.Down(PhysicalKey.V);
        h.Up(PhysicalKey.V);
        Assert.True(h.State.DragLock);

        h.Down(PhysicalKey.B);

        Assert.False(h.State.DragLock);
        Assert.Equal(1, h.Mouse.Count("left-up"));
        Assert.True(h.AutoClickTimer.Running);
    }

    [Fact]
    public void RemappedKey_Works()
    {
        var h = new RouterHarness();
        h.KeySettings.AutoClick = "Q";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.B));
        Assert.True(h.Down(PhysicalKey.Q));
        Assert.True(h.AutoClickTimer.Running);
    }

    [Fact]
    public void AutoOff_DoesNotStopARunningAutoClicker()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(5);
        h.TapCaps();
        h.Down(PhysicalKey.B);
        h.Up(PhysicalKey.B);

        h.Now += 60_000;
        h.Tick();

        Assert.True(h.State.MouseModeOn);
        Assert.True(h.AutoClickTimer.Running);
    }

    [Theory]
    [InlineData(1, AutoClicker.MinIntervalMs)]
    [InlineData(999_999, AutoClicker.MaxIntervalMs)]
    public void Settings_ClampTheInterval(int configured, int expected)
    {
        var settings = new Settings { AutoClick = new AutoClickSettings { IntervalMs = configured, Button = "bogus" } };

        settings.Normalize();

        Assert.Equal(expected, settings.AutoClick.IntervalMs);
        Assert.Equal("Left", settings.AutoClick.Button);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
