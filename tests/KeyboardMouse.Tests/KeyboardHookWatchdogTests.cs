using KeyboardMouse.Input;
using Xunit;

namespace KeyboardMouse.Tests;

public class KeyboardHookWatchdogTests
{
    [Fact]
    public void No_raw_key_yet_means_alive()
    {
        Assert.False(KeyboardHook.HookLooksDead(rawMissed: 0, lastRawTicks: 0, nowTicks: 60_000));
    }

    [Fact]
    public void Live_hook_keeps_the_counter_low()
    {
        // The hook clears the counter on every key, so it never passes 1.
        Assert.False(KeyboardHook.HookLooksDead(rawMissed: 1, lastRawTicks: 10_000, nowTicks: 15_000));
        Assert.False(KeyboardHook.HookLooksDead(rawMissed: 2, lastRawTicks: 10_000, nowTicks: 15_000));
    }

    [Fact]
    public void Three_missed_keys_mean_dead()
    {
        Assert.True(KeyboardHook.HookLooksDead(rawMissed: 3, lastRawTicks: 20_000, nowTicks: 20_500));
    }

    [Fact]
    public void Very_fresh_key_waits_for_the_hook()
    {
        Assert.False(KeyboardHook.HookLooksDead(rawMissed: 5, lastRawTicks: 20_000, nowTicks: 20_100));
    }
}
