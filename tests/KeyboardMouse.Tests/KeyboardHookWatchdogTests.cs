using KeyboardMouse.Input;
using Xunit;

namespace KeyboardMouse.Tests;

public class KeyboardHookWatchdogTests
{
    [Fact]
    public void Recent_callback_means_alive()
    {
        // A held key repeats about every 33 ms after a 500 ms delay.
        Assert.False(KeyboardHook.HookLooksDead(silentForMs: 0));
        Assert.False(KeyboardHook.HookLooksDead(silentForMs: 600));
        Assert.False(KeyboardHook.HookLooksDead(silentForMs: 1000));
    }

    [Fact]
    public void Held_key_through_a_second_of_silence_means_dead()
    {
        Assert.True(KeyboardHook.HookLooksDead(silentForMs: 1001));
        Assert.True(KeyboardHook.HookLooksDead(silentForMs: 30_000));
    }
}
