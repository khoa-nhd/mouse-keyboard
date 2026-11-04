using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class HintScanResultTests
{
    private static VisualTarget Target() =>
        new(
            "target-1",
            TargetSource.Uia,
            new TargetBounds(10, 20, 30, 40),
            new Point(20, 30));

    [Fact]
    public void FromTargetsInfersNoTargets()
    {
        var result = HintScanResult.FromTargets([]);

        Assert.Equal(HintScanStatus.NoTargets, result.Status);
        Assert.True(result.IsSuccess);
        Assert.False(result.HasTargets);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void FromTargetsInfersSuccess()
    {
        var diagnostics = new HintScanDiagnostics(
            CandidateCount: 4,
            AcceptedCount: 1,
            ElapsedMilliseconds: 12,
            UiaUsed: true);

        var result = HintScanResult.FromTargets([Target()], diagnostics);

        Assert.Equal(HintScanStatus.Success, result.Status);
        Assert.True(result.IsSuccess);
        Assert.True(result.HasTargets);
        Assert.Same(diagnostics, result.Diagnostics);
        Assert.Equal(1, result.Diagnostics?.AcceptedCount);
    }

    [Fact]
    public void FailureCarriesStatusAndReason()
    {
        var result = HintScanResult.Failure(
            HintScanStatus.TimedOut,
            "The scan deadline expired.");

        Assert.Equal(HintScanStatus.TimedOut, result.Status);
        Assert.False(result.IsSuccess);
        Assert.False(result.HasTargets);
        Assert.Empty(result.Targets);
        Assert.Equal("The scan deadline expired.", result.FailureReason);
    }

    [Fact]
    public void ScanOptionsDefaultToUiaAndABudget()
    {
        var options = new HintScanOptions(MaxElements: 400, IncludeTaskbar: true);

        Assert.Equal(TargetSource.Uia, options.Sources);
        Assert.True(options.Budget > TimeSpan.Zero);
    }
}
