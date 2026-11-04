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

    [Fact]
    public void Scan_ExcludedRegions_AreNotReported()
    {
        using var bitmap = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var pen = new Pen(Color.FromArgb(30, 30, 30), 2);
            graphics.DrawRectangle(pen, 40, 40, 80, 30);
            graphics.DrawRectangle(pen, 200, 80, 100, 40);
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
                new HintScanOptions(100, false)
                {
                    Sources = TargetSource.Cv,
                    ExcludedRegions = [new TargetBounds(1030, 530, 100, 50)]
                }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotNull(result);
        Assert.True(result!.HasTargets);
        Assert.DoesNotContain(
            result.Targets,
            target => target.Bounds.Contains(new Point(1080, 555)));
        Assert.Contains(
            result.Targets,
            target => target.Bounds.Contains(new Point(1250, 600)));
    }

    [Fact]
    public void VirtualDesktopScope_CapturesEveryMonitor_IncludingNegativeOrigins()
    {
        var left = new MonitorInfo(
            new IntPtr(1),
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(-1920, 0, 1920, 1040),
            1.0);
        var primary = new MonitorInfo(
            new IntPtr(2),
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1040),
            1.5);

        using var capture = new RegionCaptureService();
        var scanner = new ComputerVisionScanner(
            capture,
            timeout: TimeSpan.FromSeconds(10),
            maxElements: 200,
            monitors: new FakeMonitorService(left, primary));

        HintScanResult? result = null;
        using var done = new ManualResetEventSlim();

        scanner.BeginScan(
            new HintScanRequest(
                new IntPtr(1),
                new HintScanOptions(200, false)
                {
                    Sources = TargetSource.Cv,
                    Scope = HintScanScope.VirtualDesktop,
                    Budget = TimeSpan.FromSeconds(10)
                }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(15)));
        Assert.NotNull(result);
        Assert.True(result!.HasTargets);

        // Each monitor is wider than 1280 px, so both are split into two tiles.
        Assert.Equal(4, capture.Requested.Count);
        Assert.All(capture.Requested, region =>
        {
            Assert.True(region.Width <= ScreenScanPlanner.MaxTileDimension);
            Assert.True(region.Height <= ScreenScanPlanner.MaxTileDimension);
            Assert.True(
                region.Left >= -1920 && region.Right <= 1920,
                $"Capture escaped the desktop: {region}");
        });

        Assert.Contains(result.Targets, target => target.Bounds.Left < 0);
        Assert.Contains(result.Targets, target => target.Bounds.Left >= 0);
        Assert.All(result.Targets, target =>
            Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0));
    }

    [Fact]
    public void LargeMonitor_IsSplitIntoTiles_ThatCoverTheWholeArea()
    {
        var monitor = new MonitorInfo(
            new IntPtr(1),
            new Rectangle(0, 0, 3000, 1600),
            new Rectangle(0, 0, 3000, 1560),
            1.0);

        using var capture = new RegionCaptureService();
        var scanner = new ComputerVisionScanner(
            capture,
            timeout: TimeSpan.FromSeconds(10),
            maxElements: 200,
            monitors: new FakeMonitorService(monitor));

        HintScanResult? result = null;
        using var done = new ManualResetEventSlim();

        scanner.BeginScan(
            new HintScanRequest(
                new IntPtr(1),
                new HintScanOptions(200, false)
                {
                    Sources = TargetSource.Cv,
                    Scope = HintScanScope.ActiveMonitor,
                    Budget = TimeSpan.FromSeconds(10)
                }),
            value =>
            {
                result = value;
                done.Set();
            });

        Assert.True(done.Wait(TimeSpan.FromSeconds(15)));
        Assert.NotNull(result);

        // 3000x1600 needs three columns and two rows at 1280 px per tile.
        Assert.Equal(6, capture.Requested.Count);
        Assert.All(capture.Requested, region =>
        {
            Assert.True(region.Width <= 1280);
            Assert.True(region.Height <= 1280);
        });
        Assert.Contains(capture.Requested, region => region.Contains(new Point(0, 0)));
        Assert.Contains(capture.Requested, region => region.Contains(new Point(2999, 1599)));
    }

    private static bool ContainsCenter(TargetBounds bounds, int x, int y) =>
        bounds.Contains(new Point(x, y));

    /// <summary>
    /// Returns a bitmap for every requested region with one outlined control
    /// near its top-left corner, so scope and tiling can be verified without a
    /// visible desktop.
    /// </summary>
    private sealed class RegionCaptureService : IScreenCaptureService
    {
        public List<TargetBounds> Requested { get; } = [];

        public CaptureResult CaptureForegroundWindow(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Scope tests must not fall back to a window capture.");

        public CaptureResult CaptureMonitor(
            MonitorInfo monitor,
            CancellationToken cancellationToken = default) =>
            CaptureRegion(TargetBounds.FromRectangle(monitor.Bounds), cancellationToken);

        public CaptureResult CaptureRegion(
            TargetBounds region,
            CancellationToken cancellationToken = default)
        {
            Requested.Add(region);

            var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                using var pen = new Pen(Color.FromArgb(30, 30, 30), 2);
                graphics.DrawRectangle(
                    pen,
                    40,
                    40,
                    Math.Min(120, Math.Max(20, region.Width - 60)),
                    Math.Min(40, Math.Max(12, region.Height - 60)));
            }

            return new CaptureResult
            {
                Bitmap = bitmap,
                Region = region,
                DpiScale = 1.0
            };
        }

        public void Dispose()
        {
        }
    }

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
