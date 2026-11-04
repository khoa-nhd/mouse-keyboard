using System.Drawing;
using System.Windows.Forms;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Tests;

/// <summary>
/// Exercises the exact adapter the application uses, not only the underlying
/// scanners. A real WinForms window keeps the test on the same capture path as
/// pressing H in the running app.
/// </summary>
[Collection(RealUiAutomationCollection.Name)]
public class HybridClickableScannerTests
{
    [Fact]
    public async Task CvMode_ReturnsClickableTargets_FromARealWindow()
    {
        using var window = TestWindow.Show();
        var scanner = new HybridClickableScanner(FakeMonitorService.Single());

        var completion = new TaskCompletionSource<IReadOnlyList<ClickableTarget>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int timedOutCount = 0;
        int completedCount = 0;

        scanner.BeginScan(
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Cv,
                Sources = TargetSource.Cv,
                Budget = TimeSpan.FromSeconds(3)
            },
            targets =>
            {
                Interlocked.Increment(ref completedCount);
                completion.TrySetResult(targets);
            },
            () =>
            {
                Interlocked.Increment(ref timedOutCount);
                completion.TrySetResult([]);
            });

        IReadOnlyList<ClickableTarget> targets =
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, completedCount);
        Assert.Equal(0, timedOutCount);
        Assert.NotEmpty(targets);
        Assert.All(targets, target =>
        {
            Assert.True(target.Bounds.Width > 0);
            Assert.True(target.Bounds.Height > 0);
            Assert.True(target.Bounds.Contains(target.ClickPoint));
        });
    }

    [Fact]
    public async Task AutoMode_FallsBackToCv_WhenUiaFindsOnlyCaptionButtons()
    {
        // No child controls: the only UIA targets are Minimize/Maximize/Close.
        // Auto must not stop there; it has to bring in the CV content.
        using var window = TestWindow.Show(withButton: false);
        var scanner = TestHintScanners.Clickable(FakeMonitorService.Single());

        var completion = new TaskCompletionSource<IReadOnlyList<ClickableTarget>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        scanner.BeginScan(
            window.Handle,
            new HintScanOptions(200, false)
            {
                Mode = HintScanMode.Auto,
                Sources = TargetSource.All,
                Budget = TimeSpan.FromSeconds(3)
            },
            targets => completion.TrySetResult(targets),
            () => completion.TrySetResult([]));

        IReadOnlyList<ClickableTarget> targets =
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(targets);

        Rectangle client = window.ClientBounds;
        Assert.Contains(
            targets,
            target => client.Contains(target.Bounds) &&
                      target.Bounds.Width >= 40 &&
                      target.Bounds.Height >= 20);
    }

    [Fact]
    public async Task ZeroWindow_CompletesWithNoTargets()
    {
        var scanner = new HybridClickableScanner(FakeMonitorService.Single());
        var completion = new TaskCompletionSource<IReadOnlyList<ClickableTarget>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int timedOutCount = 0;

        scanner.BeginScan(
            IntPtr.Zero,
            new HintScanOptions(10, false) { Mode = HintScanMode.Cv },
            targets => completion.TrySetResult(targets),
            () => Interlocked.Increment(ref timedOutCount));

        IReadOnlyList<ClickableTarget> targets =
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(targets);
        Assert.Equal(0, timedOutCount);
    }

    private sealed class TestWindow : IDisposable
    {
        private readonly Form _form;
        private readonly ManualResetEventSlim _closed = new();

        private TestWindow(Form form) => _form = form;

        public IntPtr Handle => _form.Handle;

        public Rectangle ClientBounds => _form.RectangleToScreen(_form.ClientRectangle);

        public static TestWindow Show(bool withButton = true)
        {
            var ready = new TaskCompletionSource<TestWindow>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                var form = new Form
                {
                    Text = "KeyboardMouse adapter test",
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(120, 120),
                    ClientSize = new Size(360, 220),
                    ShowInTaskbar = false
                };

                var button = new Button
                {
                    Text = "OK",
                    Location = new Point(24, 24),
                    Size = new Size(100, 32)
                };

                var panel = new Panel
                {
                    Location = new Point(24, 80),
                    Size = new Size(280, 100),
                    BackColor = Color.White
                };

                panel.Paint += (_, eventArgs) =>
                {
                    using var pen = new Pen(Color.FromArgb(40, 40, 40), 2);
                    eventArgs.Graphics.DrawRectangle(pen, 10, 10, 120, 34);
                    eventArgs.Graphics.DrawRectangle(pen, 150, 50, 100, 34);
                };

                form.Controls.Add(button);
                form.Controls.Add(panel);

                if (!withButton)
                {
                    form.Controls.Remove(button);
                    button.Dispose();
                }

                var window = new TestWindow(form);
                form.Shown += (_, _) =>
                {
                    // TopMost must be applied after the handle exists; setting it
                    // in the initializer is silently ignored in this environment.
                    form.TopMost = true;
                    form.Activate();
                    form.Refresh();
                    ready.TrySetResult(window);
                };

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
                Name = "KeyboardMouse.HybridAdapterTest"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Task.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The test window did not open.");
            }

            // Let DWM compose the topmost window before the capture starts.
            // Starting in the same frame as Shown occasionally captured the
            // window underneath it.
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
            catch (InvalidOperationException)
            {
                // The form already closed.
            }

            _closed.Wait(TimeSpan.FromSeconds(5));
            _closed.Dispose();
        }
    }
}
