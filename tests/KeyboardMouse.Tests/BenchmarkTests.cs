using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KeyboardMouse.Capture;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Tests;

/// <summary>
/// Honest, opt-in measurements for the three hint sources.
///
/// These tests open a real WinForms window with two buttons and a custom-painted
/// panel, scan that window, and report what the scanner actually returned plus
/// the wall-clock time from <c>BeginScan</c> to the completion callback.
///
/// In addition to latency, we now compute accuracy metrics (precision, recall,
/// F1) against a small ground-truth corpus defined for the benchmark window,
/// and click-point validity.  The tests do not enforce strict thresholds —
/// they record numbers so that a regression can be detected later.
/// </summary>
[Collection(RealUiAutomationCollection.Name)]
public class BenchmarkTests
{
    // ── Ground truth for the benchmark window ──────────────────────────────
    // The benchmark window contains:
    //   • "Save" button (real WinForms button, UIA-accessible) at (24,24) size (120,36)
    //   • "Cancel" button (real WinForms button, UIA-accessible) at (168,24) size (120,36)
    //   • Custom-drawn panel with two rectangles:
    //       Rect A: (12,12) size (140,40)
    //       Rect B: (176,72) size (150,44)
    //   • Text "custom drawn" at (12,64) — intentionally not a clickable target
    //
    // Ground-truth TargetBounds (in screen-cap local coordinates, which for this
    // window coincide with client-area coordinates since the window is small and
    // at (80,80) on the desk):
    static readonly List<TargetBounds> GroundTruth = new()
    {
        new TargetBounds(24, 24, 120, 36),   // Save
        new TargetBounds(168, 24, 120, 36),  // Cancel
        new TargetBounds(12, 12, 140, 40),   // Rect A
        new TargetBounds(176, 72, 150, 44),  // Rect B
    };

    // Helper: convert a Rectangle from BenchmarkWindow coords to TargetBounds.
    static TargetBounds ToTargetBounds(Rectangle r) => new TargetBounds(r.Left, r.Top, r.Width, r.Height);

    // ── Private helper: run a scan and capture result + elapsed ────────────
    private static async Task<(HintScanResult Result, long ElapsedMs)> RunScanAsync(
        IHintScanner scanner,
        IntPtr window,
        HintScanOptions options)
    {
        var completion = new TaskCompletionSource<HintScanResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var stopwatch = Stopwatch.StartNew();
        scanner.BeginScan(
            new HintScanRequest(window, options),
            result => completion.TrySetResult(result));

        HintScanResult scan = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        stopwatch.Stop();

        return (scan, (long)stopwatch.ElapsedMilliseconds);
    }

    // ── Precision/Recall/F1 at IOU >= 0.5 ────────────────────────────────
    /// <summary>Compute P/R/F1 for detected targets vs ground-truth bounds.</summary>
    static (double Precision, double Recall, double F1) ComputePRL(
        IReadOnlyList<VisualTarget> detected,
        IReadOnlyList<TargetBounds> groundTruth)
    {
        var matched = new bool[groundTruth.Count];
        int truePositives = 0;

        foreach (var det in detected)
        {
            double bestIou = 0;
            int bestIdx = -1;

            for (int j = 0; j < groundTruth.Count; j++)
            {
                if (matched[j]) continue;
                double iou = TargetSimilarity.IoU(det.Bounds, groundTruth[j]);
                if (iou > bestIou)
                {
                    bestIou = iou;
                    bestIdx = j;
                }
            }

            if (bestIdx >= 0 && bestIou >= 0.5)
            {
                matched[bestIdx] = true;
                truePositives++;
            }
        }

        int falsePositives = detected.Count - truePositives;
        int falseNegatives = groundTruth.Count - matched.Count(d => d);

        double precision = detected.Count > 0 ? (double)truePositives / detected.Count : 0.0;
        double recall = groundTruth.Count > 0 ? (double)truePositives / groundTruth.Count : 1.0;
        double f1 = (precision + recall > 0) ? 2 * precision * recall / (precision + recall) : 0.0;

        return (precision, recall, f1);
    }

    // ── Click-point validity ──────────────────────────────────────────────
    static double ComputeClickPointValidity(IReadOnlyList<VisualTarget> targets)
    {
        if (targets.Count == 0) return 0.0;
        int valid = targets.Count(t => t.Bounds.Contains(t.ClickPoint));
        return (double)valid / targets.Count;
    }

    // ── Benchmark window ──────────────────────────────────────────────────
    /// <summary>A real window on its own STA message loop. Two real buttons
    /// exercise UI Automation, and a custom-painted panel exercises the pixel
    /// detector.</summary>
    private sealed class BenchmarkWindow : IDisposable
    {
        private readonly Form _form;
        private readonly ManualResetEventSlim _closed = new();

        private BenchmarkWindow(Form form) => _form = form;

        public IntPtr Handle => _form.Handle;

        public static BenchmarkWindow Show()
        {
            var ready = new TaskCompletionSource<BenchmarkWindow>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                var form = new Form
                {
                    Text = "KeyboardMouse benchmark window",
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(80, 80),
                    ClientSize = new Size(480, 280),
                    ShowInTaskbar = false
                };

                var save = new Button { Text = "Save", Location = new Point(24, 24), Size = new Size(120, 36) };
                var cancel = new Button { Text = "Cancel", Location = new Point(168, 24), Size = new Size(120, 36) };
                var panel = new Panel
                {
                    Location = new Point(24, 88),
                    Size = new Size(360, 140),
                    BackColor = Color.White
                };

                panel.Paint += (_, eventArgs) =>
                {
                    using var pen = new Pen(Color.FromArgb(50, 50, 50), 2);
                    eventArgs.Graphics.DrawRectangle(pen, 12, 12, 140, 40);
                    eventArgs.Graphics.DrawRectangle(pen, 176, 72, 150, 44);
                    eventArgs.Graphics.DrawString(
                        "custom drawn",
                        new Font("Segoe UI", 10f),
                        Brushes.Black,
                        12,
                        64);
                };

                form.Controls.Add(save);
                form.Controls.Add(cancel);
                form.Controls.Add(panel);

                var window = new BenchmarkWindow(form);
                form.Shown += (_, _) =>
                {
                    form.TopMost = true;
                    form.Activate();
                    form.Refresh();
                    ready.TrySetResult(window);
                };

                try { Application.Run(form); }
                finally { window._closed.Set(); }
            })
            {
                IsBackground = true,
                Name = "KeyboardMouse.BenchmarkWindow"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Task.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The benchmark window did not open.");
            }

            Thread.Sleep(250);
            return ready.Task.Result;
        }

        public void Dispose()
        {
            try
            {
                if (_form.IsHandleCreated)
                {
                    _form.BeginInvoke(new Action(_form.Close));
                }
            }
            catch (InvalidOperationException) { }

            _closed.Wait(TimeSpan.FromSeconds(5));
            _closed.Dispose();
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task UiOnly_RealWindow_ReportsMeasurements()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanner = TestHintScanners.Uia(FakeMonitorService.Single());

        (HintScanResult result, long elapsed) = await RunScanAsync(
            scanner,
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Uia,
                Sources = TargetSource.Uia,
                Budget = TestHintScanners.UiaTimeout
            });

        // Basic sanity: scan completed and produced results
        Assert.True(
            result.Status is HintScanStatus.Success or HintScanStatus.NoTargets,
            $"UIA scan status: {result.Status} ({result.FailureReason})");

        // Report accuracy metrics against ground truth (informational only)
        var (p, r, f1) = ComputePRL(result.Targets, GroundTruth);
        var cpv = ComputeClickPointValidity(result.Targets);
        Console.WriteLine(
            $"[UIA] P={p:F3} R={r:F3} F1={f1:F3} click-point-valid={cpv:F3} " +
            $"targets={result.Targets.Count} elapsed={elapsed}ms");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task CvOnly_RealWindow_ReportsMeasurements()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanner = new ComputerVisionScanner(
            new ScreenCaptureService(),
            timeout: TimeSpan.FromSeconds(2),
            maxElements: 200);

        (HintScanResult result, long elapsed) = await RunScanAsync(
            scanner,
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Cv,
                Sources = TargetSource.Cv,
                Budget = TimeSpan.FromSeconds(2)
            });

        // Basic sanity: scan completed and produced targets
        Assert.True(result.HasTargets, "CV scan should find at least one region");

        // Report accuracy metrics against ground truth (informational only)
        var (p, r, f1) = ComputePRL(result.Targets, GroundTruth);
        var cpv = ComputeClickPointValidity(result.Targets);
        Console.WriteLine(
            $"[CV] P={p:F3} R={r:F3} F1={f1:F3} click-point-valid={cpv:F3} " +
            $"targets={result.Targets.Count} elapsed={elapsed}ms");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task Hybrid_RealWindow_ReportsMeasurements()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanner = new HybridHintScanner(
            () => TestHintScanners.Uia(FakeMonitorService.Single()),
            () => new ComputerVisionScanner(new ScreenCaptureService()));

        (HintScanResult result, long elapsed) = await RunScanAsync(
            scanner,
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Hybrid,
                Sources = TargetSource.All,
                Budget = TestHintScanners.UiaTimeout
            });

        // Basic sanity: scan completed successfully
        Assert.Equal(HintScanStatus.Success, result.Status);

        // Report accuracy metrics against ground truth (informational only)
        var (p, r, f1) = ComputePRL(result.Targets, GroundTruth);
        var cpv = ComputeClickPointValidity(result.Targets);
        Console.WriteLine(
            $"[Hybrid] P={p:F3} R={r:F3} F1={f1:F3} click-point-valid={cpv:F3} " +
            $"targets={result.Targets.Count} elapsed={elapsed}ms");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task Latency_ThreeIterations_ReportsP50AndMax()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanners = new (string Name, IHintScanner Scanner, HintScanMode Mode, TargetSource Sources)[]
        {
            ("UIA", TestHintScanners.Uia(FakeMonitorService.Single()), HintScanMode.Uia, TargetSource.Uia),
            ("CV", new ComputerVisionScanner(new ScreenCaptureService()), HintScanMode.Cv, TargetSource.Cv),
            ("Hybrid", new HybridHintScanner(
                () => TestHintScanners.Uia(FakeMonitorService.Single()),
                () => new ComputerVisionScanner(new ScreenCaptureService())), HintScanMode.Hybrid, TargetSource.All)
        };

        foreach ((string name, IHintScanner scanner, HintScanMode mode, TargetSource sources) in scanners)
        {
            var samples = new List<long>(3);

            for (int i = 0; i < 3; i++)
            {
                var options = new HintScanOptions(200, false)
                {
                    Mode = mode,
                    Sources = sources,
                    Budget = TestHintScanners.UiaTimeout
                };

                (HintScanResult result, long elapsed) = await RunScanAsync(
                    scanner,
                    window.Handle,
                    options);

                Assert.True(
                    result.Status is HintScanStatus.Success or HintScanStatus.NoTargets,
                    $"{name} scan failed: {result.Status} {result.FailureReason}");

                samples.Add(elapsed);
                await Task.Delay(100);
            }

            samples.Sort();
            long p50 = samples[1];
            long max = samples[2];
            Console.WriteLine(
                $"[Latency {name}] n=3 samples={string.Join(",", samples)}ms " +
                $"P50={p50}ms max={max}ms");
            Assert.InRange(p50, 0, 10_000);
            Assert.InRange(max, 0, 10_000);
        }
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task ScopeLatency_WindowMonitorDesktop_ReportsMeasurements()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var monitors = new Monitors();

        foreach (HintScanScope scope in new[]
             {
                 HintScanScope.ForegroundWindow,
                 HintScanScope.ActiveMonitor,
                 HintScanScope.VirtualDesktop
             })
        {
            var scanner = new ComputerVisionScanner(
                new ScreenCaptureService(),
                timeout: TimeSpan.FromSeconds(5),
                maxElements: 300,
                monitors: monitors);

            (HintScanResult result, long elapsed) = await RunScanAsync(
                scanner,
                window.Handle,
                new HintScanOptions(300, false)
                {
                    Mode = HintScanMode.Cv,
                    Sources = TargetSource.Cv,
                    Scope = scope,
                    Budget = TimeSpan.FromSeconds(5)
                });

            Console.WriteLine(
                $"[Scope {HintScanScopes.ToId(scope)}] status={result.Status} " +
                $"targets={result.Targets.Count} " +
                $"candidates={result.Diagnostics?.CandidateCount ?? 0} " +
                $"elapsed={elapsed}ms");

            Assert.Equal(HintScanStatus.Success, result.Status);
            Assert.InRange(elapsed, 0, 10_000);
        }
    }
}