using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class GridMathTests
{
    [Fact]
    public void NineCells_CoverTheParentWithoutGaps()
    {
        var parent = new Rectangle(0, 0, 900, 600);
        var cells = Enumerable.Range(0, GridMath.Cells)
            .Select(i => GridMath.SubRect(parent, i))
            .ToArray();

        Assert.Equal(parent.Width * parent.Height, cells.Sum(c => c.Width * c.Height));
        Assert.All(cells, c => Assert.Equal(300, c.Width));
        Assert.All(cells, c => Assert.Equal(200, c.Height));

        Assert.Equal(new Rectangle(0, 0, 300, 200), cells[0]);
        Assert.Equal(new Rectangle(600, 400, 300, 200), cells[8]);
    }

    [Fact]
    public void OddSizes_DoNotOverlapOrLosePixels()
    {
        var parent = new Rectangle(0, 0, 1000, 700);
        var row = Enumerable.Range(0, GridMath.Columns)
            .Select(i => GridMath.SubRect(parent, i))
            .ToArray();

        Assert.Equal(0, row[0].Left);
        Assert.Equal(row[0].Right, row[1].Left);
        Assert.Equal(row[1].Right, row[2].Left);
        Assert.Equal(1000, row[2].Right);
        Assert.Equal(parent.Width, row.Sum(c => c.Width));
        Assert.Equal([333, 333, 334], row.Select(c => c.Width));
    }

    [Fact]
    public void NegativeMonitorOrigins_KeepWorking()
    {
        var parent = new Rectangle(-1920, -200, 1920, 1080);

        Assert.Equal(new Rectangle(-1920, -200, 640, 360), GridMath.SubRect(parent, 0));
        Assert.Equal(new Rectangle(-640, 520, 640, 360), GridMath.SubRect(parent, 8));
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
        var level1 = GridMath.SubRect(monitor, 4);
        var level2 = GridMath.SubRect(level1, 4);
        var level3 = GridMath.SubRect(level2, 4);

        Assert.True(monitor.Contains(level1));
        Assert.True(level1.Contains(level2));
        Assert.True(level2.Contains(level3));
        Assert.True(level3.Width > 0 && level3.Height > 0);
    }

    [Fact]
    public void InvalidCell_Throws()
    {
        var parent = new Rectangle(0, 0, 300, 300);

        Assert.Throws<ArgumentOutOfRangeException>(() => GridMath.SubRect(parent, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridMath.SubRect(parent, 9));
    }
}
