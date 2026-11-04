using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using KeyboardMouse.Capture;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class ComputerVisionScannerTests
{
    [Fact]
    public void Scan_FindsRectangularRegions_FromPixels()
    {
        using var bitmap = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var pen = new Pen(Color.FromArgb(30, 30, 30), 2);
            graphics.DrawRectangle(pen, 40, 40, 80, 30);
            graphics.DrawRectangle(pen, 200, 80, 100, 40);
            graphics.DrawRectangle(pen, 60, 200, 60, 60);
        }

        using var capture = new StaticCaptureService(
            bitmap,
            new TargetBounds(1000, 500, 400, 300));
        var scanner = new ComputerVisionScanner(
            capture,
            timeout: TimeSpan.FromSeconds(5),
            maxElements: 100);

        HintScanResult? result = null;
        using var done = new ManualResetEventSlim();

        scanner.BeginScan(
            new HintScanRequest(
                new IntPtr(1),
                new HintScanOptions(100, false) { Sources = TargetSource.Cv }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "The CV scan did not finish.");
        Assert.NotNull(result);
        Assert.True(result!.HasTargets, "The CV scan should find the drawn rectangles.");
        Assert.All(result.Targets, target =>
        {
            Assert.True(target.Bounds.IsValid);
            Assert.True(target.IsActionable);
            Assert.True(
                target.Bounds.Left >= 1000 &&
                target.Bounds.Top >= 500 &&
                target.Bounds.Right <= 1400 &&
                target.Bounds.Bottom <= 800,
                $"Target escaped the capture region: {target.Bounds}");
        });

        // At least one detected box should sit on each drawn rectangle.
        Assert.Contains(result.Targets, target => ContainsCenter(target.Bounds, 1040, 540));
        Assert.Contains(result.Targets, target => ContainsCenter(target.Bounds, 1200, 580));
        Assert.Contains(result.Targets, target => ContainsCenter(target.Bounds, 1060, 700));
    }

    [Fact]
    public void Scan_ReturnsNoSyntheticTargets_ForBlankCapture()
    {
        using var bitmap = new Bitmap(320, 240, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        using var capture = new StaticCaptureService(
            bitmap,
            new TargetBounds(0, 0, 320, 240));
        var scanner = new ComputerVisionScanner(
            capture,
            timeout: TimeSpan.FromSeconds(5),
            maxElements: 100);

        HintScanResult? result = null;
        using var done = new ManualResetEventSlim();

        scanner.BeginScan(
            new HintScanRequest(
                new IntPtr(1),
                new HintScanOptions(100, false) { Sources = TargetSource.Cv }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotNull(result);
        Assert.Empty(result!.Targets);
        Assert.Equal(HintScanStatus.NoTargets, result.Status);
    }

    [Fact]
    public void Scan_RespectsSourceSelection()
    {
        using var bitmap = new Bitmap(100, 100);
        using var capture = new StaticCaptureService(bitmap, new TargetBounds(0, 0, 100, 100));
        var scanner = new ComputerVisionScanner(capture);

        HintScanResult? result = null;
        using var done = new ManualResetEventSlim();

        scanner.BeginScan(
            new HintScanRequest(
                IntPtr.Zero,
                new HintScanOptions(100, false) { Sources = TargetSource.Uia }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotNull(result);
        Assert.False(result!.Diagnostics?.CvUsed);
        Assert.Empty(result.Targets);
    }

    private static bool ContainsCenter(TargetBounds bounds, int x, int y) =>
        bounds.Contains(new Point(x, y));

    private sealed class StaticCaptureService : IScreenCaptureService
    {
        private readonly Bitmap _bitmap;
        private readonly TargetBounds _region;

        public StaticCaptureService(Bitmap bitmap, TargetBounds region)
        {
            _bitmap = bitmap;
            _region = region;
        }

        public CaptureResult CaptureForegroundWindow(CancellationToken cancellationToken = default) =>
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
