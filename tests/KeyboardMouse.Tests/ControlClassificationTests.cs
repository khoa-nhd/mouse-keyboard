using System.Drawing;
using KeyboardMouse.Vision;
using Xunit.Abstractions;

namespace KeyboardMouse.Tests;

/// <summary>
/// Phase 4 measurement. Every category reports localisation recall, type
/// accuracy and precision separately: an aggregate F1 would hide exactly the
/// failure this work exists to avoid — a good text-button score with poor
/// icon-button accuracy.
///
/// The floors below are the values measured on these fixtures, rounded down.
/// They are regression guards, not claims about real applications.
/// </summary>
public class ControlClassificationTests
{
    private const int Tolerance = 4;

    private readonly ITestOutputHelper _output;

    public ControlClassificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Recall per category, measured. Dense navigation and owner-drawn
    /// widgets are the weakest and are documented as such in the README.</summary>
    private static readonly Dictionary<string, double> RecallFloor = new()
    {
        ["text-buttons"] = 0.85,
        ["borderless-toolbar"] = 0.60,
        ["small-toolbar"] = 0.90,
        ["cards"] = 0.95,
        ["icon-buttons"] = 0.40,
        ["custom-drawn"] = 0.40,
        ["dense-navigation"] = 0.15,
        ["decorative"] = 1.00
    };

    [Fact]
    public void Per_category_recall_precision_and_type_accuracy()
    {
        var rows = new List<Row>();
        int falsePositives = 0;
        var recallByCategory = new Dictionary<string, double>();

        foreach (var (category, image, truth, interactive) in ControlFixtures.All())
        {
            var result = VisualPipeline.Detect(image, new VisualDetectionOptions());
            var offered = result.Interactive;
            var matched = new HashSet<int>();

            foreach (var control in offered)
            {
                int index = IndexOfTruth(control.Bounds, truth);

                if (index < 0)
                {
                    falsePositives++;
                    _output.WriteLine(
                        $"   FP [{category}] {control.Type} interaction={control.InteractionConfidence:0.00} " +
                        $"detection={control.DetectionConfidence:0.00} {control.Bounds} {control.Evidence}");
                    continue;
                }

                matched.Add(index);
                rows.Add(new Row(category, truth[index].Type, control.Type,
                    truth[index].Type == control.Type,
                    truth[index].Type == control.Type || IsAcceptableSubstitute(truth[index].Type, control.Type)));
            }

            double recall = truth.Length == 0
                ? (offered.Count == 0 ? 1.0 : 0.0)
                : matched.Count / (double)truth.Length;

            double precision = offered.Count == 0
                ? 1.0
                : (offered.Count - (offered.Count - matched.Count)) / (double)offered.Count;

            recallByCategory[category] = recall;

            _output.WriteLine(
                $"{category,-20} truth={truth.Length,2} offered={offered.Count,2} " +
                $"recall={recall:0.00} precision={precision:0.00} {result.ElapsedMilliseconds} ms");

            if (interactive && truth.Length > 0)
            {
                for (int i = 0; i < truth.Length; i++)
                {
                    if (!matched.Contains(i))
                    {
                        _output.WriteLine($"   MISS [{category}] {truth[i].Type} {truth[i].Bounds}");
                    }
                }
            }
        }

        _output.WriteLine(string.Empty);

        foreach (var group in rows.GroupBy(row => row.Truth).OrderBy(group => group.Key.ToString()))
        {
            int total = group.Count();
            int exact = group.Count(row => row.Exact);
            int acceptable = group.Count(row => row.Acceptable);

            _output.WriteLine(
                $"{group.Key,-13} exact={exact}/{total} acceptable={acceptable}/{total} " +
                $"({(double)acceptable / total:0.00})");
        }

        _output.WriteLine($"offered={rows.Count} falsePositives={falsePositives}");

        foreach (var (category, floor) in RecallFloor)
        {
            Assert.True(
                recallByCategory.TryGetValue(category, out double recall),
                $"missing category {category}");
            Assert.True(
                recall >= floor,
                $"{category}: recall {recall:0.00} below floor {floor:0.00}");
        }

        // Decorative icons, cards, photos and paragraphs must not become targets.
        Assert.True(falsePositives <= 1, $"{falsePositives} false positives on non-controls");
    }

    [Fact]
    public void Text_buttons_are_buttons_not_fields()
    {
        var (_, image, truth, _) = ControlFixtures.All().First(fixture => fixture.Category == "text-buttons");
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        var textButtons = result.Interactive.Where(control => control.Type == ControlType.Button).ToList();

        _output.WriteLine(
            $"text buttons: {textButtons.Count} of {truth.Length} truth targets, " +
            $"types={string.Join(",", textButtons.Select(c => c.Type))}");

        Assert.True(textButtons.Count >= truth.Length - 1, "nearly every text button should be typed Button");
        Assert.DoesNotContain(result.Interactive, control => control.Type == ControlType.TextField);
    }

    [Fact]
    public void Icon_only_controls_outweigh_decorative_icons()
    {
        var (_, image, truth, _) = ControlFixtures.All().First(fixture => fixture.Category == "small-toolbar");
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        int iconControls = result.Interactive.Count(control => control.Type is ControlType.IconButton or ControlType.ToolbarItem);

        _output.WriteLine($"icon controls: {iconControls} of {truth.Length}");

        Assert.Equal(truth.Length, iconControls);
        Assert.All(result.Interactive, control => Assert.True(
            control.InteractionConfidence >= ControlThresholds.InteractiveThreshold(control.Type)));
    }

    [Fact]
    public void Decorative_page_yields_no_targets()
    {
        var (_, image, _, _) = ControlFixtures.All().First(fixture => fixture.Category == "decorative");
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        foreach (var control in result.Controls.Where(c => !c.IsInteractive))
        {
            _output.WriteLine(
                $"   off {control.Type} i={control.InteractionConfidence:0.00} d={control.DetectionConfidence:0.00} {control.Bounds}");
        }

        Assert.Empty(result.Interactive);
    }

    [Fact]
    public void Interaction_confidence_is_separate_from_detection_confidence()
    {
        var (_, image, _, _) = ControlFixtures.All().First(fixture => fixture.Category == "decorative");
        var result = VisualPipeline.Detect(image, new VisualDetectionOptions());

        // The paragraph and the grey marks are real shapes, so detection scores
        // them; only the interaction score says "not a control".
        var decorative = result.Controls
            .Where(control => control.Type is ControlType.DecorativeIcon or ControlType.StaticLabel)
            .ToList();

        Assert.NotEmpty(decorative);
        Assert.All(decorative, control =>
        {
            Assert.True(control.DetectionConfidence >= 0.4, "these shapes were detected");
            Assert.True(control.InteractionConfidence < 0.5, "but they are not interactive");
        });
    }

    [Fact]
    public void Type_thresholds_are_per_type()
    {
        Assert.True(
            ControlThresholds.InteractiveThreshold(ControlType.IconButton) >
            ControlThresholds.InteractiveThreshold(ControlType.Button),
            "an icon-only control needs more evidence than a bordered text button");

        Assert.True(
            ControlThresholds.InteractiveThreshold(ControlType.DecorativeIcon) >
            ControlThresholds.InteractiveThreshold(ControlType.IconButton));

        Assert.True(
            ControlThresholds.InteractiveThreshold(ControlType.Card) >
            ControlThresholds.InteractiveThreshold(ControlType.Button));
    }

    private static bool IsAcceptableSubstitute(ControlType truth, ControlType actual)
    {
        // A borderless icon in a toolbar and a bordered icon button are the same
        // target with different chrome; counting that as a type error would hide
        // real mistakes.
        return (truth, actual) switch
        {
            (ControlType.ToolbarItem, ControlType.IconButton) => true,
            (ControlType.IconButton, ControlType.ToolbarItem) => true,
            (ControlType.Button, ControlType.ToggleButton) => true,
            (ControlType.ToggleButton, ControlType.Button) => true,
            _ => false
        };
    }

    private static int IndexOfTruth(Rectangle bounds, ControlTruth[] truth)
    {
        var center = new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));

        for (int i = 0; i < truth.Length; i++)
        {
            if (Rectangle.Inflate(truth[i].Bounds, Tolerance, Tolerance).Contains(center))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed record Row(string Category, ControlType Truth, ControlType Actual, bool Exact, bool Acceptable);
}
