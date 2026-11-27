using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class GridCodesTests
{
    [Theory]
    [InlineData(28, 18, 10)]
    [InlineData(19, 12, 10)]
    [InlineData(37, 22, 11)] // 2560 x 1440 borrows one extra key
    [InlineData(56, 32, 14)]
    [InlineData(100, 100, 25)]
    public void KeysNeededGrowsOnlyWhenTheConfiguredTenRunOut(int columns, int rows, int atMost)
    {
        int needed = GridCodes.KeysNeeded(columns, rows);
        Assert.InRange(needed, 2, atMost);
        Assert.True(needed * needed * needed >= columns * rows);
    }

    [Theory]
    [InlineData(28, 18)]
    [InlineData(37, 22)]
    [InlineData(56, 32)]
    [InlineData(100, 100)]
    [InlineData(2, 2)]
    public void EveryCellGetsItsOwnThreeCharacterCode(int columns, int rows)
    {
        int radix = Math.Max(10, GridCodes.KeysNeeded(columns, rows));
        var keys = Enumerable.Range(0, radix).Select(i => ((char)('!' + i)).ToString()).ToArray();

        var labels = GridCodes.Build(columns, rows, keys);

        Assert.Equal(columns * rows, labels.Length);
        Assert.All(labels, label => Assert.Equal(3, label.Length));
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }
}
