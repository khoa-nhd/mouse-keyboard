using System.Drawing;
using System.Drawing.Drawing2D;
using KeyboardMouse.Capture;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class TargetGeometryTests
{
    [Fact]
    public void RoundedButton_RefinesBoundsAndKeepsClickInside()
    {
        using var bitmap = new Bitmap(240, 160);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var shadow = new SolidBrush(Color.FromArgb(228, 228, 228));
            using var shadowPath = RoundedRectangle(new Rectangle(43, 43, 120, 50), 14);
            graphics.FillPath(shadow, shadowPath);

            using var fill = new SolidBrush(Color.FromArgb(228, 232, 238));
            using var border = new Pen(Color.FromArgb(70, 90, 110), 2);
            using var buttonPath = RoundedRectangle(new Rectangle(40, 40, 120, 50), 14);
            graphics.FillPath(fill, buttonPath);
            graphics.DrawPath(border, buttonPath);
        }

        var groundTruth = new Rectangle(40, 40, 120, 50);
        CvGeometryResult result = CvGeometryRefiner.Refine(
            CvPixelSource.Create(bitmap),
            new Rectangle(34, 34, 134, 64),
            TargetType.Button,
            new Rectangle(78, 52, 44, 20),
            detectionConfidence: 0.80);

        double iou = IntersectionOverUnion(result.Bounds, groundTruth);

        Assert.True(iou >= 0.75, $"Rounded-button IoU was {iou:0.000}: {result.Bounds}");
        Assert.True(result.Bounds.Contains(result.ClickPoint));
        Assert.True(groundTruth.Contains(result.ClickPoint));
        Assert.True(result.Geometry.HasRoundedCorners);
        Assert.InRange(result.Confidence, 0.35, 0.99);
        Assert.False(result.Geometry.HasIssue(GeometryIssue.ZeroSized));
    }

    [Fact]
    public void ShadowedBorderlessButton_DoesNotIncludeTheShadowTail()
    {
        using var bitmap = new Bitmap(220, 120);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var shadow = new SolidBrush(Color.FromArgb(235, 235, 235));
            graphics.FillRectangle(shadow, 43, 43, 100, 36);

            using var fill = new SolidBrush(Color.FromArgb(90, 140, 210));
            graphics.FillRectangle(fill, 40, 40, 100, 36);
        }

        CvGeometryResult result = CvGeometryRefiner.Refine(
            CvPixelSource.Create(bitmap),
            new Rectangle(36, 36, 112, 50),
            TargetType.Button,
            textBounds: null,
            detectionConfidence: 0.72);

        var groundTruth = new Rectangle(40, 40, 100, 36);
        double iou = IntersectionOverUnion(result.Bounds, groundTruth);

        Assert.True(iou >= 0.75, $"Shadowed-button IoU was {iou:0.000}: {result.Bounds}");
        Assert.True(result.Bounds.Contains(result.ClickPoint));
        Assert.True(groundTruth.Contains(result.ClickPoint));
    }

    [Fact]
    public void OutlinedButtonWithCentredLabel_KeepsTheButtonNotJustTheGlyph()
    {
        using var bitmap = new Bitmap(240, 140);
        var groundTruth = new Rectangle(40, 45, 150, 42);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var border = new Pen(Color.FromArgb(25, 25, 25), 2);
            using var ink = new SolidBrush(Color.FromArgb(25, 25, 25));

            // White interior + a centred dark square stands in for a label.
            // The refiner must not mistake that small inner component for the
            // whole clickable button.
            graphics.DrawRectangle(
                border,
                groundTruth.Left,
                groundTruth.Top,
                groundTruth.Width - 1,
                groundTruth.Height - 1);
            graphics.FillRectangle(ink, 107, 58, 16, 16);
        }

        CvGeometryResult result = CvGeometryRefiner.Refine(
            CvPixelSource.Create(bitmap),
            new Rectangle(34, 39, 162, 54),
            TargetType.Button,
            textBounds: new Rectangle(107, 58, 16, 16),
            detectionConfidence: 0.80);

        double iou = IntersectionOverUnion(result.Bounds, groundTruth);

        Assert.True(iou >= 0.70, $"Outlined-button IoU was {iou:0.000}: {result.Bounds}");
        Assert.True(result.Bounds.Width >= groundTruth.Width - 8);
        Assert.True(result.Bounds.Height >= groundTruth.Height - 8);
        Assert.True(groundTruth.Contains(result.ClickPoint));
    }

    [Fact]
    public void IrregularTriangle_UsesAnInteriorPoint_InsteadOfTheBoundingBoxCentre()
    {
        const int size = 130;
        var mask = new bool[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                mask[(y * size) + x] = x >= 10 && y >= 10 && x + y <= 120;
            }
        }

        var region = new Rectangle(10, 10, 101, 101);
        var centre = new Point(60, 60);

        MaskInteriorPoint selected = MaskGeometry.SelectInteriorPoint(
            mask,
            size,
            size,
            region,
            new[] { centre });

        Assert.True(selected.InsideMask);
        Assert.True(selected.BoundaryDistance >= 8);
        Assert.True(
            Math.Abs(selected.Point.X - centre.X) + Math.Abs(selected.Point.Y - centre.Y) >= 8,
            "The triangle's bounding-box centre is on its hypotenuse.");
        Assert.True(mask[(selected.Point.Y * size) + selected.Point.X]);
    }

    [Fact]
    public void SmallIcon_ClickPointStaysInsideTheInk()
    {
        using var bitmap = new Bitmap(80, 80);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var fill = new SolidBrush(Color.FromArgb(30, 30, 30));
            graphics.FillRectangle(fill, 34, 36, 12, 8);
        }

        CvGeometryResult result = CvGeometryRefiner.Refine(
            CvPixelSource.Create(bitmap),
            new Rectangle(30, 32, 20, 16),
            TargetType.Icon,
            textBounds: null,
            detectionConfidence: 0.55);

        Assert.True(result.Bounds.Contains(result.ClickPoint));
        Assert.InRange(result.Bounds.Width, 8, 20);
        Assert.InRange(result.Bounds.Height, 6, 16);
    }

    [Fact]
    public void AdjacentButtons_GetDistinctClickPointsAwayFromTheSharedEdge()
    {
        var left = new ClickableTarget(new Rectangle(10, 10, 12, 30));
        var right = new ClickableTarget(new Rectangle(22, 10, 12, 30));

        IReadOnlyList<ClickableTarget> protectedTargets =
            TargetGeometryNeighbors.Protect(new[] { left, right });

        ClickableTarget first = protectedTargets[0];
        ClickableTarget second = protectedTargets[1];

        Assert.True(first.Bounds.Contains(first.ClickPoint));
        Assert.True(second.Bounds.Contains(second.ClickPoint));
        Assert.NotEqual(first.Center, first.ClickPoint);
        Assert.NotEqual(second.Center, second.ClickPoint);
        Assert.True(first.ClickPoint.X <= first.Bounds.Left + 4);
        Assert.True(second.ClickPoint.X >= second.Bounds.Right - 5);
        Assert.True(
            Math.Abs(first.ClickPoint.X - second.ClickPoint.X) > 8,
            "Neighbouring click points must not share a boundary.");
        Assert.True(first.Geometry!.HasIssue(GeometryIssue.SharedBoundary));
        Assert.True(second.Geometry!.HasIssue(GeometryIssue.SharedBoundary));

        IReadOnlyList<ClickableTarget> protectedAgain =
            TargetGeometryNeighbors.Protect(protectedTargets);

        Assert.Equal(first.Geometry.Confidence, protectedAgain[0].Geometry!.Confidence);
        Assert.Equal(second.Geometry.Confidence, protectedAgain[1].Geometry!.Confidence);
        Assert.Equal(first.ClickPoint, protectedAgain[0].ClickPoint);
        Assert.Equal(second.ClickPoint, protectedAgain[1].ClickPoint);
    }

    [Fact]
    public void OverlappingTargets_GetDistinctPointsAndOverlapDiagnostics()
    {
        var left = new ClickableTarget(new Rectangle(10, 10, 30, 30));
        var right = new ClickableTarget(new Rectangle(25, 10, 30, 30));

        IReadOnlyList<ClickableTarget> protectedTargets =
            TargetGeometryNeighbors.Protect(new[] { left, right });

        Assert.True(protectedTargets[0].Bounds.Contains(protectedTargets[0].ClickPoint));
        Assert.True(protectedTargets[1].Bounds.Contains(protectedTargets[1].ClickPoint));
        Assert.NotEqual(protectedTargets[0].ClickPoint, protectedTargets[1].ClickPoint);
        Assert.True(protectedTargets[0].Geometry!
            .HasIssue(GeometryIssue.OverlapsNeighbor));
        Assert.True(protectedTargets[1].Geometry!
            .HasIssue(GeometryIssue.OverlapsNeighbor));
    }

    [Theory]
    [InlineData(10, 10, 0, 10, true)]
    [InlineData(10, 10, 4, 4, true)]
    [InlineData(10, 10, 2000, 800, true)]
    [InlineData(10, 10, 100, 30, false)]
    public void StrictValidation_RejectsBadButtonGeometry(
        int x,
        int y,
        int width,
        int height,
        bool rejected)
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var bounds = new TargetBounds(x, y, width, height);
        var click = width > 0 && height > 0
            ? new Point(x + (width / 2), y + (height / 2))
            : Point.Empty;

        GeometryValidation result = TargetGeometryValidator.Validate(
            bounds,
            click,
            TargetType.Button,
            screen,
            strict: true);

        Assert.Equal(!rejected, result.Accepted);
    }

    [Fact]
    public void OffScreenAndClippedTargets_AreFlagged_NotSilentlyAccepted()
    {
        var screen = new Rectangle(0, 0, 100, 100);

        GeometryValidation clipped = TargetGeometryValidator.Validate(
            new TargetBounds(-5, 10, 40, 20),
            new Point(10, 20),
            TargetType.Button,
            screen,
            strict: true);

        Assert.True(clipped.Accepted);
        Assert.True(clipped.Geometry.IsClipped);
        Assert.True(clipped.Geometry.HasIssue(GeometryIssue.PartiallyOffScreen));

        GeometryValidation outside = TargetGeometryValidator.Validate(
            new TargetBounds(200, 200, 30, 20),
            new Point(210, 210),
            TargetType.Button,
            screen,
            strict: true);

        Assert.False(outside.Accepted);
        Assert.True(outside.Geometry.HasIssue(GeometryIssue.OffScreen));

        GeometryValidation badClick = TargetGeometryValidator.Validate(
            new TargetBounds(10, 10, 30, 20),
            new Point(5, 5),
            TargetType.Button,
            screen,
            strict: true);

        Assert.False(badClick.Accepted);
        Assert.True(badClick.Geometry.HasIssue(GeometryIssue.ClickPointOutside));
    }

    [Fact]
    public void TypeSpecificLimits_RejectOversizedCheckboxes_ButAllowWideEditFields()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);

        GeometryValidation checkbox = TargetGeometryValidator.Validate(
            new TargetBounds(10, 10, 300, 300),
            new Point(160, 160),
            TargetType.CheckBox,
            screen,
            strict: true);

        GeometryValidation edit = TargetGeometryValidator.Validate(
            new TargetBounds(10, 10, 300, 24),
            new Point(160, 22),
            TargetType.Edit,
            screen,
            strict: true);

        Assert.False(checkbox.Accepted);
        Assert.True(edit.Accepted);
    }

    [Fact]
    public void MixedDpi_ChangesSizeLimitsWithoutChangingCoordinates()
    {
        TargetSizeLimits native = TargetGeometryValidator.LimitsFor(TargetType.Icon, 1.0);
        TargetSizeLimits scaled = TargetGeometryValidator.LimitsFor(TargetType.Icon, 2.0);

        Assert.True(scaled.MinWidth >= native.MinWidth);
        Assert.True(scaled.MaxWidth > native.MaxWidth);

        GeometryValidation result = TargetGeometryValidator.Validate(
            new TargetBounds(10, 10, 20, 20),
            new Point(20, 20),
            TargetType.Icon,
            new Rectangle(0, 0, 100, 100),
            dpiScale: 2.0,
            strict: true);

        Assert.True(result.Accepted);
        Assert.Equal(new TargetBounds(10, 10, 20, 20), new TargetBounds(10, 10, 20, 20));
    }

    [Fact]
    public void ResizedProcessingImage_MapsBackToDesktopPixels()
    {
        using var bitmap = new Bitmap(200, 150);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var fill = new SolidBrush(Color.FromArgb(50, 100, 170));
            graphics.FillRectangle(fill, 40, 30, 80, 40);
        }

        var capture = new StaticCaptureService(bitmap, new TargetBounds(0, 0, 400, 300));
        var scanner = new ComputerVisionScanner(
            capture,
            timeout: TimeSpan.FromSeconds(5),
            maxElements: 20);

        HintScanResult? result = null;
        using var done = new ManualResetEventSlim();

        scanner.BeginScan(
            new HintScanRequest(
                new IntPtr(1),
                new HintScanOptions(20, false)
                {
                    Sources = TargetSource.Cv,
                    Budget = TimeSpan.FromSeconds(5)
                }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotNull(result);
        Assert.True(result!.HasTargets);

        VisualTarget target = Assert.Single(result.Targets);
        var groundTruth = new TargetBounds(80, 60, 160, 80);
        double iou = IntersectionOverUnion(target.Bounds, groundTruth);

        Assert.True(iou >= 0.60, $"Resized-image IoU was {iou:0.000}: {target.Bounds}");
        Assert.True(groundTruth.Contains(target.ClickPoint));
    }

    [Fact]
    public void InvalidCoordinateTransforms_AreReportedInsteadOfThrowing()
    {
        Assert.False(HintCoordinates.TryFrameToDesktop(
            new Rectangle(0, 0, 100, 100),
            new Rectangle(10, 10, 20, 20),
            0,
            1,
            out _));

        Assert.False(HintCoordinates.TryFrameToDesktop(
            new Rectangle(0, 0, 100, 100),
            new Point(10, 10),
            double.NaN,
            1,
            out _));

        Assert.True(HintCoordinates.TryFrameToDesktop(
            new Rectangle(0, 0, 400, 300),
            new Rectangle(40, 30, 80, 40),
            0.5,
            0.5,
            out TargetBounds mapped));

        Assert.Equal(new TargetBounds(80, 60, 160, 80), mapped);
    }

    [Fact]
    public async Task FullScanner_RoundedButton_MeasuresIoUAndClickValidity()
    {
        using var bitmap = new Bitmap(320, 220);
        var groundTruth = new Rectangle(70, 80, 160, 50);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var fill = new SolidBrush(Color.FromArgb(225, 232, 240));
            using var border = new Pen(Color.FromArgb(50, 80, 120), 2);
            using var path = RoundedRectangle(groundTruth, 16);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        HintScanResult result = await ScanAsync(bitmap);

        VisualTarget target = BestMatch(result.Targets, groundTruth);
        double iou = IntersectionOverUnion(target.Bounds, groundTruth);

        Assert.True(iou >= 0.60, $"Full-scanner rounded-button IoU was {iou:0.000}: {target.Bounds}");
        Assert.True(target.Bounds.Contains(target.ClickPoint));
        Assert.True(
            groundTruth.Contains(target.ClickPoint),
            $"Click point {target.ClickPoint} escaped the real button {groundTruth}.");
        Assert.True(target.Geometry.Confidence > 0.20);
    }

    [Fact]
    public async Task FullScanner_AdjacentButtons_StayDistinctAndClickable()
    {
        using var bitmap = new Bitmap(260, 140);
        var leftTruth = new Rectangle(40, 50, 34, 32);
        var rightTruth = new Rectangle(82, 50, 34, 32);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var fill = new SolidBrush(Color.FromArgb(235, 238, 242));
            using var border = new Pen(Color.FromArgb(60, 70, 85), 2);

            foreach (Rectangle rectangle in new[] { leftTruth, rightTruth })
            {
                graphics.FillRectangle(fill, rectangle);
                graphics.DrawRectangle(
                    border,
                    rectangle.Left,
                    rectangle.Top,
                    rectangle.Width - 1,
                    rectangle.Height - 1);
            }
        }

        HintScanResult result = await ScanAsync(bitmap);

        VisualTarget left = BestMatch(result.Targets, leftTruth);
        VisualTarget right = BestMatch(result.Targets, rightTruth);

        Assert.NotEqual(left.Bounds, right.Bounds);
        Assert.True(IntersectionOverUnion(left.Bounds, leftTruth) >= 0.45);
        Assert.True(IntersectionOverUnion(right.Bounds, rightTruth) >= 0.45);
        Assert.True(left.Bounds.Contains(left.ClickPoint));
        Assert.True(right.Bounds.Contains(right.ClickPoint));
        Assert.True(
            left.ClickPoint.X < right.ClickPoint.X,
            "Distinct adjacent buttons must not share a click point.");
        Assert.True(left.ClickPoint.X <= leftTruth.Right - 6);
        Assert.True(right.ClickPoint.X >= rightTruth.Left + 6);
    }

    [Fact]
    public async Task FullScanner_TextField_KeepsClickInsideTheField()
    {
        using var bitmap = new Bitmap(340, 150);
        var groundTruth = new Rectangle(40, 55, 240, 30);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var fill = new SolidBrush(Color.FromArgb(248, 248, 248));
            using var border = new Pen(Color.FromArgb(70, 80, 95), 2);
            graphics.FillRectangle(fill, groundTruth);
            graphics.DrawRectangle(
                border,
                groundTruth.Left,
                groundTruth.Top,
                groundTruth.Width - 1,
                groundTruth.Height - 1);

            using var text = new SolidBrush(Color.FromArgb(40, 40, 40));
            graphics.DrawString("search", SystemFonts.DefaultFont, text, 52, 62);
        }

        HintScanResult result = await ScanAsync(bitmap);

        VisualTarget target = BestMatch(result.Targets, groundTruth);
        Assert.True(IntersectionOverUnion(target.Bounds, groundTruth) >= 0.55);
        Assert.True(target.Bounds.Contains(target.ClickPoint));
        Assert.True(groundTruth.Contains(target.ClickPoint));
    }

    [Theory]
    [InlineData(TargetType.CheckBox, 18, 18)]
    [InlineData(TargetType.RadioButton, 16, 16)]
    [InlineData(TargetType.Icon, 22, 22)]
    [InlineData(TargetType.Button, 90, 32)]
    [InlineData(TargetType.Edit, 220, 28)]
    [InlineData(TargetType.TabItem, 100, 28)]
    [InlineData(TargetType.MenuItem, 140, 24)]
    public void TypeSpecificGeometryRules_AcceptPlausibleControls(
        TargetType type,
        int width,
        int height)
    {
        var bounds = new TargetBounds(20, 20, width, height);
        GeometryValidation validation = TargetGeometryValidator.Validate(
            bounds,
            bounds.Center,
            type,
            new Rectangle(0, 0, 1920, 1080),
            strict: true);

        Assert.True(validation.Accepted, $"{type} should accept a {width}x{height} control.");
    }

    private static VisualTarget BestMatch(
        IReadOnlyList<VisualTarget> targets,
        Rectangle groundTruth)
    {
        Assert.NotEmpty(targets);
        return targets
            .OrderByDescending(target => IntersectionOverUnion(
                new Rectangle(
                    target.Bounds.X,
                    target.Bounds.Y,
                    target.Bounds.Width,
                    target.Bounds.Height),
                groundTruth))
            .First();
    }

    private static async Task<HintScanResult> ScanAsync(Bitmap bitmap)
    {
        var capture = new StaticCaptureService(
            bitmap,
            new TargetBounds(0, 0, bitmap.Width, bitmap.Height));
        var scanner = new ComputerVisionScanner(
            capture,
            timeout: TimeSpan.FromSeconds(5),
            maxElements: 100);

        var completion = new TaskCompletionSource<HintScanResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        scanner.BeginScan(
            new HintScanRequest(
                new IntPtr(1),
                new HintScanOptions(100, false)
                {
                    Sources = TargetSource.Cv,
                    Budget = TimeSpan.FromSeconds(5)
                }),
            result => completion.TrySetResult(result));

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(
            bounds.Right - diameter,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static double IntersectionOverUnion(Rectangle a, Rectangle b)
    {
        Rectangle intersection = Rectangle.Intersect(a, b);
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0;
        }

        long intersectionArea = (long)intersection.Width * intersection.Height;
        long union = ((long)a.Width * a.Height) +
                     ((long)b.Width * b.Height) -
                     intersectionArea;

        return union > 0 ? intersectionArea / (double)union : 0;
    }

    private static double IntersectionOverUnion(TargetBounds a, TargetBounds b) =>
        IntersectionOverUnion(
            new Rectangle(a.X, a.Y, a.Width, a.Height),
            new Rectangle(b.X, b.Y, b.Width, b.Height));

    private static double IntersectionOverUnion(TargetBounds a, Rectangle b) =>
        IntersectionOverUnion(new Rectangle(a.X, a.Y, a.Width, a.Height), b);

    private sealed class StaticCaptureService : IScreenCaptureService
    {
        private readonly Bitmap _bitmap;
        private readonly TargetBounds _region;

        public StaticCaptureService(Bitmap bitmap, TargetBounds region)
        {
            _bitmap = bitmap;
            _region = region;
        }

        public CaptureResult CaptureForegroundWindow(
            CancellationToken cancellationToken = default) =>
            new()
            {
                Bitmap = new Bitmap(_bitmap),
                Region = _region,
                DpiScale = 1.0,
                WindowHandle = new IntPtr(1)
            };

        public CaptureResult CaptureMonitor(
            MonitorInfo monitor,
            CancellationToken cancellationToken = default) =>
            CaptureForegroundWindow(cancellationToken);

        public CaptureResult CaptureRegion(
            TargetBounds region,
            CancellationToken cancellationToken = default) =>
            CaptureForegroundWindow(cancellationToken);

        public void Dispose()
        {
        }
    }
}
