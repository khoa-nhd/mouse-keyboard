namespace KeyboardMouse.Core;

[Flags]
public enum Direction
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 4,
    Right = 8
}

public static class DirectionExtensions
{
    /// <summary>Unit vector for a single direction key (used for the tap step).</summary>
    public static (double X, double Y) ToUnitVector(this Direction direction) => direction switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        _ => (0, 0)
    };

    /// <summary>
    /// Normalized vector for any combination of held directions, so diagonals
    /// are not faster than straight movement.
    /// </summary>
    public static (double X, double Y) ToNormalizedVector(this Direction directions)
    {
        int dx = (directions.HasFlag(Direction.Right) ? 1 : 0) - (directions.HasFlag(Direction.Left) ? 1 : 0);
        int dy = (directions.HasFlag(Direction.Down) ? 1 : 0) - (directions.HasFlag(Direction.Up) ? 1 : 0);

        if (dx == 0 && dy == 0)
        {
            return (0, 0);
        }

        if (dx != 0 && dy != 0)
        {
            const double diagonal = 0.7071067811865476;
            return (dx * diagonal, dy * diagonal);
        }

        return (dx, dy);
    }
}
