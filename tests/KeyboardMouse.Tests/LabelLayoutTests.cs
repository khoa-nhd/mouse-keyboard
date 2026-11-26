using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class LabelLayoutTests
{
    private static readonly RectangleF Screen = new(0, 0, 1920, 1080);
    private static readonly SizeF Label = new(30, 20);

    [Fact]
    public void ALoneLabelSitsOnItsTargetsTopLeftCorner()
    {
        var spots = LabelLayout.Place([(new RectangleF(100, 100, 80, 30), Label)], Screen);

        Assert.Equal(new PointF(100, 100), spots[0]);
    }

    [Fact]
    public void LabelsOfNeighbouringTargetsDoNotOverlap()
    {
        // Ten toolbar buttons 8 px apart: corner labels 30 px wide would collide.
        var items = Enumerable.Range(0, 10)
            .Select(i => (new RectangleF(100 + (i * 20), 100, 18, 18), Label))
            .ToList();

        var spots = LabelLayout.Place(items, Screen);
        var boxes = spots.Select(p => new RectangleF(p, Label)).ToArray();

        for (int i = 0; i < boxes.Length; i++)
        {
            for (int j = i + 1; j < boxes.Length; j++)
            {
                var overlap = RectangleF.Intersect(boxes[i], boxes[j]);
                Assert.True(overlap.Width <= 0 || overlap.Height <= 0, $"labels {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void ANestedTargetMovesItsLabelInsideItself()
    {
        // A row and a button inside it share the top-left corner.
        var row = new RectangleF(100, 100, 400, 40);
        var button = new RectangleF(100, 100, 120, 40);

        var spots = LabelLayout.Place([(row, Label), (button, Label)], Screen);

        Assert.Equal(new PointF(100, 100), spots[0]);
        Assert.True(button.Contains(new RectangleF(spots[1], Label)), $"label at {spots[1]} left its button");
    }

    [Fact]
    public void LabelsStayOnTheScreen()
    {
        var spots = LabelLayout.Place([(new RectangleF(1905, 1070, 10, 10), Label)], Screen);

        Assert.True(spots[0].X + Label.Width <= Screen.Right);
        Assert.True(spots[0].Y + Label.Height <= Screen.Bottom);
    }
}
