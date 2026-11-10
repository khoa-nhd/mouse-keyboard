using System.Drawing;
using KeyboardMouse.Vision;
using Xunit.Abstractions;

namespace KeyboardMouse.Tests;

/// <summary>
/// Behavioural tests for the pixel layer. Thresholds are the values measured on
/// the synthetic fixtures, not guesses: every case currently scores 1.00/1.00,
/// so 0.95 leaves room for a single antialiasing change without hiding a
/// regression. Matching is centre-in-truth because the click point is what the
/// hint needs, not an exact box.
/// </summary>
public class VisualDetectionTests
{
    private const double MinRecall = 0.95;
    private const double MinPrecision = 0.95;

    private readonly ITestOutputHelper _output;

    public VisualDetectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly (string Name, Image32 Image, Rectangle[] Truth)[] Cases = BuildCases();

    private static (string, Image32, Rectangle[])[] BuildCases()
    {
        var toolbar = SyntheticUi.Toolbar(out var toolbarTruth);
        var dialog = SyntheticUi.DialogButtons(out var dialogTruth);
        var checkboxes = SyntheticUi.Checkboxes(out var checkboxTruth);
        var radios = SyntheticUi.Radios(out var radioTruth);
        var link = SyntheticUi.Link(out var linkTruth);
        var small = SyntheticUi.SmallControls(out var smallTruth);
        var segmented = SyntheticUi.Segmented(out var segmentedTruth);

        return
        [
            ("toolbar", toolbar, toolbarTruth),
            ("dialog", dialog, dialogTruth),
            ("checkboxes", checkboxes, checkboxTruth),
            ("radios", radios, radioTruth),
            ("link", link, linkTruth),
            ("small", small, smallTruth),
            ("segmented", segmented, segmentedTruth)
        ];
    }

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();

        foreach (var (name, _, _) in Cases)
        {
            data.Add(name);
        }

        return data;
    }

    private static Image32 Image(string name) => Cases.First(c => c.Name == name).Image;

    private static Rectangle[] Truth(string name) => Cases.First(c => c.Name == name).Truth;

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_meets_recall_and_precision(string name)
    {
        var image = Image(name);
        var truth = Truth(name);

        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        // Measured on the surface the hint actually offers (typed, interactive
        // controls). The raw shape list is an intermediate that the classifier is
        // allowed to reject — a decorative ring counts as a detected shape but not
        // as a target.
        var offered = result.Interactive;
        Report(name, image, result, truth);

        Assert.True(
            Metrics.Recall(offered, truth) >= MinRecall,
            $"recall below {MinRecall}: {Metrics.Recall(offered, truth):0.00}");
        Assert.True(
            Metrics.Precision(offered, truth) >= MinPrecision,
            $"precision below {MinPrecision}: {Metrics.Precision(offered, truth):0.00}");
    }

    [Fact]
    public void Multi_scale_never_costs_recall()
    {
        var image = Image("radios");
        var truth = Truth("radios");

        // Same detector set, same cost ceiling, only the extra scale differs.
        var single = VisualPipeline.Detect(image, new VisualDetectionOptions
        {
            MultiScale = false,
            RefineSmallControls = false
        });

        var multi = VisualPipeline.Detect(image, new VisualDetectionOptions
        {
            RefineSmallControls = false
        });

        double singleRecall = Metrics.Recall(single.Interactive, truth);
        double multiRecall = Metrics.Recall(multi.Interactive, truth);

        _output.WriteLine(
            $"single scales=[{string.Join(",", single.Scales)}] recall={singleRecall:0.00} | " +
            $"multi scales=[{string.Join(",", multi.Scales)}] recall={multiRecall:0.00}");

        Assert.Contains(2.0, multi.Scales);
        Assert.True(multiRecall >= singleRecall, "the extra scale must not lose targets");
        Assert.True(multiRecall >= MinRecall);
    }

    [Fact]
    public void Large_capture_stays_inside_the_pixel_budget()
    {
        // A 1920x1080 capture at native resolution would be 2.07 M px per pass.
        var screen = SyntheticUi.SmallControls(out _).Resize(1920, 1080);
        var budget = new VisualDetectionOptions();

        var result = VisualPipeline.Detect(screen, budget);

        long largest = result.Scales
            .Select(scale => (long)Math.Round(screen.Width * scale) * (long)Math.Round(screen.Height * scale))
            .DefaultIfEmpty(0)
            .Max();

        _output.WriteLine(
            $"scales=[{string.Join(",", result.Scales)}] largest={largest} px " +
            $"elapsed={result.ElapsedMilliseconds} ms candidates={result.Candidates.Count}");

        Assert.All(result.Scales, scale => Assert.True(scale <= 1.0));
        Assert.True(largest <= budget.MaxProcessingPixels, $"processed {largest} px over budget");
        Assert.True(result.ElapsedMilliseconds < 3_000, $"took {result.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Adjacent_buttons_are_not_merged()
    {
        VisualCandidate Button(int left) => new(
            new Rectangle(left, 0, 40, 30),
            VisualShape.RoundedRectangle,
            VisualDetector.FillRegion,
            0.8);

        var merged = VisualMerger.Merge([Button(0), Button(40)]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Duplicate_boxes_from_different_detectors_are_merged()
    {
        var border = new VisualCandidate(new Rectangle(10, 10, 60, 40), VisualShape.Rectangle, VisualDetector.BorderEdge, 0.7);
        var fill = new VisualCandidate(new Rectangle(11, 11, 59, 39), VisualShape.RoundedRectangle, VisualDetector.FillRegion, 0.9);

        var merged = VisualMerger.Merge([border, fill]);

        Assert.Single(merged);
    }

    [Fact]
    public void Nested_control_keeps_the_inner_box()
    {
        var container = new VisualCandidate(new Rectangle(0, 0, 200, 60), VisualShape.RoundedRectangle, VisualDetector.FillRegion, 0.6);
        var inner = new VisualCandidate(new Rectangle(20, 12, 60, 34), VisualShape.RoundedRectangle, VisualDetector.BorderEdge, 0.8);

        var merged = VisualMerger.Merge([container, inner]);

        Assert.Single(merged);
        Assert.Equal(inner.Bounds, merged[0].Bounds);
    }

    private void Report(string name, Image32 image, VisualDetectionResult result, Rectangle[] truth)
    {
        var offered = result.Interactive;

        _output.WriteLine(
            $"{name}: {image.Width}x{image.Height} scales=[{string.Join(",", result.Scales)}] " +
            $"{result.ElapsedMilliseconds} ms recall={Metrics.Recall(offered, truth):0.00} " +
            $"precision={Metrics.Precision(offered, truth):0.00}");

        foreach (var candidate in offered)
        {
            if (!Metrics.Hits(candidate, truth))
            {
                _output.WriteLine(
                    $"   FP {candidate.Type} {candidate.Bounds} i={candidate.InteractionConfidence:0.00}");
            }
        }
    }
}

internal static class Metrics
{
    public const int Tolerance = 3;

    public static bool Hits(ControlCandidate candidate, Rectangle[] truth) =>
        Hits(candidate.Center, truth);

    public static bool Hits(VisualCandidate candidate, Rectangle[] truth) =>
        Hits(candidate.Center, truth);

    private static bool Hits(Point center, Rectangle[] truth)
    {
        foreach (var rect in truth)
        {
            if (Rectangle.Inflate(rect, Tolerance, Tolerance).Contains(center))
            {
                return true;
            }
        }

        return false;
    }

    public static double Recall(IReadOnlyList<ControlCandidate> candidates, Rectangle[] truth) =>
        Recall(candidates.Select(c => c.Center).ToList(), truth);

    public static double Recall(IReadOnlyList<VisualCandidate> candidates, Rectangle[] truth) =>
        Recall(candidates.Select(c => c.Center).ToList(), truth);

    public static double Precision(IReadOnlyList<ControlCandidate> candidates, Rectangle[] truth) =>
        Precision(candidates.Select(c => (object)c).ToList(), truth,
            item => ((ControlCandidate)item).Center);

    public static double Precision(IReadOnlyList<VisualCandidate> candidates, Rectangle[] truth) =>
        Precision(candidates.Select(c => (object)c).ToList(), truth,
            item => ((VisualCandidate)item).Center);

    private static double Recall(IReadOnlyList<Point> centers, Rectangle[] truth)
    {
        if (truth.Length == 0)
        {
            return 1;
        }

        int matched = 0;

        foreach (var rect in truth)
        {
            var area = Rectangle.Inflate(rect, Tolerance, Tolerance);

            if (centers.Any(area.Contains))
            {
                matched++;
            }
        }

        return (double)matched / truth.Length;
    }

    private static double Precision(
        IReadOnlyList<object> candidates,
        Rectangle[] truth,
        Func<object, Point> center)
    {
        if (candidates.Count == 0)
        {
            return 1;
        }

        return (double)candidates.Count(item => Hits(center(item), truth)) / candidates.Count;
    }
}
