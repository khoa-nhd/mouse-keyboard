using System.Drawing;

namespace KeyboardMouse.Vision;

public enum VisualShape
{
    Rectangle,
    RoundedRectangle,
    Circle,
    Underline,
    Icon
}

/// <summary>Which strategy produced a candidate. Kept so merging can report and
/// tests can assert that a control was found by the intended detector.</summary>
public enum VisualDetector
{
    BorderEdge,
    FillRegion,
    Circle,
    Underline,
    Icon,
    Segmented,
    TextAssociation
}

/// <param name="Bounds">In original image pixels of the frame's source region.</param>
public sealed record VisualCandidate(Rectangle Bounds, VisualShape Shape, VisualDetector Detector, double Score)
{
    public int Area => Bounds.Width * Bounds.Height;

    public Point Center => new(
        Bounds.Left + (Bounds.Width / 2),
        Bounds.Top + (Bounds.Height / 2));

    public double Iou(VisualCandidate other) => Iou(Bounds, other.Bounds);

    public static double Iou(Rectangle a, Rectangle b)
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

/// <summary>
/// Everything one scale of one capture shares between detectors: grayscale,
/// edges, two polarity thresholds, line segments and blobs. Building these once
/// per scale keeps the five strategies cheap enough to run together.
/// </summary>
public sealed class DetectionFrame
{
    private readonly Lazy<GrayImage> _gray;
    private readonly Lazy<GrayImage> _edges;
    private readonly Lazy<int> _threshold;
    private readonly Lazy<GrayImage> _dark;
    private readonly Lazy<GrayImage> _light;
    private readonly Lazy<List<EdgeSegment>> _horizontal;
    private readonly Lazy<List<EdgeSegment>> _vertical;
    private readonly Lazy<List<Blob>> _edgeBlobs;
    private readonly Lazy<GrayImage> _closedEdges;
    private readonly Lazy<List<Blob>> _iconBlobs;
    private readonly Lazy<List<Rectangle>> _textLines;

    public DetectionFrame(Image32 color, double factor)
    {
        Color = color;
        Factor = factor;
        _gray = new Lazy<GrayImage>(() => GrayImage.FromImage(color));
        _edges = new Lazy<GrayImage>(() => Gray.Canny());
        _threshold = new Lazy<int>(() => Gray.OtsuThreshold());
        _dark = new Lazy<GrayImage>(() => Gray.Binarize(Threshold));
        _light = new Lazy<GrayImage>(() => Gray.Binarize(Threshold, darkIsForeground: false));
        _horizontal = new Lazy<List<EdgeSegment>>(() => EdgeSegments.Horizontal(Edges));
        _vertical = new Lazy<List<EdgeSegment>>(() => EdgeSegments.Vertical(Edges));
        _edgeBlobs = new Lazy<List<Blob>>(() => Blobs.FromBinary(Edges));
        _closedEdges = new Lazy<GrayImage>(() => Edges.Dilate(1));
        _iconBlobs = new Lazy<List<Blob>>(() => Blobs.FromBinary(ClosedEdges));
        _textLines = new Lazy<List<Rectangle>>(() => TextLayout.FindTextLines(this));
    }

    /// <summary>Processed pixels per source pixel; 1.0 is native resolution.</summary>
    public double Factor { get; }

    public Image32 Color { get; }

    public int Width => Color.Width;

    public int Height => Color.Height;

    public GrayImage Gray => _gray.Value;

    public GrayImage Edges => _edges.Value;

    public int Threshold => _threshold.Value;

    /// <summary>Dark pixels marked as foreground (light theme controls).</summary>
    public GrayImage DarkBinary => _dark.Value;

    /// <summary>Light pixels marked as foreground (dark theme controls).</summary>
    public GrayImage LightBinary => _light.Value;

    public List<EdgeSegment> Horizontal => _horizontal.Value;

    public List<EdgeSegment> Vertical => _vertical.Value;

    public List<Blob> EdgeBlobs => _edgeBlobs.Value;

    /// <summary>
    /// Edges with a one-pixel dilation. A 1.5-2 px glyph stroke antialiases into
    /// two faint parallel runs that the raw edge map keeps apart, so an icon used
    /// to arrive as several small fragments instead of one glyph.
    /// </summary>
    public GrayImage ClosedEdges => _closedEdges.Value;

    public List<Blob> IconBlobs => _iconBlobs.Value;

    public List<Rectangle> TextLines => _textLines.Value;

    public Rectangle Bounds => new(0, 0, Width, Height);

    /// <summary>Maps a processed-pixel rectangle back to source pixels.</summary>
    public Rectangle ToSource(Rectangle bounds)
    {
        double factor = Factor <= 0 ? 1 : Factor;
        int left = (int)Math.Floor(bounds.Left / factor);
        int top = (int)Math.Floor(bounds.Top / factor);
        int right = (int)Math.Ceiling(bounds.Right / factor);
        int bottom = (int)Math.Ceiling(bounds.Bottom / factor);

        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }
}
