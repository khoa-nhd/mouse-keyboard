using KeyboardMouse.Input;
using Xunit;

namespace KeyboardMouse.Tests;

public class KeyboardHookWatchdogTests
{
    [Fact]
    public void Idle_machine_is_not_poked_again()
    {
        // The only input since the last poke is the poke itself.
        Assert.False(KeyboardHook.ShouldPoke(lastInputTick: 10_050, lastPokeTick: 10_000));
        Assert.False(KeyboardHook.ShouldPoke(lastInputTick: 9_000, lastPokeTick: 10_000));
    }

    [Fact]
    public void Real_input_after_the_poke_allows_another()
    {
        Assert.True(KeyboardHook.ShouldPoke(lastInputTick: 14_000, lastPokeTick: 10_000));
    }

    [Fact]
    public void Tick_wraparound_is_handled()
    {
        Assert.True(KeyboardHook.ShouldPoke(lastInputTick: 2_000, lastPokeTick: uint.MaxValue - 1_000));
        Assert.False(KeyboardHook.ShouldPoke(lastInputTick: 100, lastPokeTick: uint.MaxValue - 100));
    }
}
