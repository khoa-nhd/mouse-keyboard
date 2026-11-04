using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

/// <summary>
/// Phase 6 coverage for duplicate suppression, global UIA/CV assignment,
/// composite ranking and final filtering on dense and conflicting inputs.
/// </summary>
public class TargetResolutionTests
{
    [Fact]
    public void Deduplicator_KeepsTheButtonInsteadOfItsOcrText()
    {
        VisualTarget button = Target(
            "uia-save",
            TargetSource.Uia,
            new Rectangle(40, 40, 120, 40),
            TargetType.Button,
            text: "Save",
            detection: 1.0,
            interaction: 1.0,
            geometry: 0.98,
            classification: 0.98);
        VisualTarget text = Target(
            "cv-text",
            TargetSource.CvOcr,
            new Rectangle(70, 50, 60, 18),
            TargetType.Text,
            text: "Save",
            detection: 0.99,
            interaction: 0.15,
            geometry: 0.95,
            classification: 0.50);

        IReadOnlyList<VisualTarget> deduplicated =
            TargetDeduplicator.SuppressDuplicates(new[] { text, button });

        VisualTarget result = Assert.Single(deduplicated);
        Assert.Equal("uia-save", result.Id);
        Assert.Equal(TargetType.Button, result.Type);

        VisualTarget finalized = Assert.Single(
            TargetPipeline.Finalize(new[] { text, button }));
        Assert.Equal("uia-save", finalized.Id);
    }

    [Fact]
    public void Deduplicator_KeepsTheButtonInsteadOfANestedIcon()
    {
        VisualTarget button = Target(
            "cv-button",
            TargetSource.CvContour,
            new Rectangle(20, 20, 100, 36),
            TargetType.Button,
            detection: 0.72,
            interaction: 0.65,
            geometry: 0.86,
            classification: 0.70);
        VisualTarget icon = Target(
            "cv-icon",
            TargetSource.CvContour,
            new Rectangle(62, 28, 16, 16),
            TargetType.Icon,
            detection: 0.74,
            interaction: 0.60,
            geometry: 0.82,
            classification: 0.60);

        IReadOnlyList<VisualTarget> finalized =
            TargetPipeline.Finalize(new[] { icon, button });

        VisualTarget result = Assert.Single(finalized);
        Assert.Equal("cv-button", result.Id);
        Assert.Equal(TargetType.Button, result.Type);
    }

    [Fact]
    public void Deduplicator_CollapsesMultipleContoursAndScalesOfOneControl()
    {
        VisualTarget coarse = Target(
            "cv-coarse",
            TargetSource.CvContour,
            new Rectangle(50, 50, 104, 38),
            TargetType.Button,
            detection: 0.58,
            interaction: 0.55,
            geometry: 0.72,
            classification: 0.64);
        VisualTarget native = Target(
            "cv-native",
            TargetSource.CvContour,
            new Rectangle(51, 51, 100, 36),
            TargetType.Button,
            detection: 0.66,
            interaction: 0.61,
            geometry: 0.84,
            classification: 0.70);
        VisualTarget refined = Target(
            "cv-refined",
            TargetSource.CvContour,
            new Rectangle(52, 52, 98, 34),
            TargetType.Button,
            detection: 0.70,
            interaction: 0.64,
            geometry: 0.88,
            classification: 0.72);

        IReadOnlyList<VisualTarget> deduplicated =
            TargetDeduplicator.SuppressDuplicates(
                new[] { coarse, refined, native });

        VisualTarget result = Assert.Single(deduplicated);
        Assert.Equal("cv-refined", result.Id);
    }

    [Fact]
    public void Deduplicator_DoesNotMergeAdjacentButtons()
    {
        VisualTarget left = Target(
            "cv-left",
            TargetSource.CvContour,
            new Rectangle(10, 10, 100, 40),
            TargetType.Button,
            detection: 0.70,
            interaction: 0.65,
            geometry: 0.86,
            classification: 0.70);
        VisualTarget right = Target(
            "cv-right",
            TargetSource.CvContour,
            new Rectangle(112, 10, 100, 40),
            TargetType.Button,
            detection: 0.70,
            interaction: 0.65,
            geometry: 0.86,
            classification: 0.70);

        IReadOnlyList<VisualTarget> finalized =
            TargetPipeline.Finalize(new[] { left, right });

        Assert.Equal(2, finalized.Count);
        Assert.All(finalized, target => Assert.True(target.Bounds.Contains(target.ClickPoint)));
    }

    [Fact]
    public void RepeatedIdenticalIcons_StaySeparateTargets()
    {
        var iconA = Target(
            "cv-icon-a",
            TargetSource.CvContour,
            new Rectangle(10, 10, 16, 16),
            TargetType.Icon,
            detection: 0.68,
            interaction: 0.58,
            geometry: 0.84,
            classification: 0.60);
        var iconB = Target(
            "cv-icon-b",
            TargetSource.CvContour,
            new Rectangle(30, 10, 16, 16),
            TargetType.Icon,
            detection: 0.68,
            interaction: 0.58,
            geometry: 0.84,
            classification: 0.60);

        IReadOnlyList<VisualTarget> finalized =
            TargetPipeline.Finalize(new[] { iconA, iconB });

        Assert.Equal(2, finalized.Count);
    }

    [Fact]
    public void Fusion_MergesSameControlAndKeepsBothMetadataSets()
    {
        var uiaMetadata = new UiaTargetMetadata(
            AutomationId: "save-button",
            RuntimeId: "42.1",
            ControlType: "Button",
            ClassName: "Button",
            SupportsInvoke: true,
            SourceBounds: new Rectangle(20, 20, 120, 40),
            SupportedPatterns: UiaPatternFlags.Invoke);
        var cvMetadata = new CvTargetMetadata(
            OcrText: "Save",
            OcrConfidence: 0.93,
            FrameId: 3,
            Detector: "ocr",
            SourceBounds: new Rectangle(18, 18, 124, 44));

        VisualTarget uia = Target(
            "uia-save",
            TargetSource.Uia,
            new Rectangle(20, 20, 120, 40),
            TargetType.Button,
            text: "Save",
            detection: 1.0,
            interaction: 1.0,
            geometry: 0.98,
            classification: 0.98,
            metadata: new TargetMetadata { Uia = uiaMetadata });
        VisualTarget cv = Target(
            "cv-save",
            TargetSource.CvOcr,
            new Rectangle(18, 18, 124, 44),
            TargetType.Button,
            text: "Save",
            detection: 0.76,
            interaction: 0.66,
            geometry: 0.90,
            classification: 0.66,
            metadata: new TargetMetadata { Cv = cvMetadata });

        HintScanResult result = new TargetFusionEngine().Fuse(
            new[] { uia },
            new[] { cv });

        VisualTarget merged = Assert.Single(result.Targets);
        Assert.True(merged.Source.HasFlag(TargetSource.Uia));
        Assert.True(merged.Source.HasFlag(TargetSource.Cv));
        Assert.Equal("save-button", merged.Metadata.Uia?.AutomationId);
        Assert.True(merged.Metadata.Uia?.SupportsInvoke);
        Assert.Equal("Save", merged.Metadata.Cv?.OcrText);
        Assert.Equal(0.93, merged.Metadata.Cv?.OcrConfidence);
        Assert.Equal("ocr", merged.Metadata.Cv?.Detector);
        Assert.Equal(TargetType.Button, merged.Type);
        Assert.Equal(1.0, merged.InteractionConfidence);
    }

    [Fact]
    public void Fusion_DoesNotMergeAnAdjacentControl()
    {
        VisualTarget save = Target(
            "uia-save",
            TargetSource.Uia,
            new Rectangle(0, 0, 100, 40),
            TargetType.Button,
            text: "Save",
            detection: 1.0,
            interaction: 1.0,
            geometry: 0.98,
            classification: 0.98);
        VisualTarget cancel = Target(
            "uia-cancel",
            TargetSource.Uia,
            new Rectangle(104, 0, 100, 40),
            TargetType.Button,
            text: "Cancel",
            detection: 1.0,
            interaction: 1.0,
            geometry: 0.98,
            classification: 0.98);
        VisualTarget cv = Target(
            "cv-save",
            TargetSource.CvContour,
            new Rectangle(2, 2, 96, 36),
            TargetType.Button,
            detection: 0.72,
            interaction: 0.65,
            geometry: 0.86,
            classification: 0.70);

        HintScanResult result = new TargetFusionEngine().Fuse(
            new[] { save, cancel },
            new[] { cv });

        Assert.Equal(2, result.Targets.Count);
        VisualTarget fused = Assert.Single(
            result.Targets.Where(target => target.Bounds.Left < 50));
        Assert.True(fused.Source.HasFlag(TargetSource.Cv));
        Assert.Equal("Save", fused.Text);

        VisualTarget untouched = Assert.Single(
            result.Targets.Where(target => target.Bounds.Left >= 50));
        Assert.False(untouched.Source.HasFlag(TargetSource.Cv));
        Assert.Equal("Cancel", untouched.Text);
    }

    [Fact]
    public void Fusion_KeepsValidUiaOnlyAndCvOnlyTargets()
    {
        VisualTarget uia = Target(
            "uia-save",
            TargetSource.Uia,
            new Rectangle(0, 0, 80, 30),
            TargetType.Button,
            text: "Save",
            detection: 1.0,
            interaction: 1.0,
            geometry: 0.98,
            classification: 0.98);
        VisualTarget cv = Target(
            "cv-icon",
            TargetSource.CvContour,
            new Rectangle(300, 300, 24, 24),
            TargetType.Icon,
            detection: 0.70,
            interaction: 0.60,
            geometry: 0.84,
            classification: 0.62);

        HintScanResult result = new TargetFusionEngine().Fuse(
            new[] { uia },
            new[] { cv });

        Assert.Equal(2, result.Targets.Count);
        Assert.Contains(result.Targets, target => target.Id == "uia-save");
        Assert.Contains(result.Targets, target => target.Id == "cv-icon");
    }

    [Fact]
    public void Ranking_HighOcrConfidenceTextRanksBelowARealButton()
    {
        VisualTarget text = Target(
            "cv-text",
            TargetSource.CvOcr,
            new Rectangle(10, 10, 180, 28),
            TargetType.Text,
            text: "Settings",
            detection: 0.99,
            interaction: 0.15,
            geometry: 0.96,
            classification: 0.55);
        VisualTarget button = Target(
            "cv-button",
            TargetSource.CvContour,
            new Rectangle(10, 60, 100, 32),
            TargetType.Button,
            text: "Apply",
            detection: 0.70,
            interaction: 0.75,
            geometry: 0.80,
            classification: 0.75);

        TargetRanking textRanking = TargetRanker.Evaluate(text);
        TargetRanking buttonRanking = TargetRanker.Evaluate(button);

        Assert.True(textRanking.Ambiguity > buttonRanking.Ambiguity);
        Assert.True(textRanking.Score < buttonRanking.Score);
    }

    [Fact]
    public void FinalFilter_DropsUnsupportedTextTinyAndOversizedCvTargets()
    {
        VisualTarget uia = Target(
            "uia-button",
            TargetSource.Uia,
            new Rectangle(10, 10, 80, 30),
            TargetType.Button,
            text: "Open",
            detection: 0.45,
            interaction: 0.45,
            geometry: 0.35,
            classification: 0.98,
            issues: GeometryIssue.WeakEdges);
        VisualTarget highConfidenceText = Target(
            "cv-text",
            TargetSource.CvOcr,
            new Rectangle(120, 10, 180, 26),
            TargetType.Text,
            text: "Unclickable heading",
            detection: 0.99,
            interaction: 0.20,
            geometry: 0.96,
            classification: 0.55);
        VisualTarget tiny = Target(
            "cv-tiny",
            TargetSource.CvContour,
            new Rectangle(10, 100, 2, 2),
            TargetType.Button,
            detection: 0.90,
            interaction: 0.80,
            geometry: 0.90,
            classification: 0.70);
        VisualTarget oversized = Target(
            "cv-huge",
            TargetSource.CvContour,
            new Rectangle(0, 150, 3000, 900),
            TargetType.Button,
            detection: 0.90,
            interaction: 0.80,
            geometry: 0.90,
            classification: 0.70);
        VisualTarget validCv = Target(
            "cv-button",
            TargetSource.CvContour,
            new Rectangle(150, 120, 100, 30),
            TargetType.Button,
            detection: 0.70,
            interaction: 0.70,
            geometry: 0.88,
            classification: 0.72);

        IReadOnlyList<VisualTarget> finalized = TargetPipeline.Finalize(
            new[] { uia, highConfidenceText, tiny, oversized, validCv });

        Assert.Equal(2, finalized.Count);
        Assert.Contains(finalized, target => target.Id == "uia-button");
        Assert.Contains(finalized, target => target.Id == "cv-button");
        Assert.DoesNotContain(finalized, target => target.Id == "cv-text");
        Assert.DoesNotContain(finalized, target => target.Id == "cv-tiny");
        Assert.DoesNotContain(finalized, target => target.Id == "cv-huge");
    }

    [Fact]
    public void Selection_RemovesAContainerButKeepsItsRealChild()
    {
        VisualTarget container = Target(
            "uia-pane",
            TargetSource.Uia,
            new Rectangle(0, 0, 400, 300),
            TargetType.Unknown,
            detection: 1.0,
            interaction: 0.8,
            geometry: 0.90,
            classification: 0.50);
        VisualTarget button = Target(
            "cv-button",
            TargetSource.CvContour,
            new Rectangle(50, 50, 100, 30),
            TargetType.Button,
            detection: 0.72,
            interaction: 0.68,
            geometry: 0.88,
            classification: 0.72);

        IReadOnlyList<VisualTarget> finalized =
            TargetPipeline.Finalize(new[] { container, button });

        Assert.Single(finalized);
        Assert.Equal("cv-button", finalized[0].Id);
    }

    [Fact]
    public void Selection_KeepsDistinctNestedControls()
    {
        VisualTarget row = Target(
            "uia-row",
            TargetSource.Uia,
            new Rectangle(0, 0, 300, 40),
            TargetType.ListItem,
            text: "Row",
            detection: 1.0,
            interaction: 0.9,
            geometry: 0.98,
            classification: 0.98);
        VisualTarget checkbox = Target(
            "cv-checkbox",
            TargetSource.CvContour,
            new Rectangle(10, 10, 20, 20),
            TargetType.CheckBox,
            detection: 0.70,
            interaction: 0.70,
            geometry: 0.86,
            classification: 0.66);

        IReadOnlyList<VisualTarget> finalized =
            TargetPipeline.Finalize(new[] { row, checkbox });

        Assert.Equal(2, finalized.Count);
        Assert.Contains(finalized, target => target.Id == "uia-row");
        Assert.Contains(finalized, target => target.Id == "cv-checkbox");
    }

    [Fact]
    public void Selection_KeepsANestedActionButtonInsideARow()
    {
        VisualTarget row = Target(
            "uia-row",
            TargetSource.Uia,
            new Rectangle(0, 0, 300, 40),
            TargetType.ListItem,
            text: "Message row",
            detection: 1.0,
            interaction: 0.9,
            geometry: 0.98,
            classification: 0.98);
        VisualTarget action = Target(
            "uia-delete",
            TargetSource.Uia,
            new Rectangle(70, 2, 220, 36),
            TargetType.Button,
            text: "Delete",
            detection: 1.0,
            interaction: 1.0,
            geometry: 0.98,
            classification: 0.98);

        IReadOnlyList<VisualTarget> finalized =
            TargetPipeline.Finalize(new[] { row, action });

        Assert.Equal(2, finalized.Count);
        Assert.Contains(finalized, target => target.Id == "uia-row");
        Assert.Contains(finalized, target => target.Id == "uia-delete");
    }

    [Fact]
    public void Assignment_ChoosesTheGlobalOptimumInsteadOfTheFirstGreedyEdge()
    {
        var scores = new double[,]
        {
            { 0.82, 0.79 },
            { 0.80, 0.10 }
        };

        int[] assignment = TargetAssignment.Assign(scores, allowUnmatched: false);

        Assert.Equal(1, assignment[0]);
        Assert.Equal(0, assignment[1]);
    }

    [Fact]
    public void Fusion_DenseInterface_CollapsesDuplicatesWithoutLosingControls()
    {
        var uiaTargets = new List<VisualTarget>();
        var cvTargets = new List<VisualTarget>();

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                int x = 10 + (column * 90);
                int y = 10 + (row * 45);
                string text = $"Button {row}-{column}";
                var bounds = new Rectangle(x, y, 80, 30);

                uiaTargets.Add(Target(
                    $"uia-{row}-{column}",
                    TargetSource.Uia,
                    bounds,
                    TargetType.Button,
                    text: text,
                    detection: 1.0,
                    interaction: 1.0,
                    geometry: 0.98,
                    classification: 0.98));
                cvTargets.Add(Target(
                    $"cv-{row}-{column}",
                    TargetSource.CvContour,
                    new Rectangle(x + 1, y + 1, 78, 28),
                    TargetType.Button,
                    text: text,
                    detection: 0.74,
                    interaction: 0.68,
                    geometry: 0.88,
                    classification: 0.72));
                cvTargets.Add(Target(
                    $"cv-text-{row}-{column}",
                    TargetSource.CvOcr,
                    new Rectangle(x + 12, y + 7, 56, 16),
                    TargetType.Text,
                    text: text,
                    detection: 0.99,
                    interaction: 0.15,
                    geometry: 0.94,
                    classification: 0.55));
            }
        }

        HintScanResult result = new TargetFusionEngine().Fuse(
            uiaTargets,
            cvTargets);

        Assert.Equal(20, result.Targets.Count);
        Assert.All(result.Targets, target =>
        {
            Assert.True(target.Source.HasFlag(TargetSource.Uia));
            Assert.True(target.Bounds.Contains(target.ClickPoint));
        });
    }

    private static VisualTarget Target(
        string id,
        TargetSource source,
        Rectangle bounds,
        TargetType type,
        string text = "",
        double detection = 0.8,
        double interaction = 0.8,
        double geometry = 0.9,
        double classification = 0.8,
        GeometryIssue issues = GeometryIssue.None,
        TargetMetadata? metadata = null)
    {
        Point clickPoint = new(
            bounds.Left + (bounds.Width / 2),
            bounds.Top + (bounds.Height / 2));

        return new VisualTarget(
            id,
            source,
            new TargetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            clickPoint,
            type,
            text,
            detection,
            interaction,
            isEnabled: true,
            isVisible: true,
            metadata: metadata,
            geometry: new TargetGeometry
            {
                Confidence = geometry,
                Issues = issues
            },
            classificationConfidence: classification);
    }
}
