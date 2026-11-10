using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Pure rectangle subdivision for the Mouseless-style grid. The primary lattice
/// is ten columns by thirty rows (all of it drawn at once, each cell labelled
/// with its two-key code), then a ten by three subgrid inside the chosen cell,
/// then a three by three micro grid inside the chosen sub-cell. Boundaries are
/// computed from the parent rectangle, so odd pixel sizes never overlap or leave
/// gaps.
/// </summary>
public static class GridMath
{
    public const int Level1Columns = 10;
    public const int Level1Rows = 1;
    public const int Level1Cells = Level1Columns * Level1Rows;

    public const int Level2Columns = 1;
    public const int Level2Rows = 30;
    public const int Level2Cells = Level2Columns * Level2Rows;

    /// <summary>
    /// The lattice the overlay shows first: the product of both top levels, so
    /// every cell has its complete two-key code before anything is typed.
    /// </summary>
    public const int PrimaryColumns = Level1Columns;
    public const int PrimaryRows = Level2Rows;
    public const int PrimaryCells = PrimaryColumns * PrimaryRows;

    public const int SubgridColumns = 10;
    public const int SubgridRows = 3;
    public const int SubgridCells = SubgridColumns * SubgridRows;

    public const int MicroColumns = 3;
    public const int MicroRows = 3;
    public const int MicroCells = MicroColumns * MicroRows;

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
