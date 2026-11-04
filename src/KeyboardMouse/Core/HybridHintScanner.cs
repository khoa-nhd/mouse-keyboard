using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;
using KeyboardMouse.Native;

namespace KeyboardMouse.Capture;

/// <summary>
/// Orchestrated hint scanner combining UIA and CV via TargetFusionEngine.
/// </summary>
public sealed class HybridHintScanner : IHintScanner
{
    private const int HardTimeoutMs = 800;

    private readonly Lazy<UIAutomationScanner> _uiScanner;
    private readonly Lazy<ComputerVisionScanner> _cvScanner;
    private readonly Lazy<TargetFusionEngine> _fusionEngine;

    public HybridHintScanner()
    {
        _uiScanner = new Lazy<UIAutomationScanner>(() => new UIAutomationScanner());
        _cvScanner = new Lazy<ComputerVisionScanner>(() => new ComputerVisionScanner(new ScreenCaptureService()));
        _fusionEngine = new Lazy<TargetFusionEngine>(() => new TargetFusionEngine());
    }

    public void BeginScan(HintScanRequest request, Action<HintScanResult> completed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completed);

        bool scanUia = (request.Options.Sources & TargetSource.Uia) != 0;
        bool scanCv = (request.Options.Sources & TargetSource.Cv) != 0;

        if (!scanUia && !scanCv)
        {
            completed(HintScanResult.FromTargets(new List<VisualTarget>()));
            return;
        }

        if (scanUia && !scanCv)
        {
            var uiaReq = new HintScanRequest(
                request.Window,
                new HintScanOptions(Math.Clamp(request.Options.MaxElements, 1, 2000), request.Options.IncludeTaskbar) { Sources = TargetSource.Uia },
                request.CancellationToken);
            _uiScanner.Value.BeginScan(uiaReq, completed);
            return;
        }

        if (!scanUia && scanCv)
        {
            var captureService = new ScreenCaptureService();
            var cvScanner = new ComputerVisionScanner(captureService);
            var cvReq = new HintScanRequest(
                request.Window,
                new HintScanOptions(Math.Clamp(request.Options.MaxElements, 1, 2000), request.Options.IncludeTaskbar) { Sources = TargetSource.Cv },
                request.CancellationToken);
            cvScanner.BeginScan(cvReq, completed);
            return;
        }

        // Hybrid mode: UIA first, then CV, then fuse.
        HybridRun(request, completed);
    }

    private void HybridRun(HintScanRequest request, Action<HintScanResult> completed)
    {
        var baseOpts = new HintScanOptions(Math.Clamp(request.Options.MaxElements, 1, 2000), request.Options.IncludeTaskbar);
        var uiTargets = new List<VisualTarget>();
        var cvTargets = new List<VisualTarget>();
        var doneLock = new object();
        bool uiDone = false;
        bool cvDone = false;
        bool outerDone = false;

        // Run UIA
        var uiaReq = new HintScanRequest(
            request.Window,
            baseOpts with { Sources = TargetSource.Uia },
            request.CancellationToken);
        _uiScanner.Value.BeginScan(uiaReq, r =>
        {
            lock (doneLock) { uiTargets.AddRange(r.Targets); Monitor.PulseAll(doneLock); }
        });
        lock (doneLock) { while (!uiDone && Monitor.Wait(doneLock, HardTimeoutMs)) { if (uiTargets.Count > 0) uiDone = true; } }

        if (!uiDone)
        {
            // UIA timed out, run CV alone
            var cvReq = new HintScanRequest(
                request.Window,
                baseOpts with { Sources = TargetSource.Cv },
                request.CancellationToken);
            var capSvc = new ScreenCaptureService();
            var cvScanner = new ComputerVisionScanner(capSvc);
            cvScanner.BeginScan(cvReq, r =>
            {
                lock (doneLock) { cvTargets.AddRange(r.Targets); cvDone = true; Monitor.PulseAll(doneLock); }
            });
            lock (doneLock)
            {
                if (!outerDone) { outerDone = true; completed(HintScanResult.FromTargets(cvTargets, new HintScanDiagnostics(CandidateCount: cvTargets.Count, AcceptedCount: cvTargets.Count, UiaUsed: false, CvUsed: true))); }
            };
            return;
        }

        // Run CV
        var cvReq2 = new HintScanRequest(
            request.Window,
            baseOpts with { Sources = TargetSource.Cv },
            request.CancellationToken);
        var capSvc2 = new ScreenCaptureService();
        var cvScanner2 = new ComputerVisionScanner(capSvc2);
        cvScanner2.BeginScan(cvReq2, r =>
        {
            lock (doneLock) { cvTargets.AddRange(r.Targets); cvDone = true; Monitor.PulseAll(doneLock); }
        });
        lock (doneLock) { while (!cvDone && Monitor.Wait(doneLock, HardTimeoutMs)) { if (cvTargets.Count > 0) cvDone = true; } }

        // Fuse and complete
        lock (doneLock)
        {
            if (outerDone) return;
            outerDone = true;
            var diagnostics = new HintScanDiagnostics(CandidateCount: uiTargets.Count + cvTargets.Count, AcceptedCount: 0, UiaUsed: uiTargets.Count > 0, CvUsed: cvTargets.Count > 0);
            List<VisualTarget> targets;
            if (uiTargets.Count > 0 && cvTargets.Count > 0)
            {
                var engine = new TargetFusionEngine();
                var fused = engine.Fuse(uiTargets, cvTargets);
                targets = fused.Targets.ToList();
            }
            else if (uiTargets.Count > 0) { targets = uiTargets; }
            else if (cvTargets.Count > 0) { targets = cvTargets; }
            else { targets = new List<VisualTarget>(); }
            completed(HintScanResult.FromTargets(targets, diagnostics));
        }
    }
}