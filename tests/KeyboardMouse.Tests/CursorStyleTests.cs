using System.Drawing;
using KeyboardMouse.Overlays;

namespace KeyboardMouse.Tests;

public class CursorStyleTests
{
    [Fact]
    public void EveryStyle_RoundTripsThroughItsId()
    {
        Assert.Equal(8, CursorStyleNames.All.Count);
        Assert.Equal(8, CursorStyleNames.All.Distinct().Count());

        foreach (var style in CursorStyleNames.All)
        {
            string id = CursorStyleNames.ToId(style);
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(CursorStyleNames.TryParse(id, out var parsed));
            Assert.Equal(style, parsed);
            Assert.True(CursorStyleNames.TryParse(id.ToUpperInvariant(), out var upper));
            Assert.Equal(style, upper);
            Assert.False(string.IsNullOrWhiteSpace(CursorStyleNames.DisplayName(style)));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("nope")]
    public void TryParse_RejectsUnknownValues(string? value)
    {
        Assert.False(CursorStyleNames.TryParse(value, out var style));
        Assert.Equal(CursorStyle.Dot, style);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(48)]
    public void Measure_ReturnsUsableSurface_AndSupportsThePointer(int size)
    {
        foreach (var style in CursorStyleNames.All)
        {
            var visual = CursorPainter.Measure(style, size);

            Assert.True(visual.Width > 0, $"{style} width");
            Assert.True(visual.Height > 0, $"{style} height");

            // The pointer point must fall inside the surface.
            Assert.InRange(-visual.OffsetX, 0, visual.Width);
            Assert.InRange(-visual.OffsetY, 0, visual.Height);
        }
    }

    [Fact]
    public void Paint_DrawsSomethingForEveryStyle()
    {
        var trail = new[]
        {
            new PointF(20, 20),
            new PointF(26, 24),
            new PointF(32, 30)
        };

        foreach (var style in CursorStyleNames.All)
        {
            var visual = CursorPainter.Measure(style, 14);
            using var bitmap = new Bitmap(visual.Width, visual.Height);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.Clear(Color.Transparent);
            var pointer = new PointF(-visual.OffsetX, -visual.OffsetY);
            CursorPainter.Paint(
                graphics,
                style,
                14,
                Color.FromArgb(255, 46, 204, 113),
                1.25f,
                pointer,
                trail);

            Assert.True(HasVisiblePixel(bitmap), $"{style} painted nothing");
        }
    }

    [Fact]
    public void AnimatedStyles_AreExactlyTheOnesWithMotion()
    {
        var animated = CursorStyleNames.All.Where(CursorPainter.IsAnimated).ToArray();

        Assert.Equal(
            new[] { CursorStyle.Spotlight, CursorStyle.Orbit, CursorStyle.Buddy },
            animated);
    }

    private static bool HasVisiblePixel(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 8)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
