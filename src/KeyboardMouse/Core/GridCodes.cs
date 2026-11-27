namespace KeyboardMouse.Core;

/// <summary>
/// The three-character code written in every cell of the grid.
///
/// The grid is one flat layout: every cell shows its complete code from the
/// moment the overlay opens, and typing all three characters moves to that
/// cell. The lattice is cut into a few rectangular blocks, and the three
/// characters address a cell like this:
///
/// <code>
///   character 0  which block                    (blocks row-major)
///   character 1  the column inside that block
///   character 2  the row inside that block
/// </code>
///
/// Typing therefore narrows the overlay to one block, then to one column of
/// it, then to a single cell. Every block is at most as wide and as tall as
/// there are keys, and there are never more blocks than keys. When the
/// configured keys are too few for the lattice, more keys are used
/// (<see cref="KeysNeeded"/>): the number of cells is never reduced.
/// </summary>
public static class GridCodes
{
    /// <summary>Every code is three characters, whatever the grid size.</summary>
    public const int Length = 3;

    /// <summary>How the lattice is cut into blocks.</summary>
    public readonly record struct Blocks(int Across, int Down)
    {
        public int Count => Across * Down;
    }

    /// <summary>
    /// The cheapest cut for <paramref name="radix"/> keys: the fewest blocks,
    /// then the most even ones. Null when that many keys cannot address the
    /// lattice in three characters.
    /// </summary>
    public static Blocks? Plan(int columns, int rows, int radix)
    {
        if (columns < 1 || rows < 1 || radix < 1)
        {
            return null;
        }

        Blocks? best = null;
        int bestWaste = int.MaxValue;

        for (int across = 1; across <= radix; across++)
        {
            if (Ceil(columns, across) > radix)
            {
                continue;
            }

            for (int down = 1; across * down <= radix; down++)
            {
                if (Ceil(rows, down) > radix)
                {
                    continue;
                }

                // Prefer fewer blocks; among equals, blocks closest to square
                // in cells.
                int waste = Math.Abs(Ceil(columns, across) - Ceil(rows, down));
                var candidate = new Blocks(across, down);
                if (best is null ||
                    candidate.Count < best.Value.Count ||
                    (candidate.Count == best.Value.Count && waste < bestWaste))
                {
                    best = candidate;
                    bestWaste = waste;
                }

                break; // a larger 'down' only adds blocks for this 'across'
            }
        }

        return best;
    }

    /// <summary>The fewest keys that address every cell in three characters.</summary>
    public static int KeysNeeded(int columns, int rows)
    {
        for (int radix = 2; ; radix++)
        {
            if (Plan(columns, rows, radix) is not null)
            {
                return radix;
            }
        }
    }

    /// <summary>The block a column or row falls in, with blocks spread evenly.</summary>
    public static int BlockOf(int index, int count, int blocks) =>
        (int)((((long)index + 1) * blocks - 1) / count);

    /// <summary>The first column or row of a block.</summary>
    public static int BlockStart(int block, int count, int blocks) =>
        (int)((long)count * block / blocks);

    public static string Code(int column, int row, int columns, int rows, Blocks plan, IReadOnlyList<string> keys)
    {
        int bx = BlockOf(column, columns, plan.Across);
        int by = BlockOf(row, rows, plan.Down);
        int block = (by * plan.Across) + bx;
        return keys[block] +
               keys[column - BlockStart(bx, columns, plan.Across)] +
               keys[row - BlockStart(by, rows, plan.Down)];
    }

    /// <summary>
    /// One label per cell, row-major. <paramref name="keys"/> must hold at
    /// least <see cref="KeysNeeded"/> keys; only that many are used.
    /// </summary>
    public static string[] Build(int columns, int rows, IReadOnlyList<string> keys)
    {
        var plan = Plan(columns, rows, keys.Count)
            ?? throw new ArgumentException(
                $"{keys.Count} keys cannot address a {columns} x {rows} grid; {KeysNeeded(columns, rows)} are needed.",
                nameof(keys));

        var labels = new string[columns * rows];

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                labels[(row * columns) + column] = Code(column, row, columns, rows, plan, keys);
            }
        }

        return labels;
    }

    /// <summary>Whether a cell is still reachable from what has been typed.</summary>
    public static bool Matches(string? code, string typed)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        if (typed.Length == 0)
        {
            return true;
        }

        return typed.Length <= code.Length &&
               code.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
    }

    private static int Ceil(int value, int divisor) => (value + divisor - 1) / divisor;
}
