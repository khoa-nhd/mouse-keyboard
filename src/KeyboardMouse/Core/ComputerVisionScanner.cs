using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using KeyboardMouse.Core;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Capture;

/// <summary>
/// Computer vision hint scanner. Captures the foreground window with GDI and
/// finds rectangular UI regions by classical pixel analysis: greyscale
/// downsampling, gradient thresholding, edge dilation and connected components.
/// No model files, no cloud calls and no synthetic rectangles.
///
/// OCR is optional. When an <see cref="IOcrEngine"/> is available its word
/// boxes add names/confidence; without one the detector still returns contours
/// so custom-drawn interfaces get hints.
/// </summary>
public sealed class ComputerVisionScanner : IHintScanner
{
    private const int MaxAnalysisDimension = 640;
    private const int MinComponentPixels = 6;
    private readonly DiagnosticsLevel _diagnosticsLevel = DiagnosticsLevel.Basic;
    private readonly IMonitorService? _monitors;
    private readonly IScreenCaptureService _capture;
    private readonly IOcrEngine _ocr;
    private readonly TimeSpan _hardTimeout;
    private readonly int _maxElements;
    private readonly int _maxComponents;

    /// <summary>
    /// Constructs a CV scanner. The <paramref name="ocr"/> parameter may be
    /// <c>null</c>; in that case only the pixel detector is used.
    /// </summary>
    public ComputerVisionScanner(
        IScreenCaptureService capture,
        IOcrEngine? ocr = null,
        TimeSpan? timeout = null,
        int maxElements = 200,
        int visitedLimit = 5000,
        IMonitorService? monitors = null,
        DiagnosticsLevel diagnosticsLevel = DiagnosticsLevel.Basic)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _ocr = ocr ?? NullOcrEngine.Instance;
        _monitors = monitors;
        _diagnosticsLevel = diagnosticsLevel;
        _hardTimeout = timeout is { } value && value > TimeSpan.Zero
            ? value
            : TimeSpan.FromMilliseconds(400);
        _maxElements = Math.Clamp(maxElements, 1, 2000);
        _maxComponents = Math.Clamp(visitedLimit, 16, 20000);
    }

    public void BeginScan(HintScanRequest request, Action<HintScanResult> completed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completed);

        if ((request.Options.Sources & TargetSource.Cv) == 0)
        {
            completed(HintScanResult.FromTargets(
                [],
                new HintScanDiagnostics(UiaUsed: false, CvUsed: false)));
            return;
        }

        if (request.Window == IntPtr.Zero)
        {
            completed(HintScanResult.Failure(
                HintScanStatus.Failed,
                "No foreground window handle provided."));
            return;
        }

        var thread = new Thread(() =>
        {
            HintScanResult result;

            try
            {
                result = ScanCore(request);
            }
            catch (OperationCanceledException)
            {
                result = HintScanResult.Failure(
                    HintScanStatus.TimedOut,
                    "The CV hint scan timed out.");
            }
            catch (Exception ex)
            {
                Log.Error("CV hint scan failed", ex);
                result = HintScanResult.Failure(
                    HintScanStatus.Failed,
                    "The CV hint scan failed.");
            }

            completed(result);
        })
        {
            IsBackground = true,
            Name = "KeyboardMouse.CVScan"
        };

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private HintScanResult ScanCore(HintScanRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(_hardTimeout);
        cts.Token.ThrowIfCancellationRequested();

        CaptureResult capture;

        try
        {
            // Capture the window the caller asked for, not whatever happens to
            // be foreground once this worker thread runs. Hint mode stores the
            // foreground handle before the scan starts.
            if (User32.GetWindowRect(request.Window, out RECT windowRect) &&
                windowRect.Width > 0 &&
                windowRect.Height > 0)
            {
                capture = _capture.CaptureRegion(
                    new TargetBounds(
                        windowRect.Left,
                        windowRect.Top,
                        windowRect.Width,
                        windowRect.Height),
                    cts.Token);
            }
            else
            {
                capture = _capture.CaptureForegroundWindow(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return HintScanResult.Failure(
                HintScanStatus.TimedOut,
                "The CV hint scan timed out during capture.");
        }
        catch (Exception ex)
        {
            Log.Error("CV hint capture failed", ex);
            return HintScanResult.Failure(
                HintScanStatus.Failed,
                "The CV hint scan could not capture the foreground window.");
        }

        using Bitmap bitmap = capture.Bitmap;

        IReadOnlyList<(string Text, TargetBounds Bounds, double Confidence)> ocrTextBoxes;
        try
        {
            ocrTextBoxes = ConvertOcrWordsToBoxes(_ocr.Recognize(capture, cts.Token));
        }
        catch (OperationCanceledException)
        {
            return HintScanResult.Failure(
                HintScanStatus.TimedOut,
                "The CV hint scan timed out during OCR.");
        }
        catch (Exception ex)
        {
            // OCR is optional: a broken engine must not hide contour targets.
            Log.Error("CV hint OCR failed", ex);
            ocrTextBoxes = [];
        }

        List<Rectangle> candidates = DetectCandidates(bitmap, cts.Token);
        List<VisualTarget> targets = BuildTargets(candidates, capture, ocrTextBoxes);
        List<VisualTarget> filtered = ApplyPostProcessing(targets, _maxElements);

        stopwatch.Stop();
        IReadOnlyList<CandidateDiagnostics>? candidateDiagnostics = null;
        if (_diagnosticsLevel >= DiagnosticsLevel.Extended)
        {
            candidateDiagnostics = candidates.Select(c => new CandidateDiagnostics
            {
                Source = TargetSource.CvContour,
                Detector = "edge-components",
                PerimeterDensity = 0.5,
                GeometryConfidence = filtered.Any(t => t.Bounds.Contains(TargetBounds.FromRectangle(c))) ? 1.0 : 0.3,
                RejectionReason = null,
                MergeReason = null
            }).ToList();
        }

        var diagnostics = new HintScanDiagnostics(
            CandidateCount: candidates.Count,
            AcceptedCount: filtered.Count,
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
            UiaUsed: false,
            CvUsed: true,
            CandidateDiagnostics: candidateDiagnostics);

        Log.Info(
            $"CV hint scan found {filtered.Count} target(s) from " +
            $"{candidates.Count} candidate(s) in {diagnostics.ElapsedMilliseconds} ms.");

        return HintScanResult.FromTargets(filtered, diagnostics);
    }

    /// <summary>
    /// Converts OcrWord results into text boxes in capture-local coordinates.
    /// Adjacent words on the same row are merged into one label.
    /// </summary>
    private static IReadOnlyList<(string Text, TargetBounds Bounds, double Confidence)> ConvertOcrWordsToBoxes(
        IReadOnlyList<OcrWord> words)
    {
        var result = new List<(string Text, TargetBounds Bounds, double Confidence)>();
        if (words.Count == 0)
        {
            return result;
        }

        var sorted = words
            .Where(word => word.Bounds.IsValid)
            .OrderBy(word => word.Bounds.Top)
            .ThenBy(word => word.Bounds.Left)
            .ToList();

        var used = new bool[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            used[i] = true;
            int left = sorted[i].Bounds.Left;
            int top = sorted[i].Bounds.Top;
            int right = sorted[i].Bounds.Right;
            int bottom = sorted[i].Bounds.Bottom;
            string text = sorted[i].Text;
            double confidence = sorted[i].Confidence;

            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (used[j])
                {
                    continue;
                }

                OcrWord a = sorted[i];
                OcrWord b = sorted[j];
                bool sameRow = Math.Abs(a.Bounds.Center.Y - b.Bounds.Center.Y) <
                               Math.Max(12, a.Bounds.Height / 2);
                bool closeHorizontally =
                    Math.Abs(a.Bounds.Center.X - b.Bounds.Center.X) <
                    ((a.Bounds.Width + b.Bounds.Width) / 2) + 8;

                if (!sameRow || !closeHorizontally)
                {
                    continue;
                }

                left = Math.Min(left, b.Bounds.Left);
                top = Math.Min(top, b.Bounds.Top);
                right = Math.Max(right, b.Bounds.Right);
                bottom = Math.Max(bottom, b.Bounds.Bottom);
                text = string.IsNullOrEmpty(text) ? b.Text : text + " " + b.Text;
                confidence = Math.Max(confidence, b.Confidence);
                used[j] = true;
            }

            result.Add((
                text,
                new TargetBounds(left, top, right - left, bottom - top),
                confidence));
        }

        return result;
    }

    /// <summary>
    /// Pixel detector. Down-samples the capture into a greyscale grid, marks
    /// pixels whose local gradient stands out, dilates those edges so letters
    /// and thin outlines connect, then extracts connected components as boxes.
    /// </summary>
    private static List<Rectangle> DetectCandidates(Bitmap bitmap, CancellationToken cancellationToken)
    {
        if (bitmap.Width < 16 || bitmap.Height < 16)
        {
            return [];
        }

        int step = Math.Max(
            1,
            (int)Math.Ceiling(Math.Max(bitmap.Width, bitmap.Height) / (double)MaxAnalysisDimension));
        int gridWidth = Math.Max(3, bitmap.Width / step);
        int gridHeight = Math.Max(3, bitmap.Height / step);

        byte[] grey = DownsampleGrey(bitmap, gridWidth, gridHeight, step, cancellationToken);

        // Adaptive edge threshold: flat UIs stay sensitive, busy photos do not
        // turn every texture into a hint.
        long gradientSum = 0;
        int gradientSamples = 0;

        for (int y = 1; y < gridHeight - 1; y++)
        {
            for (int x = 1; x < gridWidth - 1; x++)
            {
                int index = (y * gridWidth) + x;
                int gx = grey[index + 1] - grey[index - 1];
                int gy = grey[index + gridWidth] - grey[index - gridWidth];
                gradientSum += Math.Abs(gx) + Math.Abs(gy);
                gradientSamples++;
            }
        }

        if (gradientSamples == 0)
        {
            return [];
        }

        int threshold = Math.Clamp(
            (int)((gradientSum / (double)gradientSamples) * 2.0),
            22,
            60);

        var edges = new bool[gridWidth * gridHeight];

        for (int y = 1; y < gridHeight - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int row = y * gridWidth;

            for (int x = 1; x < gridWidth - 1; x++)
            {
                int index = row + x;
                int gx = grey[index + 1] - grey[index - 1];
                int gy = grey[index + gridWidth] - grey[index - gridWidth];

                if (Math.Abs(gx) + Math.Abs(gy) >= threshold)
                {
                    edges[index] = true;
                }
            }
        }

        int radius = step <= 2 ? 2 : 1;
        edges = Dilate(edges, gridWidth, gridHeight, radius);

        return FindComponents(
            edges,
            gridWidth,
            gridHeight,
            step,
            bitmap.Width,
            bitmap.Height,
            cancellationToken);
    }

    /// <summary>
    /// Copies one pixel per grid cell into a grey buffer. The bitmap is locked
    /// for the whole copy, so no per-pixel GDI+ calls are made.
    /// </summary>
    private static byte[] DownsampleGrey(
        Bitmap bitmap,
        int gridWidth,
        int gridHeight,
        int step,
        CancellationToken cancellationToken)
    {
        var grey = new byte[gridWidth * gridHeight];
        Bitmap? converted = null;
        Bitmap source = bitmap;

        if (bitmap.PixelFormat != PixelFormat.Format32bppArgb &&
            bitmap.PixelFormat != PixelFormat.Format32bppPArgb &&
            bitmap.PixelFormat != PixelFormat.Format32bppRgb)
        {
            converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(converted))
            {
                graphics.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }

            source = converted;
        }

        try
        {
            var bounds = new Rectangle(0, 0, source.Width, source.Height);
            BitmapData data = source.LockBits(bounds, ImageLockMode.ReadOnly, source.PixelFormat);

            try
            {
                int stride = Math.Abs(data.Stride);
                bool bottomUp = data.Stride < 0;
                var row = new byte[stride];

                for (int gy = 0; gy < gridHeight; gy++)
                {
                    if ((gy & 31) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    int sy = Math.Min(source.Height - 1, gy * step);
                    int sourceY = bottomUp ? source.Height - 1 - sy : sy;
                    IntPtr rowPointer = IntPtr.Add(data.Scan0, sourceY * stride);
                    Marshal.Copy(rowPointer, row, 0, stride);

                    int rowBase = gy * gridWidth;

                    for (int gx = 0; gx < gridWidth; gx++)
                    {
                        int sx = Math.Min(source.Width - 1, gx * step);
                        int offset = sx * 4;
                        int blue = row[offset];
                        int green = row[offset + 1];
                        int red = row[offset + 2];
                        grey[rowBase + gx] = (byte)((red * 77 + green * 150 + blue * 29) >> 8);
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
        }
        finally
        {
            converted?.Dispose();
        }

        return grey;
    }

    /// <summary>Separable box dilation of a boolean mask.</summary>
    private static bool[] Dilate(bool[] mask, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            return (bool[])mask.Clone();
        }

        var horizontal = new bool[mask.Length];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                int start = Math.Max(0, x - radius);
                int end = Math.Min(width - 1, x + radius);
                bool set = false;

                for (int sample = start; sample <= end; sample++)
                {
                    if (mask[row + sample])
                    {
                        set = true;
                        break;
                    }
                }

                horizontal[row + x] = set;
            }
        }

        var result = new bool[mask.Length];

        for (int y = 0; y < height; y++)
        {
            int start = Math.Max(0, y - radius);
            int end = Math.Min(height - 1, y + radius);

            for (int x = 0; x < width; x++)
            {
                bool set = false;

                for (int sample = start; sample <= end; sample++)
                {
                    if (horizontal[(sample * width) + x])
                    {
                        set = true;
                        break;
                    }
                }

                result[(y * width) + x] = set;
            }
        }

        return result;
    }

    private static List<Rectangle> FindComponents(
        bool[] mask,
        int gridWidth,
        int gridHeight,
        int step,
        int bitmapWidth,
        int bitmapHeight,
        CancellationToken cancellationToken)
    {
        var visited = new bool[mask.Length];
        var stack = new int[mask.Length];
        var boxes = new List<Rectangle>(64);

        int minWidth = Math.Max(10, step * 3);
        int minHeight = Math.Max(8, step * 2);
        long bitmapArea = (long)bitmapWidth * bitmapHeight;

        for (int start = 0; start < mask.Length; start++)
        {
            if ((start & 8191) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!mask[start] || visited[start])
            {
                continue;
            }

            int top = 0;
            stack[top++] = start;
            visited[start] = true;

            int minX = gridWidth;
            int minY = gridHeight;
            int maxX = 0;
            int maxY = 0;
            int pixelCount = 0;

            while (top > 0)
            {
                int index = stack[--top];
                int x = index % gridWidth;
                int y = index / gridWidth;

                pixelCount++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if ((uint)ny >= (uint)gridHeight)
                    {
                        continue;
                    }

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int nx = x + dx;
                        if ((uint)nx >= (uint)gridWidth)
                        {
                            continue;
                        }

                        int neighbour = (ny * gridWidth) + nx;
                        if (mask[neighbour] && !visited[neighbour])
                        {
                            visited[neighbour] = true;
                            stack[top++] = neighbour;
                        }
                    }
                }
            }

            if (pixelCount < MinComponentPixels)
            {
                continue;
            }

            int left = minX * step;
            int upper = minY * step;
            int right = Math.Min(bitmapWidth, (maxX + 1) * step);
            int lower = Math.Min(bitmapHeight, (maxY + 1) * step);
            int width = right - left;
            int height = lower - upper;

            if (width < minWidth || height < minHeight)
            {
                continue;
            }

            // The window frame and full-width chrome are not clickable targets.
            if (width > bitmapWidth * 0.92 || height > bitmapHeight * 0.92)
            {
                continue;
            }

            if ((long)width * height > bitmapArea * 4 / 10)
            {
                continue;
            }

            double aspect = (double)width / Math.Max(height, 1);
            if (aspect > 30 || aspect < 1.0 / 30)
            {
                continue;
            }

            boxes.Add(new Rectangle(left, upper, width, height));

            if (boxes.Count >= 2000)
            {
                break;
            }

            if ((boxes.Count & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return MergeBoxes(boxes);
    }

    /// <summary>
    /// Drops boxes already covered by a larger box (text inside a button) and
    /// boxes that overlap almost completely (duplicate contours).
    /// </summary>
    private static List<Rectangle> MergeBoxes(List<Rectangle> boxes)
    {
        var ordered = boxes
            .OrderByDescending(box => (long)box.Width * box.Height)
            .ToList();

        var kept = new List<Rectangle>(ordered.Count);

        foreach (Rectangle box in ordered)
        {
            bool covered = false;

            foreach (Rectangle existing in kept)
            {
                if (existing.Contains(box))
                {
                    covered = true;
                    break;
                }

                int overlapWidth = Math.Min(existing.Right, box.Right) -
                                   Math.Max(existing.Left, box.Left);
                int overlapHeight = Math.Min(existing.Bottom, box.Bottom) -
                                    Math.Max(existing.Top, box.Top);

                if (overlapWidth <= 0 || overlapHeight <= 0)
                {
                    continue;
                }

                long overlap = (long)overlapWidth * overlapHeight;
                long smallerArea = Math.Min(
                    (long)existing.Width * existing.Height,
                    (long)box.Width * box.Height);

                if (smallerArea > 0 && overlap * 100 >= smallerArea * 70)
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
            {
                kept.Add(box);
            }
        }

        return kept
            .OrderBy(box => box.Top)
            .ThenBy(box => box.Left)
            .ToList();
    }

    /// <summary>
    /// Converts capture-local candidate rectangles into canonical desktop-space
    /// <see cref="VisualTarget"/> values.
    /// </summary>
    private static List<VisualTarget> BuildTargets(
        List<Rectangle> candidates,
        CaptureResult capture,
        IReadOnlyList<(string Text, TargetBounds Bounds, double Confidence)> ocrTextBoxes)
    {
        var targets = new List<VisualTarget>(candidates.Count);
        Rectangle frame = capture.Region.ToRectangle();
        int index = 0;

        foreach (Rectangle candidate in candidates)
        {
            TargetBounds desktopBounds = HintCoordinates.FrameToDesktop(
                frame,
                candidate,
                capture.DpiScale,
                capture.DpiScale);

            if (!desktopBounds.IsValid)
            {
                continue;
            }

            string? text = null;
            double ocrConfidence = 0;

            if (ocrTextBoxes.Count > 0)
            {
                var matching = ocrTextBoxes
                    .Where(box => candidate.IntersectsWith(new Rectangle(
                        box.Bounds.Left,
                        box.Bounds.Top,
                        box.Bounds.Width,
                        box.Bounds.Height)))
                    .ToList();

                if (matching.Count > 0)
                {
                    text = string.Join(
                        " ",
                        matching
                            .Select(box => box.Text)
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    ocrConfidence = matching.Max(box => box.Confidence);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = null;
                    }
                }
            }

            Point clickPoint = HintCoordinates.ClampToBounds(
                desktopBounds.Center,
                desktopBounds);

            double detectionConfidence = text is null
                ? 0.55
                : Math.Clamp(0.55 + (ocrConfidence * 0.4), 0.55, 0.95);

            targets.Add(new VisualTarget(
                id: $"cv-{++index}",
                source: text is null ? TargetSource.CvContour : TargetSource.CvOcr,
                bounds: desktopBounds,
                clickPoint: clickPoint,
                type: HeuristicTargetType(candidate),
                text: text ?? string.Empty,
                detectionConfidence: detectionConfidence,
                interactionConfidence: detectionConfidence,
                isEnabled: true,
                isVisible: true,
                metadata: new TargetMetadata
                {
                    Cv = new CvTargetMetadata(
                        OcrText: text,
                        OcrConfidence: text is null ? null : ocrConfidence,
                        FrameId: 0,
                        Detector: "edge-components",
                        SourceBounds: candidate)
                }));
        }

        return targets;
    }

    private static TargetType HeuristicTargetType(Rectangle rectangle)
    {
        double aspect = (double)rectangle.Width / Math.Max(rectangle.Height, 1);

        if (aspect >= 2.5)
        {
            return TargetType.Button;
        }

        if (rectangle.Height > rectangle.Width * 1.8)
        {
            return TargetType.ListItem;
        }

        if (aspect is >= 0.8 and <= 1.2 && rectangle.Width <= 48)
        {
            return TargetType.CheckBox;
        }

        return TargetType.Other;
    }

    /// <summary>Deduplicates by center and caps the output list.</summary>
    private static List<VisualTarget> ApplyPostProcessing(List<VisualTarget> targets, int maxElements)
    {
        int cap = Math.Clamp(maxElements, 1, 2000);
        var centers = new List<Point>(Math.Min(targets.Count, cap));
        var deduped = new List<VisualTarget>(Math.Min(targets.Count, cap));

        foreach (VisualTarget target in targets
                     .OrderBy(item => item.Bounds.Top)
                     .ThenBy(item => item.Bounds.Left))
        {
            Point center = target.Bounds.Center;
            bool duplicate = false;

            foreach (Point existing in centers)
            {
                if (Math.Abs(existing.X - center.X) <= 4 &&
                    Math.Abs(existing.Y - center.Y) <= 4)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            centers.Add(center);
            deduped.Add(target);
        }

        return deduped.Take(cap).ToList();
    }
}

/// <summary>
/// No-op OCR implementation. Used when no local OCR engine is configured; the
/// contour detector still runs, so this does not disable computer vision.
/// </summary>
internal sealed class NullOcrEngine : IOcrEngine
{
    public IReadOnlyList<OcrWord> Recognize(CaptureResult capture, CancellationToken cancellationToken = default)
        => [];

    public static NullOcrEngine Instance { get; } = new();
}
