using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class TargetGeometryPipelineTests
{
    [Fact]
    public void ExplicitClickPoint_SurvivesFiltering()
    {
        var monitor = new MonitorInfo(
            new IntPtr(1),
            new Rectangle(0, 0, 100, 100),
            new Rectangle(0, 0, 100, 100),
            1.0);

        var raw = new[]
        {
            new RawClickableTarget(
                new Rectangle(10, 10, 40, 20),
                IsOffscreen: false,
                IsEnabled: true,
                ClickPoint: new Point(15, 15))
        };

        ClickableTarget target = Assert.Single(
            HintTargetFilter.Apply(raw, new[] { monitor }, maxElements: 100));

        Assert.Equal(new Point(15, 15), target.ClickPoint);
    }

    [Fact]
    public void ClippedTarget_ClampsTheExplicitPointIntoTheVisibleBounds()
    {
        var monitor = new MonitorInfo(
            new IntPtr(1),
            new Rectangle(0, 0, 100, 100),
            new Rectangle(0, 0, 100, 100),
            1.0);

        var raw = new[]
        {
            new RawClickableTarget(
                new Rectangle(-10, 10, 40, 20),
                IsOffscreen: false,
                IsEnabled: true,
                ClickPoint: new Point(-5, 20))
        };

        ClickableTarget target = Assert.Single(
            HintTargetFilter.Apply(raw, new[] { monitor }, maxElements: 100));

        Assert.Equal(new Rectangle(0, 10, 30, 20), target.Bounds);
        Assert.Equal(new Point(0, 20), target.ClickPoint);
        Assert.True(target.Geometry!.IsClipped);
    }

    [Fact]
    public void Selection_PrefersAClearChildControlOverItsContainer()
    {
        var container = new VisualTarget(
            "uia-pane",
            TargetSource.Uia,
            new TargetBounds(0, 0, 400, 300),
            new Point(200, 150),
            TargetType.Unknown,
            geometry: new TargetGeometry { Confidence = 0.90 });

        var button = new VisualTarget(
            "cv-button",
            TargetSource.CvContour,
            new TargetBounds(50, 50, 100, 30),
            new Point(100, 65),
            TargetType.Button,
            geometry: new TargetGeometry { Confidence = 0.82 });

        IReadOnlyList<VisualTarget> selected =
            TargetGeometrySelection.RemoveContainedContainers(new[] { container, button });

        VisualTarget result = Assert.Single(selected);
        Assert.Equal("cv-button", result.Id);
        Assert.Equal(TargetType.Button, result.Type);
    }

    [Fact]
    public void Selection_DoesNotReplaceARealButtonWithNestedText()
    {
        var button = new VisualTarget(
            "uia-button",
            TargetSource.Uia,
            new TargetBounds(40, 40, 120, 40),
            new Point(100, 60),
            TargetType.Button,
            text: "Save",
            geometry: new TargetGeometry { Confidence = 0.98 });

        var text = new VisualTarget(
            "cv-text",
            TargetSource.CvContour,
            new TargetBounds(70, 50, 40, 15),
            new Point(90, 57),
            TargetType.Text,
            geometry: new TargetGeometry { Confidence = 0.60 });

        IReadOnlyList<VisualTarget> selected =
            TargetGeometrySelection.RemoveContainedContainers(new[] { button, text });

        VisualTarget result = Assert.Single(selected);
        Assert.Equal("uia-button", result.Id);
        Assert.Equal("Save", result.Text);
    }

    [Fact]
    public void Fusion_UsesCvControlBounds_WhenUiaOnlyFoundAContainer()
    {
        var uia = new VisualTarget(
            "uia-pane",
            TargetSource.Uia,
            new TargetBounds(100, 100, 400, 300),
            new Point(300, 250),
            TargetType.Unknown,
            geometry: new TargetGeometry { Confidence = 0.88 });

        var cv = new VisualTarget(
            "cv-button",
            TargetSource.CvContour,
            new TargetBounds(150, 150, 100, 30),
            new Point(160, 165),
            TargetType.Button,
            text: "OK",
            geometry: new TargetGeometry
            {
                Confidence = 0.84,
                ClickStrategy = ClickPointStrategy.SafeInterior
            });

        var fusion = new TargetFusionEngine { MatchThreshold = 0.30 };
        HintScanResult result = fusion.Fuse(new[] { uia }, new[] { cv });

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal(new TargetBounds(150, 150, 100, 30), target.Bounds);
        Assert.Equal(new Point(160, 165), target.ClickPoint);
        Assert.Equal(TargetType.Button, target.Type);
        Assert.True(target.Source.HasFlag(TargetSource.Uia));
        Assert.True(target.Source.HasFlag(TargetSource.Cv));
    }

    [Fact]
    public void Fusion_KeepsUiaButtonBounds_ForACvTextChild()
    {
        var uia = new VisualTarget(
            "uia-button",
            TargetSource.Uia,
            new TargetBounds(40, 40, 120, 40),
            new Point(100, 60),
            TargetType.Button,
            text: "Save",
            geometry: new TargetGeometry { Confidence = 0.98 });

        var cv = new VisualTarget(
            "cv-text",
            TargetSource.CvContour,
            new TargetBounds(70, 50, 40, 15),
            new Point(90, 57),
            TargetType.Text,
            text: "Save",
            geometry: new TargetGeometry { Confidence = 0.70 });

        var fusion = new TargetFusionEngine { MatchThreshold = 0.30 };
        HintScanResult result = fusion.Fuse(new[] { uia }, new[] { cv });

        VisualTarget target = Assert.Single(result.Targets);
        Assert.Equal(new TargetBounds(40, 40, 120, 40), target.Bounds);
        Assert.Equal(new Point(100, 60), target.ClickPoint);
        Assert.Equal("Save", target.Text);
    }

    [Fact]
    public void GeometryConfidence_LowersTheRankingOfAnUncertainTarget()
    {
        var clear = new VisualTarget(
            "clear",
            TargetSource.CvContour,
            new TargetBounds(0, 0, 100, 30),
            new Point(50, 15),
            TargetType.Button,
            detectionConfidence: 0.80,
            geometry: new TargetGeometry { Confidence = 0.95 });

        var uncertain = new VisualTarget(
            "uncertain",
            TargetSource.CvContour,
            new TargetBounds(0, 0, 100, 30),
            new Point(50, 15),
            TargetType.Button,
            detectionConfidence: 0.80,
            geometry: new TargetGeometry
            {
                Confidence = 0.30,
                Issues = GeometryIssue.WeakEdges
            });

        Assert.True(clear.RankingConfidence > uncertain.RankingConfidence);
    }
}
