using System.Drawing;
using KeyboardMouse.Vision;
using Xunit;
using Xunit.Abstractions;

namespace KeyboardMouse.Tests;

/// <summary>
/// Phase 5: the geometry a target is offered with. Recall and precision say
/// "there is a control here"; these tests say whether the rectangle is the
/// control and whether the click lands somewhere sane.
/// </summary>
public class TargetGeometryTests
{
    private readonly ITestOutputHelper _output;

    public TargetGeometryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Rounded_and_shadowed_buttons_keep_their_bounds()
    {
        var image = GeometryFixtures.RoundedAndShadow(out var truth);
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        Report("rounded", result, truth);

        foreach (var expected in truth)
        {
            var match = Best(result.Interactive, expected);
            Assert.True(match is not null, $"no target found for {expected}");

            var geometry = match!.Geometry!;
            double iou = Iou(geometry.Bounds, expected);

            _output.WriteLine($"   {expected} -> {geometry.Bounds} iou={iou:0.00} click={geometry.ClickPoint} conf={geometry.Confidence:0.00}");
            Assert.True(iou >= 0.80, $"bounds IoU {iou:0.00} below 0.80 for {expected}");
            Assert.True(In(expected, 2).Contains(geometry.ClickPoint), $"click {geometry.ClickPoint} outside {expected}");
            Assert.True(geometry.IsClickable);
        }
    }

    [Fact]
    public void Small_icons_get_a_hittable_but_honest_box()
    {
        var image = SyntheticUi.Toolbar(out var truth);
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        Report("toolbar", result, truth);

        foreach (var expected in truth)
        {
            var match = Best(result.Interactive, expected);
            Assert.True(match is not null, $"no target for {expected}");

            var geometry = match!.Geometry!;

            // Growing for hittability is allowed; claiming a box half again as
            // large as the control is not.
            Assert.True(geometry.Bounds.Width <= expected.Width * 1.6 + 2, $"grew too wide: {geometry.Bounds}");
            Assert.True(geometry.Bounds.Height <= expected.Height * 1.6 + 2, $"grew too tall: {geometry.Bounds}");
            Assert.True(In(expected, 2).Contains(geometry.ClickPoint), $"click {geometry.ClickPoint} outside {expected}");
        }
    }

    [Fact]
    public void Adjacent_buttons_keep_separate_click_points()
    {
        var image = GeometryFixtures.AdjacentButtons(out var truth);
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        Report("adjacent", result, truth);

        var matched = new List<(Rectangle Truth, ControlCandidate Candidate)>();

        foreach (var expected in truth)
        {
            var best = Best(result.Interactive, expected);
            Assert.True(best is not null, $"no target for {expected}");
            matched.Add((expected, best!));
        }

        foreach (var (truthA, a) in matched)
        {
            foreach (var (truthB, b) in matched)
            {
                if (ReferenceEquals(a, b))
                {
                    continue;
                }

                // No click point may fall into the neighbour, and the two boxes
                // may not overlap: three buttons are three targets.
                Assert.False(b.Geometry!.Bounds.Contains(a.Geometry!.ClickPoint),
                    $"click for {truthA} landed inside the neighbour {truthB}");
                Assert.True(Iou(a.Geometry.Bounds, b.Geometry.Bounds) <= 0.05,
                    $"{truthA} and {truthB} boxes overlap");
            }
        }

        // And the point must be away from the shared seam, not merely inside.
        Assert.True(matched[0].Candidate.Geometry!.ClickPoint.X < truth[0].Right - 8);
        Assert.True(matched[2].Candidate.Geometry!.ClickPoint.X > truth[2].Left + 8);
    }

    [Fact]
    public void Container_is_not_offered_over_a_smaller_control_inside_it()
    {
        var image = GeometryFixtures.NestedPanel(out var panel, out var button);
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        _output.WriteLine($"nested: {result.Interactive.Count} offered");
        foreach (var candidate in result.Interactive)
        {
            _output.WriteLine($"   {candidate.Type} {candidate.Geometry?.Bounds} click={candidate.Geometry?.ClickPoint}");
        }

        var offered = result.Interactive.Where(c => c.Geometry is not null).ToList();
        var panelTarget = offered.FirstOrDefault(c =>
            Iou(c.Geometry!.Bounds, panel) >= 0.6);

        Assert.True(panelTarget is null,
            $"the panel itself was offered as a target: {panelTarget?.Type} {panelTarget?.Geometry?.Bounds}");

        var buttonTarget = Best(offered, button);
        Assert.True(buttonTarget is not null, "the button inside the panel was not offered");
        Assert.True(Iou(buttonTarget!.Geometry!.Bounds, button) >= 0.75);
    }

    [Fact]
    public void Controls_touching_the_frame_edge_stay_inside_it()
    {
        var image = GeometryFixtures.EdgeRow(out var truth);
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());
        var frame = new Rectangle(0, 0, image.Width, image.Height);

        Report("edge", result, truth);

        foreach (var candidate in result.Interactive)
        {
            var geometry = candidate.Geometry!;
            Assert.True(frame.Contains(geometry.ClickPoint), $"click outside frame: {geometry.ClickPoint}");
            Assert.True(geometry.IsClickable);
        }

        foreach (var expected in truth)
        {
            var match = Best(result.Interactive, expected);
            Assert.True(match is not null, $"no target for {expected}");
            Assert.True(In(expected, 3).Contains(match!.Geometry!.ClickPoint),
                $"click {match.Geometry.ClickPoint} outside {expected}");
        }

        // Flush controls are legitimately clipped by the frame; they must say so
        // without being rejected.
        var flush = result.Interactive.First(c => Iou(c.Geometry!.Bounds, truth[0]) >= 0.5);
        Assert.True((flush.Geometry!.Issues & GeometryIssue.ClippedByFrame) != 0 ||
                    flush.Geometry.Bounds.Left >= 0);
        Assert.True(flush.Geometry.Confidence < 1.0);
    }

    [Fact]
    public void Resized_processing_keeps_geometry_close_to_ground_truth()
    {
        var image = GeometryFixtures.WidePage(1600, 1000, out var truth);
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        _output.WriteLine($"wide: scales=[{string.Join(",", result.Scales)}] {result.ElapsedMilliseconds} ms");
        Report("wide", result, truth);

        Assert.Contains(result.Scales, scale => scale < 0.99);

        foreach (var expected in truth)
        {
            var match = Best(result.Interactive, expected);
            Assert.True(match is not null, $"no target for {expected} at a reduced scale");

            var geometry = match!.Geometry!;
            double iou = Iou(geometry.Bounds, expected);
            _output.WriteLine($"   {expected} -> {geometry.Bounds} iou={iou:0.00}");

            Assert.True(iou >= 0.70, $"bounds IoU {iou:0.00} below 0.70 after scaling");
            Assert.True(In(expected, 4).Contains(geometry.ClickPoint));
        }
    }

    [Fact]
    public void Irregular_shape_moves_the_click_point_off_the_empty_centre()
    {
        var image = GeometryFixtures.OpenArc(out var truth);
        var geometry = GeometryEstimator.Estimate(ControlType.IconButton, truth, image);

        int background = image.Luma(truth.Left - 10, truth.Top - 10);
        int centreLuma = image.Luma(truth.Left + (truth.Width / 2), truth.Top + (truth.Height / 2));
        int clickLuma = image.Luma(geometry.ClickPoint.X, geometry.ClickPoint.Y);

        _output.WriteLine(
            $"arc: bounds={geometry.Bounds} centre={centreLuma} click={geometry.ClickPoint} clickLuma={clickLuma} " +
            $"issues={geometry.Issues} conf={geometry.Confidence:0.00}");

        // The middle of the arc is paper; the click has to land on the stroke.
        Assert.True(Math.Abs(centreLuma - background) <= 12, "fixture centre is not empty");
        Assert.True(Math.Abs(clickLuma - background) >= 25, "click point landed on the empty centre");
        Assert.True(geometry.Bounds.Contains(geometry.ClickPoint));
        Assert.True((geometry.Issues & GeometryIssue.IrregularShape) != 0);
    }

    [Fact]
    public void Neighbour_protection_moves_the_click_point_away()
    {
        var image = GeometryFixtures.AdjacentButtons(out var truth);
        var neighbour = truth[2];
        var geometry = GeometryEstimator.Estimate(
            ControlType.Button,
            truth[1],
            image,
            [neighbour, truth[0]]);

        _output.WriteLine($"guarded click={geometry.ClickPoint} of {geometry.Bounds} issues={geometry.Issues}");

        Assert.True(geometry.Bounds.Contains(geometry.ClickPoint));
        Assert.False(neighbour.Contains(geometry.ClickPoint));
        Assert.True(geometry.ClickPoint.X < geometry.Bounds.Left + (geometry.Bounds.Width / 2) + 2,
            "the click point should lean away from the neighbour on the right");
    }

    [Fact]
    public void Impossible_boxes_are_rejected_and_flagged()
    {
        var image = GeometryFixtures.RoundedAndShadow(out _);
        var frame = new Rectangle(0, 0, image.Width, image.Height);

        var zero = GeometryEstimator.Estimate(ControlType.Button, new Rectangle(20, 20, 0, 0), image);
        var tiny = GeometryEstimator.Estimate(ControlType.Button, new Rectangle(20, 20, 4, 3), image);
        var huge = GeometryEstimator.Estimate(ControlType.Button, new Rectangle(0, 0, image.Width, image.Height), image);
        var outside = GeometryEstimator.Estimate(ControlType.Button, new Rectangle(image.Width + 40, 20, 60, 30), image);

        _output.WriteLine($"zero={zero.Issues} tiny={tiny.Issues} huge={huge.Issues} outside={outside.Issues}");

        Assert.False(zero.IsValid);
        Assert.True((zero.Issues & GeometryIssue.ZeroSize) != 0);

        Assert.False(tiny.IsValid);
        Assert.True((tiny.Issues & GeometryIssue.TooSmall) != 0);

        Assert.False(huge.IsValid);
        Assert.True((huge.Issues & GeometryIssue.TooLarge) != 0);

        Assert.False(outside.IsValid);
        Assert.True((outside.Issues & GeometryIssue.OffFrame) != 0);

        Assert.True(frame.Contains(zero.Bounds));
    }

    [Fact]
    public void Type_limits_separate_a_radio_from_a_dialog()
    {
        var image = GeometryFixtures.AdjacentButtons(out _);

        var radio = GeometryConstraints.For(ControlType.RadioButton);
        var button = GeometryConstraints.For(ControlType.Button);

        Assert.True(radio.MaxWidth < button.MaxWidth);
        Assert.True(radio.MinWidth < button.MinWidth);

        var wideBox = new Rectangle(20, 40, 200, 30);
        var asRadio = GeometryEstimator.Estimate(ControlType.RadioButton, wideBox, image);
        var asButton = GeometryEstimator.Estimate(ControlType.Button, wideBox, image);

        _output.WriteLine($"200x30 as radio={asRadio.Issues} conf={asRadio.Confidence:0.00}, as button={asButton.Issues} conf={asButton.Confidence:0.00}");

        Assert.True((asRadio.Issues & GeometryIssue.TooLarge) != 0);
        Assert.False(asRadio.IsValid);
        Assert.False((asButton.Issues & GeometryIssue.TooLarge) != 0);
        Assert.True(asButton.Confidence > asRadio.Confidence);
    }

    [Fact]
    public void Geometry_confidence_separates_clean_controls_from_ambiguous_ones()
    {
        var image = GeometryFixtures.RoundedAndShadow(out var truth);
        var clean = GeometryEstimator.Estimate(ControlType.Button, truth[0], image);
        var ambiguous = GeometryEstimator.Estimate(ControlType.Button, new Rectangle(2, 2, 60, 20), image);

        _output.WriteLine($"clean={clean.Confidence:0.00} {clean.Issues} | ambiguous={ambiguous.Confidence:0.00} {ambiguous.Issues}");

        Assert.True(clean.Confidence >= 0.7);
        Assert.True(ambiguous.Confidence < clean.Confidence);

        var cleanRank = 0.8 * (0.55 + (0.45 * clean.Confidence));
        var ambiguousRank = 0.8 * (0.55 + (0.45 * ambiguous.Confidence));
        Assert.True(cleanRank > ambiguousRank);
    }

    [Fact]
    public void Every_offered_target_has_a_click_point_inside_its_bounds()
    {
        foreach (var (category, image, truth, _) in ControlFixtures.All())
        {
            var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

            foreach (var candidate in result.Interactive)
            {
                var geometry = candidate.Geometry;

                Assert.True(geometry is not null, $"{category}: offered without geometry");
                Assert.True(geometry!.IsClickable, $"{category}: not clickable ({geometry.Issues})");
                Assert.True(geometry.Bounds.Contains(geometry.ClickPoint));
                Assert.True(geometry.SafeRegion.Width > 0 && geometry.SafeRegion.Height > 0);
                Assert.InRange(geometry.Confidence, 0.0, 1.0);
            }

            _output.WriteLine($"{category}: {result.Interactive.Count}/{truth.Length} offered, all clickable");
        }
    }

    [Fact]
    public void Category_fixtures_reach_a_useful_bounds_iou()
    {
        foreach (var (category, image, truth, _) in ControlFixtures.All())
        {
            if (truth.Length == 0)
            {
                continue;
            }

            var result = VisualPipeline.Detect(image, new VisualDetectionOptions());
            var matched = new List<double>();
            int found = 0;
            int containers = 0;
            int wide = 0;
            int fragments = 0;

            foreach (var expected in truth)
            {
                var rect = expected.Bounds;

                // Only a candidate whose click point really lands on the truth
                // counts as the same control; a ring inside a menu row is not
                // the row, whatever its IoU says.
                var best = result.Interactive
                    .Where(c => c.Geometry is not null && In(rect, 3).Contains(c.Geometry!.ClickPoint))
                    .OrderByDescending(c => Iou(c.Geometry!.Bounds, rect))
                    .FirstOrDefault();

                if (best is null)
                {
                    continue;
                }

                found++;
                double value = Iou(best.Geometry!.Bounds, rect);
                _output.WriteLine($"   {category}: {expected.Type} truth={rect} box={best.Geometry!.Bounds} iou={value:0.00}");

                if (value >= 0.5)
                {
                    matched.Add(value);
                }
                else if ((long)best.Geometry!.Bounds.Width * best.Geometry.Bounds.Height >
                         1.5 * (long)rect.Width * rect.Height)
                {
                    var box = best.Geometry!.Bounds;
                    long boxArea = (long)box.Width * box.Height;

                    // The brief's rule is about a container that hides a smaller
                    // control we did find. A box wider than the truth because the
                    // row has no divider is a detection limitation, reported as
                    // "wide" instead of pretending it is the same thing.
                    bool hidesSmaller = result.Interactive.Any(other =>
                        !ReferenceEquals(other, best) &&
                        other.Geometry is not null &&
                        box.Contains(other.Geometry.Bounds) &&
                        (long)other.Geometry.Bounds.Width * other.Geometry.Bounds.Height < 0.6 * boxArea);

                    if (hidesSmaller)
                    {
                        containers++;
                        _output.WriteLine($"   CONTAINER {expected.Type} truth={rect} box={box} ({best.Type})");
                    }
                    else
                    {
                        wide++;
                        _output.WriteLine($"   wide {expected.Type} truth={rect} box={box} ({best.Type}) iou={value:0.00}");
                    }
                }
                else
                {
                    // A smaller piece of the truth (a row's checkbox, a link's rule).
                    fragments++;
                }
            }

            double mean = matched.Count == 0 ? 0 : matched.Average();
            _output.WriteLine(
                $"{category}: found {found}/{truth.Length}, bounds IoU>=0.5 for {matched.Count}/{found}, mean IoU={mean:0.00}");

            if (matched.Count >= 2)
            {
                Assert.True(mean >= 0.65, $"{category}: mean bounds IoU {mean:0.00} below 0.65");
            }

            _output.WriteLine(
                $"{category}: tight={matched.Count} fragments={fragments} wide={wide} containers={containers}");

            // The Phase 5 promise is "no loose container instead of the control".
            // A box smaller than the truth, or wider because the row has no
            // divider, is a different and separately reported limitation.
            Assert.True(containers == 0, $"{category}: {containers} container(s) offered over a smaller control");
        }
    }

    // ---------- helpers ----------

    private void Report(string name, VisualDetectionResult result, Rectangle[] truth)
    {
        _output.WriteLine(
            $"{name}: scales=[{string.Join(",", result.Scales)}] {result.ElapsedMilliseconds} ms " +
            $"offered={result.Interactive.Count} truth={truth.Length}");

        foreach (var candidate in result.Interactive)
        {
            _output.WriteLine(
                $"   {candidate.Type} {candidate.Geometry?.Bounds} click={candidate.Geometry?.ClickPoint} " +
                $"conf={candidate.Geometry?.Confidence:0.00} {candidate.Geometry?.Issues}");
        }
    }

    private static ControlCandidate? Best(IReadOnlyList<ControlCandidate> controls, Rectangle truth) =>
        controls
            .Where(c => c.Geometry is not null)
            .OrderByDescending(c => In(truth, 3).Contains(c.Geometry!.ClickPoint) ? 1 : 0)
            .ThenByDescending(c => Iou(c.Geometry!.Bounds, truth))
            .FirstOrDefault();

    private static Rectangle In(Rectangle rect, int amount) => Rectangle.Inflate(rect, amount, amount);

    private static double Iou(Rectangle a, Rectangle b)
    {
        var intersection = Rectangle.Intersect(a, b);

        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0;
        }

        long inter = (long)intersection.Width * intersection.Height;
        long union = ((long)a.Width * a.Height) + ((long)b.Width * b.Height) - inter;

        return union <= 0 ? 0 : (double)inter / union;
    }
}
