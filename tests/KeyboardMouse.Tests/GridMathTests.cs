using System.Drawing;
using KeyboardMouse.Config;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class GridMathTests
{
    [Fact]
    public void TenColumns_CoverTheParentWithoutGaps()
    {
        var parent = new Rectangle(0, 0, 1000, 700);
        var cells = Enumerable.Range(0, 10)
            .Select(i => GridMath.SubRect(parent, i, 10, 1))
            .ToArray();

        Assert.Equal(parent.Width * parent.Height, cells.Sum(c => c.Width * c.Height));
        Assert.All(cells, c => Assert.Equal(100, c.Width));
        Assert.All(cells, c => Assert.Equal(700, c.Height));

        Assert.Equal(new Rectangle(0, 0, 100, 700), cells[0]);
        Assert.Equal(new Rectangle(900, 0, 100, 700), cells[9]);
    }

    [Fact]
    public void ThirtyBands_CoverAColumnWithoutGaps()
    {
        var column = new Rectangle(192, 0, 192, 1080);
        var bands = Enumerable.Range(0, 30)
            .Select(i => GridMath.SubRect(column, i, 1, 30))
            .ToArray();

        Assert.Equal(column.Width * column.Height, bands.Sum(b => b.Width * b.Height));
        Assert.All(bands, b => Assert.Equal(192, b.Width));
        Assert.Equal(36, bands[0].Height);
        Assert.Equal(new Rectangle(192, 0, 192, 36), bands[0]);
        Assert.Equal(new Rectangle(192, 1044, 192, 36), bands[29]);
    }

    [Fact]
    public void SmallCells_TileTheWholeGrid()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var cells = Enumerable.Range(0, GridSettings.DefaultColumns * GridSettings.DefaultRows)
            .Select(i => GridMath.SubRect(
                monitor,
                i,
                GridSettings.DefaultColumns,
                GridSettings.DefaultRows))
            .ToArray();

        Assert.Equal(monitor.Width * monitor.Height, cells.Sum(c => c.Width * c.Height));
        Assert.All(cells, c => Assert.True(c.Width > 0 && c.Height > 0));
        Assert.Equal(monitor, Union(cells));
    }

    [Fact]
    public void OddSizes_DoNotOverlapOrLosePixels()
    {
        var parent = new Rectangle(0, 0, 1003, 701);
        var row = Enumerable.Range(0, 10)
            .Select(i => GridMath.SubRect(parent, i, 10, 1))
            .ToArray();

        Assert.Equal(0, row[0].Left);
        for (int i = 1; i < row.Length; i++)
        {
            Assert.Equal(row[i - 1].Right, row[i].Left);
        }

        Assert.Equal(parent.Width, row[^1].Right);
        Assert.Equal(parent.Width, row.Sum(c => c.Width));
    }

    [Fact]
    public void NegativeMonitorOrigins_KeepWorking()
    {
        var parent = new Rectangle(-1920, -200, 1920, 1080);

        Assert.Equal(new Rectangle(-1920, -200, 192, 1080),
            GridMath.SubRect(parent, 0, 10, 1));
        Assert.Equal(new Rectangle(-192, -200, 192, 1080),
            GridMath.SubRect(parent, 9, 10, 1));
    }

    [Fact]
    public void Center_RoundsDownToPixel()
    {
        Assert.Equal(new Point(250, 150), GridMath.Center(new Rectangle(100, 50, 301, 201)));
    }

    [Fact]
    public void RepeatedSubdivision_StaysInsideTheChosenCell()
    {
        var monitor = new Rectangle(-1280, 0, 1280, 1024);
        var cell = GridMath.SubRect(monitor, 100, GridSettings.DefaultColumns, GridSettings.DefaultRows);

        Assert.True(monitor.Contains(cell));
        Assert.True(cell.Width > 0 && cell.Height > 0);
    }

    [Fact]
    public void InvalidIndexOrDimensions_Throw()
    {
        var parent = new Rectangle(0, 0, 300, 300);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridMath.SubRect(parent, -1, 10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridMath.SubRect(parent, 10, 10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridMath.SubRect(parent, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridMath.SubRect(parent, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridMath.Cell(parent, 10, 0, 10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridMath.Cell(parent, 0, 1, 10, 1));
    }

    private static Rectangle Union(IEnumerable<Rectangle> rects)
    {
        var all = Rectangle.Empty;
        foreach (var rect in rects)
        {
            all = all.IsEmpty ? rect : Rectangle.Union(all, rect);
        }

        return all;
    }
}
