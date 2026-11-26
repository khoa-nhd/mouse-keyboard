using KeyboardMouse.Input;
using Xunit;

namespace KeyboardMouse.Tests;

public class KeyboardHookWatchdogTests
{
    [Fact]
    public void No_raw_key_yet_means_alive()
    {
        Assert.False(KeyboardHook.HookLooksDead(lastRawTicks: 0, lastCallbackTicks: 1_000, nowTicks: 60_000));
    }

    [Fact]
    public void Hook_that_saw_the_key_is_alive()
    {
        // Callback first, raw input a few ms later: the normal order.
        Assert.False(KeyboardHook.HookLooksDead(lastRawTicks: 10_005, lastCallbackTicks: 10_000, nowTicks: 15_000));
    }

    [Fact]
    public void Idle_machine_is_alive()
    {
        // Nobody typed for a minute: nothing to compare, nothing is sent.
        Assert.False(KeyboardHook.HookLooksDead(lastRawTicks: 10_000, lastCallbackTicks: 10_000, nowTicks: 70_000));
    }

    [Fact]
    public void Key_the_hook_missed_means_dead()
    {
        Assert.True(KeyboardHook.HookLooksDead(lastRawTicks: 20_000, lastCallbackTicks: 10_000, nowTicks: 20_500));
    }

    [Fact]
    public void Very_fresh_key_waits_for_the_hook()
    {
        Assert.False(KeyboardHook.HookLooksDead(lastRawTicks: 20_000, lastCallbackTicks: 10_000, nowTicks: 20_100));
    }
}
