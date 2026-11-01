using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class WheelAccumulatorTests
{
    [Fact]
    public void SmoothMode_SendsWholeUnitsImmediately()
    {
        var wheel = new WheelAccumulator();

        Assert.Equal(30, wheel.Add(30, smooth: true));
        Assert.Equal(30, wheel.Add(30, smooth: true));
        Assert.Equal(0, wheel.Residual);
    }

    [Fact]
    public void SmoothMode_AccumulatesFractions()
    {
        var wheel = new WheelAccumulator();

        Assert.Equal(0, wheel.Add(0.4, smooth: true));
        Assert.Equal(1, wheel.Add(0.7, smooth: true));
        Assert.Equal(0.1, wheel.Residual, precision: 10);
    }

    [Fact]
    public void NonSmoothMode_WaitsForFullNotches()
    {
        var wheel = new WheelAccumulator();

        Assert.Equal(0, wheel.Add(60, smooth: false));
        Assert.Equal(120, wheel.Add(70, smooth: false));
        Assert.Equal(10, wheel.Residual, precision: 10);

        Assert.Equal(120, wheel.Add(110, smooth: false));
        Assert.Equal(0, wheel.Residual, precision: 10);
    }

    [Fact]
    public void NegativeDeltas_ScrollTheOtherWay()
    {
        var wheel = new WheelAccumulator();

        Assert.Equal(-120, wheel.Add(-120, smooth: false));
        Assert.Equal(-1, wheel.Add(-1.5, smooth: true));
        Assert.Equal(-0.5, wheel.Residual, precision: 10);
    }

    [Fact]
    public void Reset_DropsResidual()
    {
        var wheel = new WheelAccumulator();
        wheel.Add(100, smooth: false);

        wheel.Reset();

        Assert.Equal(0, wheel.Residual);
        Assert.Equal(120, wheel.Add(120, smooth: false));
    }
}
