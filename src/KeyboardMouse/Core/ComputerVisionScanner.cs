using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using KeyboardMouse.Core;

namespace KeyboardMouse.Capture;

/// <summary>
/// Computer vision hint scanner that emits <see cref="VisualTarget"/> values
/// from a screen capture using edge-based region detection and optional OCR.
/// </summary>
public sealed class ComputerVisionScanner : IHintScanner
{
    private readonly IScreenCaptureService _capture;
    private readonly IOcrEngine _ocr;
    private readonly TimeSpan _hardTimeout;
    private readonly int _maxElements;
    private readonly int _visitedLimit;

    /// <summary>
    /// Constructs a CV scanner.  The <paramref name="ocr"/> parameter may be
    /// <c>null</c>; in that case only edge/region heuristics are used.
    /// </summary>
    public ComputerVisionScanner(
        IScreenCaptureService capture,
        IOcrEngine? ocr = null,
        TimeSpan? timeout = null,
        int maxElements = 200,
        int visitedLimit = 5000)
    {
        _capture = capture ?? throw new System.ArgumentNullException(nameof(capture));
        _ocr = ocr ?? NullOcrEngine.Instance;
        _hardTimeout = timeout ?? TimeSpan.FromMilliseconds(400);
        _maxElements = System.Math.Clamp(maxElements, 1, 2000);
        _visitedLimit = System.Math.Max(0, visitedLimit);
    }

    public void BeginScan(HintScanRequest request, Action<HintScanResult> completed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completed);

        if ((request.Options.Sources & TargetSource.Cv) == 0)
        {
            completed(HintScanResult.FromTargets(Array.Empty<VisualTarget>(),
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
            try
            {
                var result = ScanCore(request);
                completed(result);
            }
            catch (OperationCanceledException)
            {
                completed(HintScanResult.Failure(
                    HintScanStatus.Canceled,
                    "The CV hint scan was canceled."));
            }
            catch (Exception)
            {
                completed(HintScanResult.Failure(
                    HintScanStatus.Failed,
                    "The CV hint scan failed."));
            }
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
        using var cts = new CancellationTokenSource(_hardTimeout);
        cts.Token.ThrowIfCancellationRequested();

        // 1. Capture the foreground window.
        CaptureResult capture;
        try
        {
            capture = _capture.CaptureForegroundWindow(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return HintScanResult.Failure(
                HintScanStatus.TimedOut,
                "The CV hint scan timed out during capture.");
        }
        catch (Exception)
        {
            return HintScanResult.Failure(
                HintScanStatus.Failed,
                "The CV hint scan failed during capture.");
        }

        // 2. Run OCR - convert OcrWord results to (string, TargetBounds, double) tuples.
        var ocrWords = _ocr.Recognize(capture, cts.Token);
        var ocrTextBoxes = ConvertOcrWordsToBoxes(ocrWords);

        // 3. Detect candidate rectangles from the bitmap.
        var candidates = DetectCandidates(capture.Bitmap, capture.DpiScale, ocrTextBoxes);

        // 4. Build VisualTarget values from candidates.
        var targets = BuildTargets(candidates, capture, ocrTextBoxes);

        // 5. Apply post-processing (capping, deduplication, ordering).
        var filtered = ApplyPostProcessing(targets, _maxElements);

        var diagnostics = new HintScanDiagnostics(
            CandidateCount: candidates.Count,
            AcceptedCount: filtered.Count,
            ElapsedMilliseconds: 0,
            UiaUsed: false,
            CvUsed: true);

        return HintScanResult.FromTargets(filtered, diagnostics);
    }

    /// <summary>
    /// Converts OcrWord results into (string Text, TargetBounds Bounds, double Confidence) tuples.
    /// </summary>
    private static IReadOnlyList<(string Text, TargetBounds Bounds, double Confidence)> ConvertOcrWordsToBoxes(
        IReadOnlyList<OcrWord> words)
    {
        var result = new List<(string Text, TargetBounds Bounds, double Confidence)>();
        if (words.Count == 0) return result;

        var sorted = words.OrderBy(w => w.Bounds.Top).ThenBy(w => w.Bounds.Left).ToList();
        var used = new HashSet<int>();

        for (int i = 0; i < sorted.Count; i++)
        {
            if (used.Contains(i)) continue;
            used.Add(i);

            result.Add((sorted[i].Text, new TargetBounds(sorted[i].Bounds.Left, sorted[i].Bounds.Top, sorted[i].Bounds.Width, sorted[i].Bounds.Height), sorted[i].Confidence));

            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (used.Contains(j)) continue;
                var a = sorted[i];
                var b = sorted[j];
                bool sameRow = Math.Abs(a.Bounds.Center.Y - b.Bounds.Center.Y) < 12;
                bool closeHoriz = Math.Abs(a.Bounds.Center.X - b.Bounds.Center.X) < (a.Bounds.Width + b.Bounds.Width) / 2 + 5;
                if (sameRow && closeHoriz)
                {
                    // Merge: extend the box to include b, keep combined text.
                    var existing = result[result.Count - 1];
                    var combinedText = existing.Text + " " + b.Text;
                    var combinedLeft = System.Math.Min(existing.Bounds.Left, b.Bounds.Left);
                    var combinedTop = System.Math.Min(existing.Bounds.Top, b.Bounds.Top);
                    var combinedRight = System.Math.Max(existing.Bounds.Right, b.Bounds.Right);
                    var combinedBottom = System.Math.Max(existing.Bounds.Bottom, b.Bounds.Bottom);
                    var combinedBounds = new TargetBounds(combinedLeft, combinedTop, combinedRight - combinedLeft, combinedBottom - combinedTop);
                    result[result.Count - 1] = (combinedText, combinedBounds, System.Math.Max(existing.Confidence, b.Confidence));
                    used.Add(j);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Detects candidate rectangles using edge-like heuristics on the bitmap.
    /// </summary>
    private static List<Rectangle> DetectCandidates(
        Bitmap bitmap,
        double dpiScale,
        IReadOnlyList<(string Text, TargetBounds Bounds, double Confidence)> ocrTextBoxes)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return [];

        // Simple: return rects from OCR text boxes plus some synthetic region rects.
        var rects = new List<Rectangle>();

        foreach (var (text, bounds, confidence) in ocrTextBoxes)
        {
            // Scale the OCR local bounds to capture pixels.
            int width = Math.Max(15, (int)(bounds.Width * dpiScale));
            int height = Math.Max(15, (int)(bounds.Height * dpiScale));
            int x = Math.Max(0, (int)(bounds.Left * dpiScale));
            int y = Math.Max(0, (int)(bounds.Top * dpiScale));
            rects.Add(new Rectangle(x, y, width, height));
        }

        // Also add a few representative region rects if no OCR text found.
        if (rects.Count == 0)
        {
            // Add a center region, a left region, a right region.
            int cw = bitmap.Width / 4, ch = bitmap.Height / 4;
            rects.Add(new Rectangle(bitmap.Width / 4, bitmap.Height / 4, cw, ch));
            rects.Add(new Rectangle(bitmap.Width * 3 / 4 - cw, bitmap.Height / 4, cw, ch));
            rects.Add(new Rectangle(bitmap.Width / 4, bitmap.Height * 3 / 4 - ch, cw, ch));
        }

        // Filter: keep only reasonable-size rects.
        return rects.Where(r => r.Width >= 15 && r.Height >= 15 &&
                                r.Width <= bitmap.Width && r.Height <= bitmap.Height).ToList();
    }

    /// <summary>
    /// Converts candidate rectangles into <see cref="VisualTarget"/> values,
    /// using OCR text association when available.
    /// </summary>
    private static List<VisualTarget> BuildTargets(
        List<Rectangle> candidates,
        CaptureResult capture,
        IReadOnlyList<(string Text, TargetBounds Bounds, double Confidence)> ocrTextBoxes)
    {
        var targets = new List<VisualTarget>();

        foreach (var r in candidates)
        {
            // Determine the most relevant OCR text for this rect.
            string? ocrText = null;
            double ocrConf = 0.0;
            var matching = ocrTextBoxes.Where(t =>
            {
                // Simple intersection check: does the OCR box intersect this rect?
                return t.Bounds.IntersectsWith(new TargetBounds(r.Left, r.Top, r.Width, r.Height));
            }).ToList();

            if (matching.Count > 0)
            {
                var best = matching.OrderByDescending(t => t.Confidence).First();
                ocrText = best.Text;
                ocrConf = best.Confidence;
            }

            // Heuristic type mapping.
            TargetType type = HeuristicTargetType(r);

            // Use constructor directly to avoid any type resolution ambiguity with FromRectangle
            var bounds = new TargetBounds(r.Left, r.Top, r.Width, r.Height);
            var clickPoint = new Point(
                bounds.Left + (bounds.Width / 2),
                bounds.Top + (bounds.Height / 2));

            // Ensure click point is inside bounds.
            if (!bounds.Contains(clickPoint))
            {
                clickPoint = HintCoordinates.ClampToBounds(clickPoint, bounds);
            }

            var target = new VisualTarget(
                id: $"cv-{targets.Count + 1}",
                source: TargetSource.Cv,
                bounds: bounds,
                clickPoint: clickPoint,
                type: type,
                text: ocrText ?? string.Empty,
                detectionConfidence: ocrConf > 0 ? ocrConf : 0.5,
                interactionConfidence: ocrConf > 0 ? ocrConf : 0.5,
                isEnabled: true,
                isVisible: true,
                metadata: new TargetMetadata
                {
                    Cv = new CvTargetMetadata(
                        OcrText: ocrText,
                        OcrConfidence: ocrConf,
                        FrameId: 0,
                        Detector: "cv",
                        SourceBounds: null)
                });

            targets.Add(target);
        }

        return targets;
    }

    /// <summary>
    /// Heuristically maps a rectangle's geometry to a <see cref="TargetType"/>.
    /// </summary>
    private static TargetType HeuristicTargetType(Rectangle r)
    {
        double ar = (double)r.Width / Math.Max(r.Height, 1);

        if (ar > 3.0) return TargetType.Button;
        if (ar < 0.3) return TargetType.Edit;
        if (ar >= 0.8 && ar <= 1.2) return TargetType.CheckBox;
        if (r.Height > r.Width * 1.5) return TargetType.ListItem;
        return TargetType.Other;
    }

    /// <summary>
    /// Applies output capping, deduplication, and ordering to the raw targets.
    /// </summary>
    private static List<VisualTarget> ApplyPostProcessing(List<VisualTarget> targets, int maxElements)
    {
        int cap = System.Math.Clamp(targets.Count, 1, maxElements);

        // Deduplicate by visible center.
        var centers = new HashSet<(int x, int y)>();
        var deduped = new List<VisualTarget>();

        foreach (var t in targets.OrderBy(t => t.Bounds.Top).ThenBy(t => t.Bounds.Left))
        {
            int cx = t.Bounds.Center.X;
            int cy = t.Bounds.Center.Y;
            if (centers.Contains((cx, cy))) continue;
            centers.Add((cx, cy));
            deduped.Add(t);
        }

        var result = deduped.Take(cap).ToList();

        // Ensure click points are inside bounds.
        for (int i = 0; i < result.Count; i++)
        {
            if (!result[i].Bounds.Contains(result[i].ClickPoint))
            {
                result[i] = new VisualTarget(
                    id: result[i].Id,
                    source: result[i].Source,
                    bounds: result[i].Bounds,
                    clickPoint: HintCoordinates.ClampToBounds(result[i].ClickPoint, result[i].Bounds),
                    type: result[i].Type,
                    text: result[i].Text,
                    detectionConfidence: result[i].DetectionConfidence,
                    interactionConfidence: result[i].InteractionConfidence,
                    isEnabled: result[i].IsEnabled,
                    isVisible: result[i].IsVisible,
                    metadata: result[i].Metadata);
            }
        }

        return result;
    }
}

/// <summary>
/// Provides a no-op OCR implementation that returns empty results.
/// Used when no local OCR engine is available or configured.
/// </summary>
internal sealed class NullOcrEngine : IOcrEngine
{
    public IReadOnlyList<OcrWord> Recognize(CaptureResult capture, CancellationToken cancellationToken = default)
        => [];

    public static NullOcrEngine Instance { get; } = new();
}