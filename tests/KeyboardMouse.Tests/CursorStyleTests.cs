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
            new TrailSample(new PointF(20, 20), 0.9f),
            new TrailSample(new PointF(26, 24), 0.5f),
            new TrailSample(new PointF(32, 30), 0.1f)
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
            new[] { CursorStyle.Halo, CursorStyle.Spotlight, CursorStyle.Comet, CursorStyle.Orbit },
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

    [Theory]
    [InlineData("e74c3c", "e74c3c")]
    [InlineData("#E74C3C", "e74c3c")]
    [InlineData("  #f1c40f ", "f1c40f")]
    [InlineData("", "2ecc71")]
    [InlineData("#abc", "2ecc71")]
    [InlineData("not-a-colour", "2ecc71")]
    [InlineData(null, "2ecc71")]
    public void CursorColors_NormalizeAcceptsSixHexDigits(string? input, string expected)
    {
        Assert.Equal(expected, CursorColors.Normalize(input));
    }

    [Fact]
    public void CursorColors_ToColorMatchesTheHexDigits()
    {
        Assert.Equal(Color.FromArgb(255, 231, 76, 60), CursorColors.ToColor("e74c3c"));
        Assert.Equal(Color.FromArgb(255, 46, 204, 113), CursorColors.ToColor(null));
    }

    [Fact]
    public void OldBuddySetting_LoadsAsBrackets()
    {
        Assert.True(CursorStyleNames.TryParse("buddy", out var style));
        Assert.Equal(CursorStyle.Brackets, style);
        Assert.Equal("brackets", CursorStyleNames.ToId(CursorStyle.Brackets));
    }

    [Theory]
    [InlineData("frame", ScreenBorderStyle.Frame)]
    [InlineData("GLOW", ScreenBorderStyle.Glow)]
    [InlineData(" corners ", ScreenBorderStyle.Corners)]
    [InlineData("none", ScreenBorderStyle.None)]
    public void BorderStyles_ParseTheirIds(string value, ScreenBorderStyle expected)
    {
        Assert.True(BorderStyles.TryParse(value, out var style));
        Assert.Equal(expected, style);
        Assert.Equal(BorderStyles.ToId(expected), BorderStyles.ToId(style));
    }

    [Fact]
    public void BorderStyles_FallBackToFrame()
    {
        Assert.False(BorderStyles.TryParse("neon", out _));
        Assert.Equal(ScreenBorderStyle.Frame, BorderStyles.ParseOrDefault(null));
        Assert.Equal("frame", BorderStyles.ToId(ScreenBorderStyle.Frame));
    }
}
