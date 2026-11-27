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

    [Fact]
    public void Keys_re_sent_by_a_remapper_are_handled_like_typing()
    {
        // kanata swallows every physical key and re-sends it with SendInput:
        // injected, with no extra info. Those must reach the app.
        Assert.False(KeyboardHook.IsOwnInjection(0x10, UIntPtr.Zero));
        Assert.False(KeyboardHook.IsOwnInjection(0x10, (UIntPtr)0x1234u));
        Assert.False(KeyboardHook.IsOwnInjection(0x00, UIntPtr.Zero));
    }

    [Fact]
    public void Only_our_own_tagged_input_is_ignored()
    {
        Assert.True(KeyboardHook.IsOwnInjection(0x10, (UIntPtr)InjectedInput.Signature));
        // A physical key never carries the injected flag, whatever its extra info.
        Assert.False(KeyboardHook.IsOwnInjection(0x00, (UIntPtr)InjectedInput.Signature));
    }
}
