using KeyboardMouse.Config;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class KeyBindingCatalogTests
{
    [Fact]
    public void Catalog_HasThirtyEightEditableBindings()
    {
        Assert.Equal(38, KeyBindingCatalog.All.Count);
        Assert.Equal(28, KeyBindingCatalog.NormalPool.Count());
        Assert.Equal(10, KeyBindingCatalog.GridPool.Count());
        Assert.Equal(KeyBindingCatalog.All.Count, KeyBindingCatalog.All.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void Normalize_PreservesEmptyValues()
    {
        var settings = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "" },
            Keys = new KeysSettings { Up = "" },
            Grid = new GridSettings
            {
                Level1Keys = ["", "S", "D", "F", "G", "H", "J", "K", "L", ";"]
            }
        };

        settings.Normalize();

        Assert.Equal("", settings.Activation.ToggleKey);
        Assert.Equal("", settings.Keys.Up);
        Assert.Equal("", settings.Grid.Level1Keys[0]);
    }

    [Fact]
    public void Normalize_CanonicalizesAndRepairsValues()
    {
        var settings = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "capslock" },
            Keys = new KeysSettings { Up = "LeftCtrl", Down = "NoSuchKey" },
            Grid = new GridSettings
            {
                Level1Keys = ["a", "s", "d", "f", "g", "h", "j", "k", "l", ";"]
            }
        };

        settings.Normalize();

        Assert.Equal("CapsLock", settings.Activation.ToggleKey);
        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("K", settings.Keys.Down);
        Assert.Equal("A", settings.Grid.Level1Keys[0]);
        Assert.Equal("Semicolon", settings.Grid.Level1Keys[9]);
    }

    [Fact]
    public void Normalize_RepairsGridArraysAndActionLevel()
    {
        var settings = new Settings
        {
            Grid = new GridSettings
            {
                Level1Keys = [],
                Level2Keys = [],
                SubgridKeys = null!,
                ActionLevel = "everything"
            }
        };

        settings.Normalize();

        Assert.Equal(10, settings.Grid.Level1Keys.Length);
        Assert.Equal(30, settings.Grid.Level2Keys.Length);
        Assert.Equal(30, settings.Grid.SubgridKeys.Length);
        Assert.Equal("subgrid", settings.Grid.ActionLevel);
        Assert.Equal("Q", settings.Grid.Level2Keys[0]);
        Assert.Equal("Slash", settings.Grid.SubgridKeys[29]);
    }

    [Fact]
    public void Normalize_RepairsDuplicateLevel2Keys()
    {
        var settings = new Settings();
        settings.Grid.Level2Keys[1] = "Q";

        settings.Normalize();

        Assert.Equal("Q", settings.Grid.Level2Keys[0]);
        Assert.Equal("W", settings.Grid.Level2Keys[1]);
        Assert.Equal(
            settings.Grid.Level2Keys.Length,
            settings.Grid.Level2Keys.Distinct().Count());
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
        Assert.Equal("I", settings.Grid.Level1Keys[0]);
        Assert.Equal("I", settings.Keys.Up);
        Assert.Null(swapped);
    }

    [Fact]
    public void TryAssign_SwapsWithinGridPool()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.True(KeyBindingCatalog.TryAssign(settings, "grid0", "S", out var swapped));
        Assert.Equal("S", settings.Grid.Level1Keys[0]);
        Assert.Equal("A", settings.Grid.Level1Keys[1]);
        Assert.Equal("grid1", swapped);
    }

    [Fact]
    public void TryAssign_RejectsEscapeModifiersAndGridControls()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.False(KeyBindingCatalog.TryAssign(settings, "up", "Escape", out _));
        Assert.False(KeyBindingCatalog.TryAssign(settings, "up", "LeftCtrl", out _));
        Assert.False(KeyBindingCatalog.TryAssign(settings, "up", "None", out _));

        foreach (string reserved in new[] { "Space", "Enter", "Tab", "Backspace", "CapsLock" })
        {
            Assert.False(KeyBindingCatalog.TryAssign(settings, "grid0", reserved, out _));
        }
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
        settings.Grid.Level1Keys[0] = "Q";
        settings.Grid.Level2Keys[0] = "Y";
        settings.Grid.SubgridKeys[0] = "Y";
        settings.Grid.ActionLevel = "2";

        KeyBindingCatalog.ResetAll(settings);

        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("A", settings.Grid.Level1Keys[0]);
        Assert.Equal("Q", settings.Grid.Level2Keys[0]);
        Assert.Equal("Q", settings.Grid.SubgridKeys[0]);
        Assert.Equal("subgrid", settings.Grid.ActionLevel);
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
