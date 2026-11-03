using KeyboardMouse.Config;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class KeyBindingCatalogTests
{
    [Fact]
    public void Catalog_HasThirtySevenEditableBindings()
    {
        Assert.Equal(37, KeyBindingCatalog.All.Count);
        Assert.Equal(28, KeyBindingCatalog.NormalPool.Count());
        Assert.Equal(9, KeyBindingCatalog.GridPool.Count());
        Assert.Equal(KeyBindingCatalog.All.Count, KeyBindingCatalog.All.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void Normalize_PreservesEmptyValues()
    {
        var settings = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "" },
            Keys = new KeysSettings { Up = "" },
            Grid = new GridSettings { Keys = ["", "I", "O", "J", "K", "L", "M", "Comma", "Period"] }
        };

        settings.Normalize();

        Assert.Equal("", settings.Activation.ToggleKey);
        Assert.Equal("", settings.Keys.Up);
        Assert.Equal("", settings.Grid.Keys[0]);
    }

    [Fact]
    public void Normalize_CanonicalizesAndRepairsValues()
    {
        var settings = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "capslock" },
            Keys = new KeysSettings { Up = "LeftCtrl", Down = "NoSuchKey" },
            Grid = new GridSettings { Keys = ["u", "i", "o", "j", "k", "l", "m", "comma", "period"] }
        };

        settings.Normalize();

        Assert.Equal("CapsLock", settings.Activation.ToggleKey);
        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("K", settings.Keys.Down);
        Assert.Equal("U", settings.Grid.Keys[0]);
    }

    [Fact]
    public void Normalize_RemovesDuplicates()
    {
        var settings = new Settings
        {
            Keys = new KeysSettings { Up = "I", Left = "I", Down = "I" }
        };

        settings.Normalize();

        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("J", settings.Keys.Left);
        Assert.Equal("K", settings.Keys.Down);
    }

    [Fact]
    public void TryAssign_SwapsWithinNormalPool()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.True(KeyBindingCatalog.TryAssign(settings, "up", "J", out var swapped));
        Assert.Equal("J", settings.Keys.Up);
        Assert.Equal("I", settings.Keys.Left);
        Assert.Equal("left", swapped);
    }

    [Fact]
    public void TryAssign_GridPoolIsIndependent()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.True(KeyBindingCatalog.TryAssign(settings, "grid0", "I", out var swapped));
        Assert.Equal("I", settings.Grid.Keys[0]);
        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("grid1", swapped);
    }

    [Fact]
    public void TryAssign_RejectsEscapeAndModifiers()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.False(KeyBindingCatalog.TryAssign(settings, "up", "Escape", out _));
        Assert.False(KeyBindingCatalog.TryAssign(settings, "up", "LeftCtrl", out _));
        Assert.False(KeyBindingCatalog.TryAssign(settings, "up", "None", out _));
    }

    [Fact]
    public void Unbind_And_Reset_Work()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.True(KeyBindingCatalog.Unbind(settings, "up"));
        Assert.Equal("", settings.Keys.Up);

        Assert.True(KeyBindingCatalog.Reset(settings, "up", out _));
        Assert.Equal("I", settings.Keys.Up);
    }

    [Fact]
    public void ResetAll_RestoresDefaults()
    {
        var settings = new Settings();
        settings.Normalize();
        settings.Keys.Up = "Q";
        settings.Grid.Keys[0] = "Q";

        KeyBindingCatalog.ResetAll(settings);

        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("U", settings.Grid.Keys[0]);
    }

    [Fact]
    public void HintAlphabet_NormalizesSupportedCharacters()
    {
        Assert.Equal("asdf12;", HintLabels.Normalize("A A S D F 1 2 ;"));
    }

    [Fact]
    public void HintLabels_SupportsDigits()
    {
        var labels = HintLabels.Generate(4, "ab1");
        Assert.All(labels, label => Assert.True(label.All(c => char.IsLetterOrDigit(c))));
    }
}