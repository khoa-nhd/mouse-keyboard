using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>
/// Pure 3×3 subdivision used by grid mode. Each cell's boundaries are computed
/// from the parent rectangle, so odd pixel sizes never overlap or leave gaps.
/// </summary>
public static class GridMath
{
    public const int Cells = 9;
    public const int Columns = 3;
    public const int Rows = 3;

    public static Rectangle SubRect(Rectangle parent, int cellIndex)
    {
        if (cellIndex is < 0 or >= Cells)
        {
            throw new ArgumentOutOfRangeException(nameof(cellIndex));
        }

        int column = cellIndex % Columns;
        int row = cellIndex / Columns;

        int left = parent.Left + (int)((long)parent.Width * column / Columns);
        int right = parent.Left + (int)((long)parent.Width * (column + 1) / Columns);
        int top = parent.Top + (int)((long)parent.Height * row / Rows);
        int bottom = parent.Top + (int)((long)parent.Height * (row + 1) / Rows);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    public static Point Center(Rectangle rect) => new(
        rect.Left + (rect.Width / 2),
        rect.Top + (rect.Height / 2));
}
