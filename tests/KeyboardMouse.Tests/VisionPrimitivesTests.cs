using System.Drawing;
using KeyboardMouse.Vision;

namespace KeyboardMouse.Tests;

/// <summary>
/// Unit tests for the primitives the detectors sit on. Each one pins behaviour
/// that the calibration fixtures cannot localise: a blur that shifts tones, a
/// resize that maps back wrongly or a labelling pass that merges neighbours
/// would only show up later as a vague recall loss.
/// </summary>
public class VisionPrimitivesTests
{
    private static Image32 Solid(int width, int height, byte value)
    {
        var image = new Image32(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image.Set(x, y, value, value, value);
            }
        }

        return image;
    }

    private static Image32 Square(int size, int left, int top, int width, int height)
    {
        var image = Solid(size, size, 255);

        for (int y = top; y < top + height; y++)
        {
            for (int x = left; x < left + width; x++)
            {
                image.Set(x, y, 0, 0, 0);
            }
        }

        return image;
    }

    [Fact]
    public void Downscale_preserves_the_average_tone()
    {
        var image = Solid(40, 20, 200);
        var resized = image.Resize(10, 5);

        Assert.Equal(10, resized.Width);
        Assert.Equal(5, resized.Height);
        Assert.Equal(200, resized.Red(5, 2));
    }

    [Fact]
    public void Resize_to_same_size_keeps_pixels()
    {
        var image = Square(32, 8, 8, 16, 16);
        var resized = image.Resize(32, 32);

        Assert.Equal(0, resized.Red(16, 16));
        Assert.Equal(255, resized.Red(1, 1));
    }

    [Fact]
    public void Box_blur_keeps_a_flat_field_exact()
    {
        var gray = GrayImage.FromImage(Solid(24, 24, 120));
        var blurred = gray.BoxBlur(2);

        for (int y = 0; y < blurred.Height; y++)
        {
            for (int x = 0; x < blurred.Width; x++)
            {
                Assert.Equal(120, blurred[x, y]);
            }
        }
    }

    [Fact]
    public void Box_blur_spreads_a_single_bright_pixel()
    {
        var gray = new GrayImage(9, 9);
        gray.Set(4, 4, 255);

        var blurred = gray.BoxBlur(1);

        Assert.True(blurred[4, 4] < 255, "peak must drop");
        Assert.True(blurred[4, 5] > 0, "neighbour must receive light");
        Assert.Equal(0, blurred[0, 0]);

        // A box average preserves total brightness: the single peak is spread,
        // not amplified or lost.
        Assert.InRange(blurred.Sum(0, 0, 9, 9), 200, 300);
    }

    [Fact]
    public void Canny_marks_the_border_and_not_the_interior()
    {
        var edges = GrayImage.FromImage(Square(80, 20, 20, 40, 40)).Canny();

        Assert.True(edges[20, 40] > 0, "top border should be an edge");
        Assert.True(edges[60, 40] > 0, "bottom border should be an edge");
        Assert.True(edges[40, 20] > 0, "left border should be an edge");
        Assert.Equal(0, edges[40, 40]);
        Assert.Equal(0, edges[2, 2]);
    }

    [Fact]
    public void Otsu_separates_a_two_tone_image()
    {
        var gray = GrayImage.FromImage(Square(64, 16, 16, 32, 32));
        var binary = gray.Binarize(gray.OtsuThreshold());

        Assert.Equal(255, binary[32, 32]);
        Assert.Equal(0, binary[2, 2]);
        Assert.Equal(32 * 32, binary.Data.Count(value => value == 255));
    }

    [Fact]
    public void Binary_blobs_report_separate_components_with_bounds()
    {
        var image = Solid(60, 30, 255);
        image.Set(5, 5, 0, 0, 0);
        image.Set(6, 5, 0, 0, 0);
        image.Set(40, 20, 0, 0, 0);

        var blobs = Blobs.FromBinary(GrayImage.FromImage(image).Binarize(128));

        Assert.Equal(2, blobs.Count);
        Assert.Contains(blobs, b => b.Area == 2 && b.Bounds == new Rectangle(5, 5, 2, 1));
        Assert.Contains(blobs, b => b.Area == 1 && b.Bounds == new Rectangle(40, 20, 1, 1));
    }

    [Fact]
    public void Color_blobs_separate_different_fills()
    {
        var image = Solid(60, 20, 255);

        for (int y = 4; y < 16; y++)
        {
            for (int x = 4; x < 24; x++)
            {
                image.Set(x, y, 220, 220, 220);
            }

            for (int x = 32; x < 52; x++)
            {
                image.Set(x, y, 210, 210, 210);
            }
        }

        var blobs = Blobs.FromColorKeys(image)
            .Where(b => b.Bounds.Width is >= 10 and <= 30 && b.Bounds.Height is >= 10 and <= 20)
            .ToList();

        Assert.Equal(2, blobs.Count);
    }

    [Fact]
    public void Edge_segments_find_horizontal_and_vertical_runs()
    {
        var edges = GrayImage.FromImage(Square(80, 20, 20, 40, 40)).Canny();

        var horizontal = EdgeSegments.Horizontal(edges).Where(s => s.Position is >= 19 and <= 21).ToList();
        var vertical = EdgeSegments.Vertical(edges).Where(s => s.Position is >= 19 and <= 21).ToList();

        Assert.Contains(horizontal, s => s.Length >= 30);
        Assert.Contains(vertical, s => s.Length >= 30);
    }

    [Fact]
    public void Frame_maps_scaled_boxes_back_to_source_pixels()
    {
        var frame = new DetectionFrame(Solid(100, 60, 255), 0.5);

        Assert.Equal(new Rectangle(20, 10, 20, 10), frame.ToSource(new Rectangle(10, 5, 10, 5)));

        var native = new DetectionFrame(Solid(100, 60, 255), 1.0);
        Assert.Equal(new Rectangle(10, 5, 10, 5), native.ToSource(new Rectangle(10, 5, 10, 5)));
    }

    [Fact]
    public void Glyph_sized_shape_inside_a_text_line_is_not_a_control()
    {
        var line = new Rectangle(10, 30, 180, 22);
        var letter = new Rectangle(40, 40, 12, 14);

        Assert.True(TextLayout.ContainsMostly(line, letter));
        Assert.False(TextLayout.ContainsMostly(line, new Rectangle(40, 30, 12, 40)));
    }
}
