using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class VisualTargetTests
{
    private static readonly TargetBounds Bounds = new(100, 200, 300, 100);

    private static VisualTarget CreateTarget(
        TargetSource source = TargetSource.Uia,
        TargetBounds? bounds = null,
        Point? clickPoint = null,
        string? text = "Save",
        double detectionConfidence = 0.9,
        double interactionConfidence = 0.8,
        bool isEnabled = true,
        bool isVisible = true,
        TargetMetadata? metadata = null) =>
        new(
            "target-1",
            source,
            bounds ?? Bounds,
            clickPoint ?? new Point(150, 250),
            TargetType.Button,
            text,
            detectionConfidence,
            interactionConfidence,
            isEnabled,
            isVisible,
            metadata);

    [Fact]
    public void ExposesSharedTargetFields()
    {
        var target = CreateTarget();

        Assert.Equal("target-1", target.Id);
        Assert.Equal(TargetSource.Uia, target.Source);
        Assert.Equal(TargetType.Button, target.Type);
        Assert.Equal("Save", target.Text);
        Assert.Equal(Bounds, target.Bounds);
        Assert.Equal(new Point(150, 250), target.ClickPoint);
        Assert.Equal(0.9, target.DetectionConfidence);
        Assert.Equal(0.8, target.InteractionConfidence);
        Assert.True(target.IsEnabled);
        Assert.True(target.IsVisible);
        Assert.True(target.IsActionable);
    }

    [Fact]
    public void AllowsEmptyTextAndNormalizesNull()
    {
        Assert.Equal(string.Empty, CreateTarget(text: "").Text);
        Assert.Equal(string.Empty, CreateTarget(text: null).Text);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    [InlineData(10, -5)]
    public void RejectsInvalidBounds(int width, int height)
    {
        var bounds = new TargetBounds(10, 10, width, height);

        Assert.False(bounds.IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTarget(bounds: bounds, clickPoint: new Point(10, 10)));
    }

    [Theory]
    [InlineData(99, 250)]
    [InlineData(400, 250)]
    [InlineData(150, 199)]
    [InlineData(150, 300)]
    public void RejectsClickPointOutsideBounds(int x, int y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTarget(clickPoint: new Point(x, y)));
    }

    [Fact]
    public void AcceptsClickPointOnTopLeftEdgeAndInside()
    {
        Assert.Equal(
            new Point(100, 200),
            CreateTarget(clickPoint: new Point(100, 200)).ClickPoint);

        Assert.Equal(
            new Point(399, 299),
            CreateTarget(clickPoint: new Point(399, 299)).ClickPoint);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsDetectionConfidenceOutsideUnitRange(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTarget(detectionConfidence: value));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsInteractionConfidenceOutsideUnitRange(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTarget(interactionConfidence: value));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void AcceptsConfidenceBoundaries(double value)
    {
        var target = CreateTarget(
            detectionConfidence: value,
            interactionConfidence: value);

        Assert.Equal(value, target.DetectionConfidence);
        Assert.Equal(value, target.InteractionConfidence);
    }

    [Fact]
    public void CombinesTargetSourceFlags()
    {
        var target = CreateTarget(source: TargetSource.Uia | TargetSource.CvOcr);

        Assert.True(target.Source.HasFlag(TargetSource.Uia));
        Assert.True(target.Source.HasFlag(TargetSource.CvOcr));
        Assert.False(target.Source.HasFlag(TargetSource.CvContour));
        Assert.Equal(
            TargetSource.CvOcr | TargetSource.CvContour,
            TargetSource.Cv);
        Assert.Equal(TargetSource.Uia | TargetSource.Cv, TargetSource.All);
    }

    [Fact]
    public void RejectsNoSourceOrUnknownSourceBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTarget(source: TargetSource.None));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateTarget(source: (TargetSource)8));
    }

    [Fact]
    public void PreservesOptionalUiaAndCvMetadata()
    {
        var metadata = new TargetMetadata
        {
            Uia = new UiaTargetMetadata(
                AutomationId: "save-button",
                RuntimeId: "42.1",
                ControlType: "Button",
                ClassName: "Button",
                SupportsInvoke: true,
                SourceBounds: new Rectangle(100, 200, 300, 100)),
            Cv = new CvTargetMetadata(
                OcrText: "Save",
                OcrConfidence: 0.92,
                FrameId: 7,
                Detector: "ocr",
                SourceBounds: new Rectangle(110, 210, 80, 20))
        };

        var target = CreateTarget(metadata: metadata);

        Assert.Same(metadata, target.Metadata);
        Assert.Equal("save-button", target.Metadata.Uia?.AutomationId);
        Assert.Equal("42.1", target.Metadata.Uia?.RuntimeId);
        Assert.True(target.Metadata.Uia?.SupportsInvoke);
        Assert.Equal("Save", target.Metadata.Cv?.OcrText);
        Assert.Equal(0.92, target.Metadata.Cv?.OcrConfidence);
        Assert.Equal(7, target.Metadata.Cv?.FrameId);
    }

    [Fact]
    public void DefaultsToEmptyMetadata()
    {
        var target = CreateTarget();

        Assert.True(TargetMetadata.Empty.IsEmpty);
        Assert.Same(TargetMetadata.Empty, target.Metadata);
        Assert.Null(target.Metadata.Uia);
        Assert.Null(target.Metadata.Cv);
    }

    [Fact]
    public void RepresentsDisabledAndInvisibleTargets()
    {
        var target = CreateTarget(isEnabled: false, isVisible: false);

        Assert.False(target.IsEnabled);
        Assert.False(target.IsVisible);
        Assert.False(target.IsActionable);
    }

    [Fact]
    public void ToStringDoesNotExposeTargetText()
    {
        var target = CreateTarget(text: "Secret label");

        Assert.DoesNotContain("Secret label", target.ToString());
        Assert.Contains(target.Id, target.ToString());
    }

    [Fact]
    public void TargetBoundsProvidesGeometryHelpers()
    {
        var bounds = new TargetBounds(10, 20, 30, 40);

        Assert.Equal(10, bounds.Left);
        Assert.Equal(20, bounds.Top);
        Assert.Equal(40, bounds.Right);
        Assert.Equal(60, bounds.Bottom);
        Assert.Equal(new Point(25, 40), bounds.Center);
        Assert.True(bounds.Contains(new Point(10, 20)));
        Assert.False(bounds.Contains(new Point(40, 60)));
        Assert.Equal(
            new TargetBounds(20, 30, 20, 30),
            bounds.Intersect(new TargetBounds(20, 30, 100, 100)));
        Assert.Equal(
            TargetBounds.Empty,
            bounds.Intersect(new TargetBounds(100, 100, 10, 10)));
    }
}
