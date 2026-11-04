using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using KeyboardMouse.Capture;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Tests;

/// <summary>
/// Honest, opt-in measurements for the three hint sources.
///
/// These tests do not invent precision/recall numbers. They open a real
/// WinForms window with two buttons and a custom-painted panel, scan that
/// window, and report what the scanner actually returned plus the wall-clock
/// time from <c>BeginScan</c> to the completion callback.
///
/// There is no ground-truth corpus in the repository, so precision, recall and
/// F1 are intentionally not computed here.
/// </summary>
public class BenchmarkTests
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task UiOnly_RealWindow_ReportsMeasurements()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanner = new UIAutomationScanner(FakeMonitorService.Single());

        (HintScanResult result, long elapsed) = await RunScanAsync(
            scanner,
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Uia,
                Sources = TargetSource.Uia,
                Budget = TimeSpan.FromMilliseconds(400)
            });

        Console.WriteLine(
            $"[UIA] status={result.Status} targets={result.Targets.Count} " +
            $"elapsed={elapsed}ms buttons={result.Targets.Count(t => t.Text is "Save" or "Cancel")}");

        Assert.Equal(HintScanStatus.Success, result.Status);
        Assert.Contains(result.Targets, target => target.Text == "Save");
        Assert.Contains(result.Targets, target => target.Text == "Cancel");
        Assert.InRange(elapsed, 0, 10_000);
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

        Console.WriteLine(
            $"[CV] status={result.Status} targets={result.Targets.Count} elapsed={elapsed}ms");

        Assert.True(
            result.HasTargets,
            "The pixel detector should find at least one region in a real window.");
        Assert.All(result.Targets, target => Assert.True(target.IsActionable));
        Assert.InRange(elapsed, 0, 10_000);
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task Hybrid_RealWindow_ReportsMeasurements()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanner = new HybridHintScanner();

        (HintScanResult result, long elapsed) = await RunScanAsync(
            scanner,
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Hybrid,
                Sources = TargetSource.All,
                Budget = TimeSpan.FromSeconds(2)
            });

        Console.WriteLine(
            $"[Hybrid] status={result.Status} targets={result.Targets.Count} elapsed={elapsed}ms");

        Assert.Equal(HintScanStatus.Success, result.Status);
        Assert.Contains(result.Targets, target => target.Text == "Save");
        Assert.InRange(elapsed, 0, 10_000);
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task Latency_ThreeIterations_ReportsP50AndMax()
    {
        using BenchmarkWindow window = BenchmarkWindow.Show();
        var scanners = new (string Name, IHintScanner Scanner, HintScanMode Mode, TargetSource Sources)[]
        {
            ("UIA", new UIAutomationScanner(FakeMonitorService.Single()), HintScanMode.Uia, TargetSource.Uia),
            ("CV", new ComputerVisionScanner(new ScreenCaptureService()), HintScanMode.Cv, TargetSource.Cv),
            ("Hybrid", new HybridHintScanner(), HintScanMode.Hybrid, TargetSource.All)
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
                    Budget = TimeSpan.FromSeconds(2)
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

        return (scan, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// A real window on its own STA message loop. Two real buttons exercise UI
    /// Automation, and a custom-painted panel exercises the pixel detector.
    /// </summary>
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

                var save = new Button
                {
                    Text = "Save",
                    Location = new Point(24, 24),
                    Size = new Size(120, 36)
                };

                var cancel = new Button
                {
                    Text = "Cancel",
                    Location = new Point(168, 24),
                    Size = new Size(120, 36)
                };

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
                form.Shown += (_, _) => ready.TrySetResult(window);

                try
                {
                    Application.Run(form);
                }
                finally
                {
                    window._closed.Set();
                }
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
            catch (InvalidOperationException)
            {
                // The form already closed.
            }

            _closed.Wait(TimeSpan.FromSeconds(5));
            _closed.Dispose();
        }
    }
}
