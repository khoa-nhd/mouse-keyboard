using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;

namespace KeyboardMouse.Tests;

public class UIAutomationScannerIntegrationTests
{
    [Fact]
    public async Task ScansARealWinFormsButtonThroughFlaUi()
    {
        var ready = new TaskCompletionSource<(Form Form, Button Button)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                Text = "KeyboardMouse UIA test",
                StartPosition = FormStartPosition.Manual,
                Location = new Point(100, 100),
                ClientSize = new Size(320, 200),
                ShowInTaskbar = false
            };
            var button = new Button
            {
                Text = "Save",
                Location = new Point(20, 20),
                Size = new Size(100, 30)
            };
            form.Controls.Add(button);
            form.Shown += (_, _) => ready.TrySetResult((form, button));

            try
            {
                Application.Run(form);
            }
            finally
            {
                closed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "KeyboardMouse.UIAIntegrationTest"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        (Form form, Button _) = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await Task.Delay(100);

            var scanner = new UIAutomationScanner(FakeMonitorService.Single());
            var result = new TaskCompletionSource<HintScanResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            scanner.BeginScan(
                new HintScanRequest(
                    form.Handle,
                    new HintScanOptions(100, false) { Budget = TimeSpan.FromMilliseconds(400) }),
                value => result.TrySetResult(value));

            HintScanResult scan = await result.Task.WaitAsync(TimeSpan.FromSeconds(5));
            VisualTarget target = Assert.Single(scan.Targets, item => item.Text == "Save");

            Assert.Equal(HintScanStatus.Success, scan.Status);
            Assert.Equal(TargetSource.Uia, target.Source);
            Assert.Equal(TargetType.Button, target.Type);
            Assert.Equal("Button", target.Metadata.Uia?.ControlType);
            Assert.True(target.IsActionable);
        }
        finally
        {
            try
            {
                form.BeginInvoke(new Action(form.Close));
            }
            catch (InvalidOperationException)
            {
                // The form already closed.
            }

            closed.Wait(TimeSpan.FromSeconds(5));
        }
    }
}
