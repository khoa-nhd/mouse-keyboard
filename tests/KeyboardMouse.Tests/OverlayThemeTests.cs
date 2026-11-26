using KeyboardMouse.Config;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class OverlayThemeTests
{
    [Fact]
    public void NewSettingsKeepTheClassicLook()
    {
        var settings = new Settings();
        settings.Normalize();

        Assert.Equal("classic", settings.Hint.Style);
        Assert.Equal("classic", settings.Grid.Theme);
    }

    [Theory]
    [InlineData("bubble", HintTheme.Bubble)]
    [InlineData("BlueOutline", HintTheme.BlueOutline)]
    [InlineData(" keycap ", HintTheme.Keycap)]
    [InlineData("neon", HintTheme.Classic)]
    [InlineData(null, HintTheme.Classic)]
    public void HintStyleIdsParse(string? id, HintTheme expected) =>
        Assert.Equal(expected, OverlayThemeNames.ParseHint(id));

    [Theory]
    [InlineData("glow", GridTheme.Glow)]
    [InlineData("KEYBOARD", GridTheme.Keyboard)]
    [InlineData("", GridTheme.Classic)]
    public void GridThemeIdsParse(string? id, GridTheme expected) =>
        Assert.Equal(expected, OverlayThemeNames.ParseGrid(id));

    [Fact]
    public void UnknownStoredValuesFallBackToClassic()
    {
        var settings = new Settings();
        settings.Hint.Style = "rainbow";
        settings.Grid.Theme = "rainbow";
        settings.Normalize();

        Assert.Equal("classic", settings.Hint.Style);
        Assert.Equal("classic", settings.Grid.Theme);
    }

    [Fact]
    public void EveryThemeHasADistinctIdThatRoundTrips()
    {
        foreach (var theme in Enum.GetValues<HintTheme>())
        {
            Assert.Equal(theme, OverlayThemeNames.ParseHint(OverlayThemeNames.ToId(theme)));
        }

        foreach (var theme in Enum.GetValues<GridTheme>())
        {
            Assert.Equal(theme, OverlayThemeNames.ParseGrid(OverlayThemeNames.ToId(theme)));
        }

        Assert.Equal(8, OverlayThemeNames.HintThemeIds.Distinct().Count());
        Assert.Equal(8, OverlayThemeNames.GridThemeIds.Distinct().Count());
    }
}
