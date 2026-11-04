using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class HintCoordinatesTests
{
    [Fact]
    public void FrameToDesktopUsesVirtualScreenOrigin()
    {
        var frame = new Rectangle(-1920, 0, 1920, 1080);
        var local = new Rectangle(100, 50, 200, 100);

        var desktop = HintCoordinates.FrameToDesktop(frame, local);

        Assert.Equal(new TargetBounds(-1820, 50, 200, 100), desktop);
    }

    [Fact]
    public void FrameToDesktopAppliesCaptureScale()
    {
        var frame = new Rectangle(1920, -200, 3840, 2160);
        var local = new Rectangle(100, 50, 200, 100);

        var desktop = HintCoordinates.FrameToDesktop(frame, local, scaleX: 2.0, scaleY: 2.0);

        Assert.Equal(new TargetBounds(1970, -175, 100, 50), desktop);
    }

    [Fact]
    public void FrameToDesktopMapsPoints()
    {
        var frame = new Rectangle(-100, 40, 800, 600);

        var desktop = HintCoordinates.FrameToDesktop(
            frame,
            new Point(50, 50));

        Assert.Equal(new Point(-50, 90), desktop);
    }

    [Fact]
    public void DesktopToFrameRoundTrips()
    {
        var frame = new Rectangle(-1920, 0, 1920, 1080);
        var local = new Rectangle(100, 50, 200, 100);
        var desktop = HintCoordinates.FrameToDesktop(
            frame,
            local,
            scaleX: 2.0,
            scaleY: 2.0);

        var roundTrip = HintCoordinates.DesktopToFrame(
            frame,
            desktop,
            scaleX: 2.0,
            scaleY: 2.0);

        Assert.Equal(local, roundTrip);
    }

    [Fact]
    public void ClipsBoundsToDesktop()
    {
        var clipped = HintCoordinates.ClipTo(
            new TargetBounds(-50, -50, 100, 100),
            new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new TargetBounds(0, 0, 50, 50), clipped);
    }

    [Fact]
    public void ClampToBoundsKeepsPointInside()
    {
        var bounds = new TargetBounds(10, 20, 30, 40);

        Assert.Equal(
            new Point(10, 20),
            HintCoordinates.ClampToBounds(new Point(-5, -5), bounds));
        Assert.Equal(
            new Point(39, 59),
            HintCoordinates.ClampToBounds(new Point(100, 100), bounds));
        Assert.Equal(
            new Point(20, 30),
            HintCoordinates.ClampToBounds(new Point(20, 30), bounds));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void RejectsInvalidScale(double scale)
    {
        var frame = new Rectangle(0, 0, 100, 100);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => HintCoordinates.FrameToDesktop(
                frame,
                new Rectangle(0, 0, 10, 10),
                scaleX: scale,
                scaleY: 1.0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => HintCoordinates.DesktopToFrame(
                frame,
                new TargetBounds(0, 0, 10, 10),
                scaleX: 1.0,
                scaleY: scale));
    }

    [Fact]
    public void RejectsClampingIntoInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HintCoordinates.ClampToBounds(
                new Point(0, 0),
                new TargetBounds(0, 0, 0, 10)));
    }
}
