using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class SpeedProfileTests
{
    [Fact]
    public void VelocityAtZero_IsBaseSpeed()
    {
        var profile = new SpeedProfile(300, 2000, 3000, 5);
        Assert.Equal(300, profile.VelocityAt(0));
    }

    [Fact]
    public void VelocityAcceleratesLinearly()
    {
        var profile = new SpeedProfile(300, 2000, 3000, 5);
        Assert.Equal(1800, profile.VelocityAt(0.5), precision: 6);
    }

    [Fact]
    public void VelocityClampsToMax()
    {
        var profile = new SpeedProfile(300, 2000, 3000, 5);
        Assert.Equal(2000, profile.VelocityAt(10));
    }

    [Fact]
    public void NegativeTime_IsTreatedAsZero()
    {
        var profile = new SpeedProfile(300, 2000, 3000, 5);
        Assert.Equal(300, profile.VelocityAt(-2));
    }

    [Fact]
    public void PrecisionProfile_HasNoAcceleration()
    {
        var profile = SpeedProfile.Precision;
        Assert.Equal(60, profile.VelocityAt(0));
        Assert.Equal(60, profile.VelocityAt(10));
    }

    [Fact]
    public void DiagonalVector_IsNormalized()
    {
        var (x, y) = (Direction.Down | Direction.Right).ToNormalizedVector();
        Assert.Equal(1.0, Math.Sqrt(x * x + y * y), precision: 10);
        Assert.Equal(0.7071, x, precision: 4);
        Assert.Equal(0.7071, y, precision: 4);
    }

    [Fact]
    public void OppositeDirections_CancelOut()
    {
        var (x, y) = (Direction.Up | Direction.Down | Direction.Left | Direction.Right).ToNormalizedVector();
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void TapStep_UsesSingleDirectionVector()
    {
        var (x, y) = Direction.Up.ToUnitVector();
        Assert.Equal(0, x);
        Assert.Equal(-1, y);
    }

    [Fact]
    public void InvalidProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpeedProfile(-1, 100, 10, 1));
        Assert.Throws<ArgumentException>(() => new SpeedProfile(200, 100, 10, 1));
    }
}
