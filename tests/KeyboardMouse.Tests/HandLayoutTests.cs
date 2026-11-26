using KeyboardMouse.Config;
using KeyboardMouse.Input;
using Xunit;

namespace KeyboardMouse.Tests;

public class HandLayoutTests
{
    [Fact]
    public void Left_hand_moves_with_wasd()
    {
        var settings = new Settings();
        Assert.True(KeyBindingCatalog.ApplyHand(settings, "left"));
        settings.Normalize();

        Assert.Equal("left", settings.Keys.Hand);
        Assert.Equal("W", settings.Keys.Up);
        Assert.Equal("A", settings.Keys.Left);
        Assert.Equal("S", settings.Keys.Down);
        Assert.Equal("D", settings.Keys.Right);
        Assert.Equal("Space", settings.Keys.LeftClick);
        Assert.Equal(new[] { "Q", "W", "E", "A", "S", "D", "Z", "X", "C" }, settings.Grid.RecursiveKeys);
    }

    [Fact]
    public void Left_hand_layout_has_no_duplicate_keys()
    {
        var settings = new Settings();
        KeyBindingCatalog.ApplyHand(settings, "left");
        var before = KeyBindingCatalog.NormalPool.Select(d => KeyBindingCatalog.Get(settings, d)).ToList();

        settings.Normalize();
        var after = KeyBindingCatalog.NormalPool.Select(d => KeyBindingCatalog.Get(settings, d)).ToList();

        Assert.Equal(before, after); // Normalize found nothing to repair
        Assert.Equal(after.Count, after.Distinct().Count());
    }

    [Fact]
    public void Switching_back_restores_the_right_hand_defaults()
    {
        var settings = new Settings();
        KeyBindingCatalog.ApplyHand(settings, "left");
        KeyBindingCatalog.ApplyHand(settings, "right");
        settings.Normalize();

        var fresh = new Settings();
        fresh.Normalize();
        foreach (var definition in KeyBindingCatalog.All)
        {
            Assert.Equal(KeyBindingCatalog.Get(fresh, definition), KeyBindingCatalog.Get(settings, definition));
        }
    }

    [Fact]
    public void Toggle_and_code_grid_keys_are_kept()
    {
        var settings = new Settings();
        settings.Activation.ToggleKey = "ScrollLock";
        settings.Grid.Keys[0] = "Q";
        KeyBindingCatalog.ApplyHand(settings, "left");

        Assert.Equal("ScrollLock", settings.Activation.ToggleKey);
        Assert.Equal("Q", settings.Grid.Keys[0]);
    }

    [Fact]
    public void Reset_uses_the_current_hand()
    {
        var settings = new Settings();
        KeyBindingCatalog.ApplyHand(settings, "left");
        KeyBindingCatalog.Unbind(settings, "up");

        Assert.True(KeyBindingCatalog.Reset(settings, "up", out _));
        Assert.Equal("W", settings.Keys.Up);
    }

    [Fact]
    public void Unknown_hand_is_rejected_and_normalised()
    {
        var settings = new Settings();
        Assert.False(KeyBindingCatalog.ApplyHand(settings, "both"));

        settings.Keys.Hand = "LEFT ";
        settings.Normalize();
        Assert.Equal("left", settings.Keys.Hand);

        settings.Keys.Hand = "nonsense";
        settings.Normalize();
        Assert.Equal("right", settings.Keys.Hand);
    }

    [Fact]
    public void Every_key_can_be_assigned_to_any_action()
    {
        var settings = new Settings();
        Assert.True(KeyBindingCatalog.TryParseBindingKey("Q", out var key));
        Assert.True(KeyBindingCatalog.TryAssign(settings, "hint", KeyNames.Canonical(key), out var swapped));
        Assert.Null(swapped); // Q was free
        Assert.Equal("Q", settings.Keys.Hint);
    }
}
