namespace KeyboardMouse.Core;

/// <summary>
/// Converts a fractional wheel velocity into SendInput deltas.
/// Smooth mode emits every whole wheel unit (sub-notch deltas are valid and
/// let every app scroll gradually); non-smooth mode waits for full 120-unit
/// notches, like a classic mouse wheel. Pure and unit-tested.
/// </summary>
public sealed class WheelAccumulator
{
    public const int WheelDelta = 120;

    private double _residual;

    public double Residual => _residual;

    /// <summary>Adds a fractional delta and returns the integer delta to send (0 = none).</summary>
    public int Add(double delta, bool smooth)
    {
        _residual += delta;

        if (smooth)
        {
            int whole = (int)_residual;
            _residual -= whole;
            return whole;
        }

        int notches = (int)(_residual / WheelDelta);
        if (notches == 0)
        {
            return 0;
        }

        _residual -= notches * WheelDelta;
        return notches * WheelDelta;
    }

    public void Reset() => _residual = 0;
}
