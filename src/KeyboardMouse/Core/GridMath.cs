using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Pure rectangle subdivision for the Mouseless-style grid. The grid is a single
/// row-major lattice of small cells, so one cell is just one division of the
/// monitor rectangle. Boundaries are computed from the parent, so odd pixel
/// sizes never overlap or leave gaps.
/// </summary>
public static class GridMath
{
    /// <summary>
    /// Returns one cell of a row-major <paramref name="columns"/> by
    /// <paramref name="rows"/> subdivision of <paramref name="parent"/>.
    /// </summary>
    public static Rectangle SubRect(Rectangle parent, int index, int columns, int rows)
    {
        if (columns < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        if (index < 0 || index >= columns * rows)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int column = index % columns;
        int row = index / columns;
        return Cell(parent, column, row, columns, rows);
    }

    /// <summary>Returns one cell by explicit column and row.</summary>
    public static Rectangle Cell(Rectangle parent, int column, int row, int columns, int rows)
    {
        if (columns < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        if (column < 0 || column >= columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if (row < 0 || row >= rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        int left = parent.Left + (int)((long)parent.Width * column / columns);
        int right = parent.Left + (int)((long)parent.Width * (column + 1) / columns);
        int top = parent.Top + (int)((long)parent.Height * row / rows);
        int bottom = parent.Top + (int)((long)parent.Height * (row + 1) / rows);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    public static Point Center(Rectangle rect) => new(
        rect.Left + (rect.Width / 2),
        rect.Top + (rect.Height / 2));
}
