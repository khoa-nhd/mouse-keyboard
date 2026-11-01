namespace KeyboardMouse.Core;

/// <summary>
/// Abstraction over the movement thread so controllers can be unit-tested
/// without starting a real thread.
/// </summary>
public interface IMovementControl
{
    void SetDirection(Direction direction, bool down);

    void ClearDirections();

    void SetMode(MovementMode mode);

    void SetMoveProfile(SpeedProfile profile);

    void SetScrollProfile(SpeedProfile profile, bool smooth);

    void SetScrollPrecision(bool precision);
}
