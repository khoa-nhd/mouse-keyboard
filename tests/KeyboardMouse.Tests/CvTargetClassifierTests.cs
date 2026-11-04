using System.Drawing;
using KeyboardMouse.Capture;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class CvTargetClassifierTests
{
    [Fact]
    public void OutlinedButton_IsAcceptedAsAButton()
    {
        CvClassification result = Assert.IsType<CvClassification>(Classify(
            new Rectangle(40, 40, 90, 32),
            perimeter: 0.55,
            interior: 0.04,
            variation: 4));

        Assert.Equal(TargetType.Button, result.Type);
        Assert.False(result.IsTextLike);
        Assert.InRange(result.DetectionConfidence, 0.35, 0.92);
        Assert.True(result.InteractionConfidence > 0);
    }

    [Fact]
    public void FilledButtonWithALabel_IsNotMistakenForPlainText()
    {
        CvClassification result = Assert.IsType<CvClassification>(Classify(
            new Rectangle(40, 40, 110, 36),
            perimeter: 0.45,
            interior: 0.22,
            variation: 25));

        Assert.Equal(TargetType.Button, result.Type);
        Assert.False(result.IsTextLike);
    }

    [Fact]
    public void CompactInkHeavyRegion_IsAnIcon()
    {
        CvClassification result = Assert.IsType<CvClassification>(Classify(
            new Rectangle(40, 40, 24, 24),
            perimeter: 0.30,
            interior: 0.28,
            variation: 30));

        Assert.Equal(TargetType.Icon, result.Type);
        Assert.True(
            result.InteractionConfidence < result.DetectionConfidence,
            "Icons should be ranked lower without being discarded.");
    }

    [Fact]
    public void HollowSquare_IsACheckbox()
    {
        CvClassification result = Assert.IsType<CvClassification>(Classify(
            new Rectangle(40, 40, 18, 18),
            perimeter: 0.35,
            interior: 0.10,
            variation: 12));

        Assert.Equal(TargetType.CheckBox, result.Type);
    }

    [Fact]
    public void WideMostlyEmptyBox_IsAnEditField()
    {
        CvClassification result = Assert.IsType<CvClassification>(Classify(
            new Rectangle(40, 40, 200, 28),
            perimeter: 0.40,
            interior: 0.05,
            variation: 6));

        Assert.Equal(TargetType.Edit, result.Type);
    }

    [Theory]
    [InlineData(180, 20, 0.04, 0.30, 25)] // long single-line label
    [InlineData(60, 18, 0.05, 0.25, 18)]  // short single-line label
    public void BorderlessText_IsRejected(
        int width,
        int height,
        double perimeter,
        double interior,
        double variation)
    {
        Assert.Null(Classify(
            new Rectangle(40, 40, width, height),
            perimeter,
            interior,
            variation));
    }

    [Fact]
    public void FramedButBlankPanel_IsAContainer()
    {
        Assert.Null(Classify(
            new Rectangle(40, 40, 300, 120),
            perimeter: 0.50,
            interior: 0.02,
            variation: 3));
    }

    [Fact]
    public void LargeDenseRegion_IsAContainer()
    {
        Assert.Null(Classify(
            new Rectangle(40, 40, 600, 400),
            perimeter: 0.40,
            interior: 0.20,
            variation: 35));
    }

    [Fact]
    public void OneDimensionalDecoration_IsASeparator()
    {
        Assert.Null(Classify(
            new Rectangle(40, 40, 300, 8),
            perimeter: 0.60,
            interior: 0.10,
            variation: 8));
    }

    [Fact]
    public void TinyRegion_IsRejected()
    {
        Assert.Null(Classify(
            new Rectangle(40, 40, 7, 5),
            perimeter: 0.60,
            interior: 0.30,
            variation: 20));
    }

    [Fact]
    public void FlatRegionWithoutAnyStructure_IsRejected()
    {
        Assert.Null(Classify(
            new Rectangle(40, 40, 60, 24),
            perimeter: 0.02,
            interior: 0.03,
            variation: 1));
    }

    private static CvClassification? Classify(
        Rectangle bounds,
        double perimeter,
        double interior,
        double variation) =>
        CvTargetClassifier.Classify(
            new CvFeatures(bounds, perimeter, interior, variation));
}
