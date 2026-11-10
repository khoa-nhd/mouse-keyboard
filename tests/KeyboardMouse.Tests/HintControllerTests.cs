using System.Drawing;
using KeyboardMouse.Core;
using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class HintControllerTests
{
    [Fact]
    public void H_StartsAScanForTheForegroundWindow()
    {
        var h = new RouterHarness();
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.H));
        Assert.True(h.Up(PhysicalKey.H));

        Assert.Equal(1, h.HintScanner.ScanCount);
        Assert.Equal(h.Foreground, h.HintScanner.Window);
        Assert.Equal(400, h.HintScanner.Options!.MaxElements);
        Assert.True(h.HintScanner.Options.IncludeTaskbar);
        Assert.True(h.HintOverlay.Visible);
        Assert.True(h.HintOverlay.Scanning);
        Assert.Equal("Scanning...", h.HintOverlay.ScanText);
    }

    [Fact]
    public void ScanResult_ShowsLabels_AndATypedLabelClicksTheCenter()
    {
        var h = new RouterHarness();
        OpenHint(h);

        h.HintScanner.Complete(Target(100, 100), Target(300, 100));

        Assert.True(h.HintOverlay.Visible);
        Assert.False(h.HintOverlay.Scanning);
        Assert.Equal(2, h.HintOverlay.Targets.Count);
        Assert.Equal(new[] { "a", "j" }, h.HintOverlay.Labels);

        Assert.True(h.Down(PhysicalKey.A));
        Assert.True(h.Up(PhysicalKey.A));

        Assert.Equal("move:150,120", h.Mouse.Calls[^2]);
        Assert.Equal("left-click", h.Mouse.Calls[^1]);
        Assert.False(h.HintOverlay.Visible);
        Assert.True(h.State.MouseModeOn);
    }

    [Fact]
    public void ShiftAtTheEnd_MovesWithoutClicking()
    {
        var h = new RouterHarness();
        OpenHint(h);
        h.HintScanner.Complete(Target(100, 100), Target(300, 100));

        h.Down(PhysicalKey.LeftShift);
        Assert.True(h.Down(PhysicalKey.A));

        Assert.Equal("move:150,120", h.Mouse.Calls[^1]);
        Assert.DoesNotContain("left-click", h.Mouse.Calls);
        Assert.DoesNotContain("right-click", h.Mouse.Calls);
        Assert.False(h.HintOverlay.Visible);

        h.Up(PhysicalKey.A);
        h.Up(PhysicalKey.LeftShift);
    }

    [Fact]
    public void CtrlAtTheEnd_RightClicks()
    {
        var h = new RouterHarness();
        OpenHint(h);
        h.HintScanner.Complete(Target(100, 100), Target(300, 100));

        h.Down(PhysicalKey.LeftCtrl);
        Assert.True(h.Down(PhysicalKey.A));

        Assert.Equal("right-click", h.Mouse.Calls[^1]);
        Assert.False(h.HintOverlay.Visible);

        h.Up(PhysicalKey.A);
        h.Up(PhysicalKey.LeftCtrl);
    }

    [Fact]
    public void HoldingF_RightClicks()
    {
        var h = new RouterHarness();
        OpenHint(h);
        h.HintScanner.Complete(Target(100, 100), Target(300, 100));

        Assert.True(h.Down(PhysicalKey.F));
        Assert.True(h.Down(PhysicalKey.A));

        Assert.Equal("right-click", h.Mouse.Calls[^1]);
        Assert.False(h.HintOverlay.Visible);

        h.Up(PhysicalKey.A);
        h.Up(PhysicalKey.F);
    }

    [Fact]
    public void FAsTheCompletingLabel_StillLeftClicks()
    {
        var h = new RouterHarness();
        OpenHint(h);

        var targets = Enumerable.Range(0, 8)
            .Select(i => Target(50 + (i * 150), 50))
            .ToArray();
        h.HintScanner.Complete(targets);

        Assert.Equal("f", h.HintOverlay.Labels[6]);

        Assert.True(h.Down(PhysicalKey.F));
        Assert.True(h.Up(PhysicalKey.F));

        Assert.Equal("move:1000,70", h.Mouse.Calls[^2]);
        Assert.Equal("left-click", h.Mouse.Calls[^1]);
        Assert.False(h.HintOverlay.Visible);
    }

    [Fact]
    public void SemicolonLabel_CanBeTyped()
    {
        var h = new RouterHarness();
        OpenHint(h);

        var targets = Enumerable.Range(0, 8)
            .Select(i => Target(50 + (i * 150), 50))
            .ToArray();
        h.HintScanner.Complete(targets);

        Assert.Equal(";", h.HintOverlay.Labels[7]);

        Assert.True(h.Down(PhysicalKey.Semicolon));
        Assert.True(h.Up(PhysicalKey.Semicolon));

        Assert.Equal("move:1150,70", h.Mouse.Calls[^2]);
        Assert.Equal("left-click", h.Mouse.Calls[^1]);
        Assert.False(h.HintOverlay.Visible);
    }

    [Fact]
    public void Backspace_RemovesTheLastTypedLetter()
    {
        var h = new RouterHarness();
        OpenHint(h);
        h.HintScanner.Complete(NineTargets());

        h.Down(PhysicalKey.A);
        h.Up(PhysicalKey.A);
        Assert.Equal("a", h.HintOverlay.Typed);
        Assert.Equal(2, h.HintOverlay.Labels[0].Length);

        h.Down(PhysicalKey.Backspace);
        h.Up(PhysicalKey.Backspace);
        Assert.Equal("", h.HintOverlay.Typed);

        h.Down(PhysicalKey.J);
        h.Up(PhysicalKey.J);
        Assert.Equal("j", h.HintOverlay.Typed);

        h.Down(PhysicalKey.A);
        h.Up(PhysicalKey.A);

        Assert.Equal("move:350,70", h.Mouse.Calls[^2]);
        Assert.Equal("left-click", h.Mouse.Calls[^1]);
        Assert.False(h.HintOverlay.Visible);
    }

    [Fact]
    public void NonMatchingLetter_IsIgnored()
    {
        var h = new RouterHarness();
        OpenHint(h);
        h.HintScanner.Complete(NineTargets());

        h.Down(PhysicalKey.A);
        h.Up(PhysicalKey.A);
        Assert.Equal("a", h.HintOverlay.Typed);

        h.Down(PhysicalKey.Z);
        h.Up(PhysicalKey.Z);

        Assert.Equal("a", h.HintOverlay.Typed);
        Assert.True(h.HintOverlay.Visible);
    }

    [Fact]
    public void Escape_ClosesHintsWithoutLeavingMouseMode()
    {
        var h = new RouterHarness();
        OpenHint(h);
        h.HintScanner.Complete(Target(100, 100));

        Assert.True(h.Down(PhysicalKey.Escape));
        Assert.True(h.Up(PhysicalKey.Escape));

        Assert.False(h.HintOverlay.Visible);
        Assert.True(h.State.MouseModeOn);

        Assert.True(h.Down(PhysicalKey.J));
        Assert.Contains(Direction.Left, h.Movement.Active);
        h.Up(PhysicalKey.J);
    }

    [Fact]
    public void EscapeDuringScanning_IgnoresTheLateResult()
    {
        var h = new RouterHarness();
        OpenHint(h);

        Assert.True(h.Down(PhysicalKey.Escape));
        Assert.True(h.Up(PhysicalKey.Escape));
        Assert.False(h.HintOverlay.Visible);

        h.HintScanner.Complete(Target(100, 100));

        Assert.False(h.HintOverlay.Visible);
        Assert.Equal(0, h.HintOverlay.ShowCount);
    }

    [Fact]
    public void ScanTimeout_ClosesHintsAndNormalKeysWorkAgain()
    {
        var h = new RouterHarness();
        OpenHint(h);

        h.HintScanner.Timeout();

        Assert.False(h.HintOverlay.Visible);
        Assert.True(h.State.MouseModeOn);

        Assert.True(h.Down(PhysicalKey.J));
        Assert.Contains(Direction.Left, h.Movement.Active);
        h.Up(PhysicalKey.J);
    }

    [Fact]
    public void EmptyScan_ClosesHints()
    {
        var h = new RouterHarness();
        OpenHint(h);

        h.HintScanner.Complete();

        Assert.False(h.HintOverlay.Visible);
        Assert.Equal(0, h.HintOverlay.ShowCount);
    }

    [Fact]
    public void LeavingMouseMode_ClosesHintsAndIgnoresLateResults()
    {
        var h = new RouterHarness();
        OpenHint(h);

        h.Router.SetMode(false);

        Assert.False(h.HintOverlay.Visible);

        h.HintScanner.Complete(Target(100, 100));

        Assert.False(h.HintOverlay.Visible);
        Assert.Equal(0, h.HintOverlay.ShowCount);
    }

    [Fact]
    public void RemappedHintKey_IsUsed()
    {
        var h = new RouterHarness();
        h.KeySettings.Hint = "Y";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.H));
        Assert.False(h.Up(PhysicalKey.H));
        Assert.Equal(0, h.HintScanner.ScanCount);

        Assert.True(h.Down(PhysicalKey.Y));
        Assert.True(h.Up(PhysicalKey.Y));
        Assert.Equal(1, h.HintScanner.ScanCount);
    }

    [Fact]
    public void HintSettingsAndCursorMonitor_ArePassedToTheScan()
    {
        var h = new RouterHarness(monitors: FakeMonitorService.Dual());
        h.HintSettings.MaxElements = 123;
        h.HintSettings.IncludeTaskbar = false;
        h.Pointer = new Point(2000, 100);
        OpenHint(h);

        Assert.Equal(123, h.HintScanner.Options!.MaxElements);
        Assert.False(h.HintScanner.Options.IncludeTaskbar);
        Assert.Equal(new IntPtr(2), h.HintOverlay.ScanMonitor!.Handle);
    }

    [Fact]
    public void EarlyBatch_ShowsHintsBeforeTheScanFinishes()
    {
        var h = new RouterHarness();
        OpenHint(h);

        h.HintScanner.Partial(Target(100, 100), Target(300, 100));

        Assert.True(h.HintOverlay.Visible);
        Assert.False(h.HintOverlay.Scanning);
        Assert.Equal(2, h.HintOverlay.Targets.Count);
        Assert.All(h.HintOverlay.Labels, label => Assert.Equal(2, label.Length));

        foreach (char letter in h.HintOverlay.Labels[1])
        {
            h.Down(KeyOf(letter));
            h.Up(KeyOf(letter));
        }

        Assert.Equal("move:350,120", h.Mouse.Calls[^2]);
        Assert.Equal("left-click", h.Mouse.Calls[^1]);
    }

    [Fact]
    public void LateBatch_AppendsTargetsWithoutMovingTheLabelsOnScreen()
    {
        var h = new RouterHarness();
        OpenHint(h);

        h.HintScanner.Partial(Target(100, 100), Target(300, 100));
        var shown = h.HintOverlay.Labels.ToArray();

        h.HintScanner.Complete(Target(100, 100), Target(300, 100), Target(500, 100));

        Assert.Equal(3, h.HintOverlay.Targets.Count);
        Assert.Equal(shown, h.HintOverlay.Labels.Take(2).ToArray());
        Assert.Equal(2, h.HintOverlay.Labels[2].Length);
    }

    [Fact]
    public void LateBatchThatNeedsLongerLabels_RelabelsWhenNothingIsTyped()
    {
        var h = new RouterHarness();
        h.HintSettings.MaxElements = 200;
        OpenHint(h);

        h.HintScanner.Partial(ManyTargets(8));
        Assert.Equal(2, h.HintOverlay.Labels[0].Length);

        h.HintScanner.Complete(ManyTargets(70));

        Assert.Equal(70, h.HintOverlay.Targets.Count);
        Assert.Equal(70, h.HintOverlay.Labels.Count);
        Assert.Equal(3, h.HintOverlay.Labels[0].Length);
        Assert.Equal(70, h.HintOverlay.Labels.Distinct().Count());
    }

    [Fact]
    public void LateBatchWhileTyping_IsParkedUntilTheTypedLabelIsEmpty()
    {
        var h = new RouterHarness();
        h.HintSettings.MaxElements = 200;
        OpenHint(h);

        h.HintScanner.Partial(ManyTargets(8));

        h.Down(PhysicalKey.A);
        h.Up(PhysicalKey.A);
        Assert.Equal("a", h.HintOverlay.Typed);

        h.HintScanner.Complete(ManyTargets(70));

        // Sixty-four labels are already reserved; the rest waits so no hint
        // changes the label the user is typing.
        Assert.Equal(64, h.HintOverlay.Targets.Count);
        Assert.Equal(2, h.HintOverlay.Labels[0].Length);

        h.Down(PhysicalKey.Backspace);
        h.Up(PhysicalKey.Backspace);

        Assert.Equal(70, h.HintOverlay.Targets.Count);
        Assert.Equal(3, h.HintOverlay.Labels[0].Length);
    }

    private static PhysicalKey KeyOf(char letter) => letter switch
    {
        'a' => PhysicalKey.A,
        's' => PhysicalKey.S,
        'd' => PhysicalKey.D,
        'f' => PhysicalKey.F,
        'j' => PhysicalKey.J,
        'k' => PhysicalKey.K,
        'l' => PhysicalKey.L,
        _ => PhysicalKey.Semicolon
    };

    private static ClickableTarget[] ManyTargets(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Target(20 + ((i % 20) * 90), 40 + ((i / 20) * 60)))];

    private static void OpenHint(RouterHarness h)
    {
        h.TapCaps();
        Assert.True(h.Down(PhysicalKey.H));
        Assert.True(h.Up(PhysicalKey.H));
        Assert.True(h.HintOverlay.Scanning);
    }

    private static ClickableTarget Target(int x, int y) =>
        new(new Rectangle(x, y, 100, 40));

    private static ClickableTarget[] NineTargets() =>
    [
        Target(50, 50),
        Target(300, 50),
        Target(550, 50),
        Target(50, 150),
        Target(300, 150),
        Target(550, 150),
        Target(50, 250),
        Target(300, 250),
        Target(550, 250)
    ];
}
