namespace KeyboardMouse.Core;

/// <summary>
/// Deterministic maximum-weight bipartite assignment used by fusion. Unlike a
/// greedy "take the highest edge first" loop, it evaluates competing candidates
/// together, so one UIA control cannot consume the only plausible CV match for
/// another control when a better global assignment exists.
/// </summary>
internal static class TargetAssignment
{
    /// <summary>
    /// Assigns rows to columns while maximizing the supplied scores. Negative
    /// scores and non-finite values are treated as zero. When
    /// <paramref name="allowUnmatched"/> is true each row gets a private dummy
    /// column, and the result contains -1 for an unmatched row.
    /// </summary>
    public static int[] Assign(double[,] scores, bool allowUnmatched = true)
    {
        ArgumentNullException.ThrowIfNull(scores);

        int rows = scores.GetLength(0);
        int realColumns = scores.GetLength(1);

        if (rows == 0)
        {
            return [];
        }

        if (realColumns == 0)
        {
            return Enumerable.Repeat(-1, rows).ToArray();
        }

        int columns = allowUnmatched ? realColumns + rows : realColumns;

        if (rows > columns)
        {
            throw new ArgumentException(
                "The score matrix needs at least as many columns as rows.",
                nameof(scores));
        }

        double maximum = 1.0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < realColumns; column++)
            {
                maximum = Math.Max(maximum, Normalize(scores[row, column]));
            }
        }

        // Hungarian solves a minimization problem. Convert each score to a
        // non-negative cost; dummy columns have score 0, so leaving a row
        // unmatched is always available and costs the same as a zero match.
        var cost = new double[rows, columns];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double score = column < realColumns
                    ? Normalize(scores[row, column])
                    : 0;
                cost[row, column] = maximum - score;
            }
        }

        int[] rowToColumn = SolveMinimumCost(cost);
        var result = new int[rows];

        for (int row = 0; row < rows; row++)
        {
            int column = rowToColumn[row];
            result[row] = column >= 0 && column < realColumns
                ? column
                : -1;
        }

        return result;
    }

    private static int[] SolveMinimumCost(double[,] cost)
    {
        int rows = cost.GetLength(0);
        int columns = cost.GetLength(1);
        var u = new double[rows + 1];
        var v = new double[columns + 1];
        var p = new int[columns + 1];
        var way = new int[columns + 1];

        for (int row = 1; row <= rows; row++)
        {
            p[0] = row;
            int column0 = 0;
            var minimum = new double[columns + 1];
            var used = new bool[columns + 1];

            for (int column = 1; column <= columns; column++)
            {
                minimum[column] = double.PositiveInfinity;
            }

            do
            {
                used[column0] = true;
                int row0 = p[column0];
                int column1 = 0;
                double delta = double.PositiveInfinity;

                for (int column = 1; column <= columns; column++)
                {
                    if (used[column])
                    {
                        continue;
                    }

                    double current =
                        cost[row0 - 1, column - 1] - u[row0] - v[column];

                    if (current < minimum[column])
                    {
                        minimum[column] = current;
                        way[column] = column0;
                    }

                    if (minimum[column] < delta)
                    {
                        delta = minimum[column];
                        column1 = column;
                    }
                }

                for (int column = 0; column <= columns; column++)
                {
                    if (used[column])
                    {
                        u[p[column]] += delta;
                        v[column] -= delta;
                    }
                    else if (double.IsFinite(minimum[column]))
                    {
                        minimum[column] -= delta;
                    }
                }

                column0 = column1;
            }
            while (p[column0] != 0);

            do
            {
                int column1 = way[column0];
                p[column0] = p[column1];
                column0 = column1;
            }
            while (column0 != 0);
        }

        var assignment = Enumerable.Repeat(-1, rows).ToArray();

        for (int column = 1; column <= columns; column++)
        {
            if (p[column] > 0)
            {
                assignment[p[column] - 1] = column - 1;
            }
        }

        return assignment;
    }

    private static double Normalize(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;
}
