namespace KeyboardMouse.Core;

/// <summary>
/// The four-character code written in every cell of the grid.
///
/// The grid is one flat layout: every cell shows its complete code from the
/// moment the overlay opens, and typing all four characters clicks that cell.
/// The four characters address the cell by column and row:
///
/// <code>
///   digit 0  which band of ten columns      column / radix
///   digit 1  the column inside that band    column % radix
///   digit 2  which band of ten rows         row    / radix
///   digit 3  the row inside that band       row    % radix
/// </code>
///
/// Typing therefore narrows the overlay to a band of columns, then to one
/// column, then to a band of rows, then to a single cell, without any second
/// grid appearing.
/// </summary>
public static class GridCodes
{
    /// <summary>Every code is four characters, whatever the grid size.</summary>
    public const int Length = 4;

    public static string Code(int column, int row, IReadOnlyList<string> keys)
    {
        int radix = keys.Count;
        return keys[column / radix] +
               keys[column % radix] +
               keys[row / radix] +
               keys[row % radix];
    }

    /// <summary>One label per cell, row-major.</summary>
    public static string[] Build(int columns, int rows, IReadOnlyList<string> keys)
    {
        var labels = new string[columns * rows];

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                labels[(row * columns) + column] = Code(column, row, keys);
            }
        }

        return labels;
    }

    /// <summary>
    /// Whether a cell is still reachable from what has been typed. A code with a
    /// blank key is shorter than four characters and never matches, which is how
    /// an unbound key makes its cells unselectable.
    /// </summary>
    public static bool Matches(string? code, string typed)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Length)
        {
            return false;
        }

        if (typed.Length == 0)
        {
            return true;
        }

        return typed.Length <= Length &&
               code.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
    }
}
