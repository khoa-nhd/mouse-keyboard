using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class GridMathTests
{
    [Fact]
    public void TenColumns_CoverTheParentWithoutGaps()
    {
        var parent = new Rectangle(0, 0, 1000, 700);
        var cells = Enumerable.Range(0, GridMath.Level1Cells)
            .Select(i => GridMath.SubRect(parent, i, GridMath.Level1Columns, GridMath.Level1Rows))
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
        var bands = Enumerable.Range(0, GridMath.Level2Cells)
            .Select(i => GridMath.SubRect(column, i, GridMath.Level2Columns, GridMath.Level2Rows))
            .ToArray();

        Assert.Equal(column.Width * column.Height, bands.Sum(b => b.Width * b.Height));
        Assert.All(bands, b => Assert.Equal(192, b.Width));
        Assert.Equal(36, bands[0].Height);
        Assert.Equal(new Rectangle(192, 0, 192, 36), bands[0]);
        Assert.Equal(new Rectangle(192, 1044, 192, 36), bands[29]);
    }

    [Fact]
    public void SubgridIsTenByThree()
    {
        var cell = new Rectangle(192, 360, 192, 36);
        var subcells = Enumerable.Range(0, GridMath.SubgridCells)
            .Select(i => GridMath.SubRect(cell, i, GridMath.SubgridColumns, GridMath.SubgridRows))
            .ToArray();

        Assert.Equal(cell.Width * cell.Height, subcells.Sum(c => c.Width * c.Height));
        Assert.Equal(new Rectangle(192, 360, 19, 12), subcells[0]);
        Assert.Equal(new Rectangle(364, 384, 20, 12), subcells[29]);
    }

    [Fact]
    public void OddSizes_DoNotOverlapOrLosePixels()
    {
        var parent = new Rectangle(0, 0, 1003, 701);
        var row = Enumerable.Range(0, GridMath.Level1Columns)
            .Select(i => GridMath.SubRect(parent, i, GridMath.Level1Columns, 1))
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
            GridMath.SubRect(parent, 0, GridMath.Level1Columns, 1));
        Assert.Equal(new Rectangle(-192, -200, 192, 1080),
            GridMath.SubRect(parent, 9, GridMath.Level1Columns, 1));
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
        var column = GridMath.SubRect(monitor, 4, GridMath.Level1Columns, 1);
        var band = GridMath.SubRect(column, 15, 1, GridMath.Level2Rows);
        var subcell = GridMath.SubRect(band, 7, GridMath.SubgridColumns, GridMath.SubgridRows);

        Assert.True(monitor.Contains(column));
        Assert.True(column.Contains(band));
        Assert.True(band.Contains(subcell));
        Assert.True(subcell.Width > 0 && subcell.Height > 0);
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
}
