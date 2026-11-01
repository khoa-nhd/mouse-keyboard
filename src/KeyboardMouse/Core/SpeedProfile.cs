namespace KeyboardMouse.Core;

/// <summary>
/// Acceleration curve for pointer movement: v(t) = min(max, base + accel * t).
/// Pure and unit-tested.
/// </summary>
public sealed class SpeedProfile
{
    public SpeedProfile(double baseSpeed, double maxSpeed, double acceleration, double tapStep)
    {
        if (baseSpeed < 0 || maxSpeed < 0 || acceleration < 0 || tapStep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseSpeed), "Speed values must be non-negative.");
        }

        if (maxSpeed < baseSpeed)
        {
            throw new ArgumentException("maxSpeed must be greater than or equal to baseSpeed.", nameof(maxSpeed));
        }

        BaseSpeed = baseSpeed;
        MaxSpeed = maxSpeed;
        Acceleration = acceleration;
        TapStep = tapStep;
    }

    public double BaseSpeed { get; }
    public double MaxSpeed { get; }
    public double Acceleration { get; }
    public double TapStep { get; }

    public static SpeedProfile Normal => new(300, 2000, 3000, 5);
    public static SpeedProfile Precision => new(60, 60, 0, 1);
    public static SpeedProfile Turbo => new(1500, 5000, 8000, 40);

    /// <summary>Velocity in pixels per second at <paramref name="seconds"/> after movement started.</summary>
    public double VelocityAt(double seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        return Math.Min(MaxSpeed, BaseSpeed + Acceleration * seconds);
    }
}
