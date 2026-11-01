using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class HintLabelsTests
{
    [Fact]
    public void EightTargets_UseSingleAlternatingHomeRowLetters()
    {
        var labels = HintLabels.Generate(8);

        Assert.Equal(new[] { "a", "j", "s", "k", "d", "l", "f", ";" }, labels);
        Assert.All(labels, label => Assert.Single(label));
    }

    [Fact]
    public void NineTargets_NeedTwoLetters()
    {
        var labels = HintLabels.Generate(9);

        Assert.Equal(9, labels.Count);
        Assert.All(labels, label => Assert.Equal(2, label.Length));
        Assert.Equal("aa", labels[0]);
        Assert.Equal("aj", labels[8]);
    }

    [Fact]
    public void LabelLength_GrowsWhenTheAlphabetRunsOut()
    {
        Assert.All(HintLabels.Generate(64), label => Assert.Equal(2, label.Length));
        Assert.All(HintLabels.Generate(65), label => Assert.Equal(3, label.Length));
        Assert.All(HintLabels.Generate(512), label => Assert.Equal(3, label.Length));
        Assert.All(HintLabels.Generate(513), label => Assert.Equal(4, label.Length));
    }

    [Fact]
    public void Labels_AreUniqueAndPrefixFree()
    {
        var labels = HintLabels.Generate(500);

        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());

        for (int i = 0; i < labels.Count; i++)
        {
            for (int j = 0; j < labels.Count; j++)
            {
                if (i != j)
                {
                    Assert.False(labels[j].StartsWith(labels[i], StringComparison.Ordinal));
                }
            }
        }
    }

    [Fact]
    public void CustomAlphabet_IsDeduplicatedAndInterleaved()
    {
        var labels = HintLabels.Generate(8, "aabbccddeeffgghh");

        Assert.Equal(new[] { "a", "e", "b", "f", "c", "g", "d", "h" }, labels);
    }

    [Fact]
    public void TooShortAlphabet_FallsBackToTheDefault()
    {
        var labels = HintLabels.Generate(8, "x");

        Assert.Equal("a", labels[0]);
        Assert.Equal(";", labels[7]);
    }

    [Fact]
    public void ZeroOrNegativeCount_ReturnsNothing()
    {
        Assert.Empty(HintLabels.Generate(0));
        Assert.Empty(HintLabels.Generate(-10));
    }
}
