using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class TapHoldDetectorTests
{
    [Fact]
    public void UpBeforeTimeout_IsTap()
    {
        var detector = new TapHoldDetector(200);

        Assert.Equal(TapHoldAction.None, detector.OnKeyDown(1000));
        Assert.Equal(TapHoldAction.Tap, detector.OnKeyUp(1199));
    }

    [Fact]
    public void UpAtExactlyTimeout_IsHold()
    {
        var detector = new TapHoldDetector(200);

        detector.OnKeyDown(0);
        Assert.Equal(TapHoldAction.HoldEnd, detector.OnKeyUp(200));
    }

    [Fact]
    public void PollAtTimeout_PromotesToHold()
    {
        var detector = new TapHoldDetector(200);
        detector.OnKeyDown(0);

        Assert.Equal(TapHoldAction.None, detector.Poll(199));
        Assert.Equal(TapHoldAction.HoldStart, detector.Poll(200));
        Assert.True(detector.IsHolding);
        Assert.Equal(TapHoldAction.HoldEnd, detector.OnKeyUp(400));
    }

    [Fact]
    public void AnotherKeyDuringPress_StartsHoldImmediately()
    {
        var detector = new TapHoldDetector(200);
        detector.OnKeyDown(0);

        Assert.Equal(TapHoldAction.HoldStart, detector.OnOtherKeyDown(50));
        Assert.Equal(TapHoldAction.HoldEnd, detector.OnKeyUp(60));
    }

    [Fact]
    public void AnotherKeyAfterTimeout_DoesNotStartHoldTwice()
    {
        var detector = new TapHoldDetector(200);
        detector.OnKeyDown(0);
        Assert.Equal(TapHoldAction.HoldStart, detector.Poll(200));

        Assert.Equal(TapHoldAction.None, detector.OnOtherKeyDown(210));
    }

    [Fact]
    public void RepeatDown_IsIgnored()
    {
        var detector = new TapHoldDetector(200);

        detector.OnKeyDown(0);
        Assert.Equal(TapHoldAction.None, detector.OnKeyDown(30));
        Assert.Equal(TapHoldAction.Tap, detector.OnKeyUp(40));
    }

    [Fact]
    public void RepeatUp_IsIgnored()
    {
        var detector = new TapHoldDetector(200);

        detector.OnKeyDown(0);
        detector.OnKeyUp(40);
        Assert.Equal(TapHoldAction.None, detector.OnKeyUp(50));
    }

    [Fact]
    public void PendingFlag_TracksTapWindow()
    {
        var detector = new TapHoldDetector(200);
        Assert.False(detector.IsPending);

        detector.OnKeyDown(0);
        Assert.True(detector.IsPending);

        detector.Poll(200);
        Assert.False(detector.IsPending);
        Assert.True(detector.IsHolding);
    }

    [Fact]
    public void ResetClearsState()
    {
        var detector = new TapHoldDetector(200);
        detector.OnKeyDown(0);
        detector.Reset();

        Assert.False(detector.IsDown);
        Assert.False(detector.IsPending);
        Assert.False(detector.IsHolding);
        Assert.Equal(TapHoldAction.None, detector.OnKeyUp(10));
    }

    [Fact]
    public void InvalidTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TapHoldDetector(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TapHoldDetector(-5));
    }
}
