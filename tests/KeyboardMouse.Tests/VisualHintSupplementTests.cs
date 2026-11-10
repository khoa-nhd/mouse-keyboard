using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Tests;

public class VisualHintSupplementTests
{
    private static ControlCandidate Control(
        ControlType type,
        Rectangle bounds,
        double interaction,
        double geometry = 0.9,
        GeometryIssue issues = GeometryIssue.None,
        Point? click = null)
    {
        var point = click ?? new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        return new ControlCandidate(
            bounds,
            type,
            VisualShape.Rectangle,
            VisualDetector.BorderEdge,
            0.8,
            interaction,
            ControlEvidence.Border)
        {
            Geometry = new TargetGeometry(bounds, bounds, point, geometry, issues, "test")
        };
    }

    private static IReadOnlyList<ClickableTarget> AddOne(ControlCandidate control, Rectangle region)
    {
        var result = new VisualDetectionResult(
            [new VisualCandidate(control.Bounds, VisualShape.Rectangle, VisualDetector.BorderEdge, 0.8)],
            [control],
            [1.0],
            5);

        return VisualHintSupplement.Add(result, region, [], maxElements: 400);
    }

    [Fact]
    public void AcceptsAConfidentInteractiveControl()
    {
        Assert.True(VisualHintSupplement.IsAcceptable(
            Control(ControlType.Button, new Rectangle(0, 0, 80, 30), interaction: 0.72)));
    }

    [Fact]
    public void AcceptsACleanNearMissOfAClickableType()
    {
        // Below the Button threshold (0.62) but with a clean box: this is the
        // band where most real buttons used to disappear.
        Assert.True(VisualHintSupplement.IsAcceptable(
            Control(ControlType.Button, new Rectangle(0, 0, 80, 30), interaction: 0.55)));
    }

    [Fact]
    public void RejectsANearMissWithWeakGeometry()
    {
        Assert.False(VisualHintSupplement.IsAcceptable(
            Control(ControlType.Button, new Rectangle(0, 0, 80, 30), interaction: 0.55, geometry: 0.3)));
    }

    [Fact]
    public void RejectsNearMissesOfDecorativeTypes()
    {
        Assert.False(VisualHintSupplement.IsAcceptable(
            Control(ControlType.DecorativeIcon, new Rectangle(0, 0, 24, 24), interaction: 0.55)));

        Assert.False(VisualHintSupplement.IsAcceptable(
            Control(ControlType.StaticLabel, new Rectangle(0, 0, 80, 18), interaction: 0.55)));
    }

    [Fact]
    public void RejectsBoxesWithoutAClickPoint()
    {
        var control = Control(
            ControlType.Button,
            new Rectangle(0, 0, 40, 20),
            interaction: 0.8,
            click: new Point(500, 500));

        Assert.False(VisualHintSupplement.IsAcceptable(control));
    }

    [Fact]
    public void MapsBoundsAndClickPointOntoTheCaptureOrigin()
    {
        var control = Control(
            ControlType.IconButton,
            new Rectangle(10, 20, 40, 20),
            interaction: 0.8,
            click: new Point(15, 25));

        var target = Assert.Single(AddOne(control, new Rectangle(100, 200, 300, 300)));

        Assert.Equal(new Rectangle(110, 220, 40, 20), target.Bounds);
        Assert.Equal(new Point(115, 225), target.HitPoint);
    }

    [Fact]
    public void SkipsAControlUiAutomationAlreadyCovers()
    {
        var existing = new[] { new ClickableTarget(new Rectangle(100, 200, 40, 20)) };
        var control = Control(ControlType.Button, new Rectangle(10, 10, 40, 20), interaction: 0.8);
        var result = new VisualDetectionResult([], [control], [1.0], 5);

        var merged = VisualHintSupplement.Add(result, new Rectangle(100, 200, 300, 300), existing, 400);

        Assert.Single(merged);
    }

    [Fact]
    public void SkipsAPixelBoxCoveringMostOfTheCapture()
    {
        // A panel or the page background: not a control, however many edges it has.
        var control = Control(ControlType.Card, new Rectangle(0, 0, 900, 700), interaction: 0.9, issues: GeometryIssue.None);

        Assert.Empty(AddOne(control, new Rectangle(0, 0, 1000, 800)));
    }

    [Fact]
    public void OverlapsTreatsNestedBoxesAsTheSameControl()
    {
        Assert.True(VisualHintSupplement.Overlaps(
            new Rectangle(10, 10, 100, 40),
            new Rectangle(20, 20, 20, 10)));

        Assert.True(VisualHintSupplement.Overlaps(
            new Rectangle(30, 25, 60, 30),
            new Rectangle(10, 10, 100, 40)));

        Assert.False(VisualHintSupplement.Overlaps(
            new Rectangle(10, 10, 100, 40),
            new Rectangle(200, 200, 40, 20)));
    }
}
