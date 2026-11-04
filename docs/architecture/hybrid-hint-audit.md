# Hybrid Hint System — Implementation and Validation

**Date:** November 4, 2026
**Status:** Implemented, wired into the app, 332/332 tests passing (Release build,
0 errors, 0 warnings).

> **Correction notice.** An earlier revision of this document contained latency,
> precision, recall, F1, CPU, memory and FPS numbers that were not produced by a
> real measurement. Those numbers have been removed. This version only reports
> values that the test suite actually printed, and explicitly marks everything
> that has not been measured.

---

## 1. What is measured

The benchmark project (`tests/KeyboardMouse.Tests/BenchmarkTests.cs`) opens a
real WinForms window on an STA thread. The window contains two real buttons
(`Save`, `Cancel`) and a custom-painted panel with drawn rectangles and text.
Scans target that window handle, and the test waits for the completion callback
before stopping the stopwatch. No fixed sleeps and no synthetic scanner results
are used.

### 1.1 Measured smoke results (this machine, this window)

| Mode | Result status | Targets returned | One-shot elapsed | Latency test P50 / max (n=3) |
|---|---|---|---|---|
| UIA only | Success | 6 (both buttons found) | 63 ms | 46 ms / 57 ms |
| CV only | Success | 20 pixel regions | 39 ms | 46 ms / 64 ms |
| Hybrid | Success | 20 raw targets | 136 ms | 53 ms / 60 ms |

Run in a fresh test process on a 150% display, after the test host was switched
to PerMonitorV2 to match the app. An earlier run in the DPI-unaware host saw a
shifted capture crop, which is why its CV count was lower.

Notes:

- The CV target count includes text fragments and inner contours. The app's
  `HybridClickableScanner` applies containment deduplication before displaying
  labels, so the visible label count is lower.
- Hybrid returned UIA + CV targets because the fusion threshold did not match
  every CV text box to its parent button. The adapter removes the duplicates
  that are contained by a larger target.
- P50 is the median of three samples; "max" is the largest of three. With three
  samples a P95 is not statistically meaningful, so it is not reported.
- These numbers are a local smoke check, not a repeatable benchmark suite or a
  comparison across machines.

### 1.2 Not measured

| Metric | Status |
|---|---|
| Precision / recall / F1 | **Not measured.** There is no ground-truth corpus in the repository. |
| CPU usage per scan | **Not measured.** |
| Memory usage per scan | **Not measured.** |
| Scans per second / FPS | **Not measured.** |
| Cross-application compatibility matrix | **Not systematically verified.** |
| Multi-monitor and mixed-DPI behavior | Unit-tested for coordinate math; the test host now runs PerMonitorV2 like the app, so CV captures are correct at 150% scaling. Not benchmarked on a physical multi-monitor desk. |

Any future numbers for these rows must come from a real harness with checked-in
ground truth. They must not be estimated and presented as measurements.

---

## 2. How the feature is wired into the application

```
H key
  └─ HintController.Open()
       └─ HintController.StartScan()
            └─ TrayApp composition root supplies HybridClickableScanner
                 └─ HintScanCoordinator chooses a mode from hint.scanMode
                      ├─ UIAutomationScanner  (UI Automation, MTA thread)
                      ├─ ComputerVisionScanner (screen capture + pixel analysis)
                      └─ TargetFusionEngine    (hybrid mode only)
                 └─ VisualTarget → ClickableTarget (bounds, text)
            └─ HintOverlay shows labels
```

Relevant types:

| Type | Role |
|---|---|
| `HybridClickableScanner` | Application adapter; implements the existing `IClickableScanner` so `HintController` does not need to change its target model. |
| `HintScanCoordinator` | Shared UIA/CV/auto/hybrid orchestration with one deadline and one terminal callback. |
| `UIAutomationScanner` | UIA3 traversal on an MTA thread, bounded depth/count, clipped to monitors. |
| `ComputerVisionScanner` | GDI capture + greyscale gradient/connected-component detector. |
| `TargetFusionEngine` | One-to-one UIA/CV matching by IoU, center distance, text, type, containment and spatial signals. |
| `HintActionExecutor` | Optional action layer with foreground, staleness, duplicate and click-point safety checks. |

The scan mode is read on every scan from `HintSettings.ScanMode`, so changing it
in the tray menu or reloading `settings.json` takes effect without a restart.

`auto` compares every UIA target against the window's client rectangle. A
custom-drawn window still exposes Minimize/Maximize/Close/System to UIA, so
"UIA returned something" is not enough to skip CV. If every UIA target is
outside the client area, auto waits for the concurrent CV result and fuses the
caption buttons with the CV content. Apps with real UIA content keep the fast
UIA-only path.

---

## 3. Computer-vision detector

`ComputerVisionScanner` is no longer a placeholder. Its pipeline is:

1. Capture the requested window handle with `CaptureRegion` (desktop pixels).
2. Down-sample to a greyscale grid whose longest side is at most 640 pixels.
3. Compute horizontal/vertical gradients and an adaptive edge threshold
   (clamped between 22 and 60) so flat UIs stay sensitive and photos do not
   produce edge noise everywhere.
4. Dilate the edge mask to connect letters and thin outlines.
5. Extract 8-connected components and convert each bounding box back to
   desktop coordinates through `HintCoordinates.FrameToDesktop`.
6. Filter tiny, huge, full-window and extreme-aspect components.
7. Drop boxes contained by a larger box or almost completely overlapped.
8. Deduplicate by center and cap to `hint.maxElements`.

OCR is an optional abstraction (`IOcrEngine`). The app currently uses
`NullOcrEngine`, so contour targets have empty text; an OCR backend can be
plugged in later without changing the detector.

The capture path previously never worked: `User32.GetDC` was declared with an
invalid `[return: MarshalAs(UnmanagedType.Bool)]` on an `IntPtr` return, which
threw `MarshalDirectiveException` for every capture. That declaration is fixed,
and the benchmark tests now exercise real GDI capture.

---

## 4. Security and privacy review (static inspection)

- **No screenshot uploads.** Capture is local GDI32 BitBlt into a
  `System.Drawing.Bitmap`; the scanner disposes it in a `using` block.
- **No OCR uploads.** `IOcrEngine` is a local interface; the default engine
  returns an empty list.
- **No persistent screenshots.** `ComputerVisionScanner` does not retain or save
  bitmaps.
- **No text logging.** `HintScanDiagnostics` contains counters/timings only.
  `VisualTarget.ToString()` omits text.
- **No elevation required.** UIA and GDI capture run in the interactive user
  session.
- **Action safety (when `HintActionExecutor` is used).** Foreground validation,
  a 5-second staleness check, duplicate-target prevention, bounds validation,
  and UIA-pattern-first / mouse-fallback rules.

These are code-inspection findings, not a penetration test.

---

## 5. Known limitations

1. **No bundled OCR.** CV targets are anonymous contours; labels still work, but
   the target has no readable name.
2. **Heuristic detection.** Very low-contrast, tiny (< ~10 px) or heavily
   anti-aliased controls may be missed.
3. **Text fragments.** The CV detector can emit separate word/letter components;
   containment deduplication reduces but does not eliminate this.
4. **Busy backgrounds.** Photos and gradients can add candidates; the size/aspect
   filters and the 2000-candidate cap bound the damage.
5. **UIA depth limit.** `DefaultMaxDepth = 16`; deeper trees are truncated.
6. **Mixed DPI.** Coordinates are canonical physical pixels, but CV size
   thresholds have only been exercised at 100% scaling.
7. **No ground-truth quality metrics.** Precision/recall are unknown until a
   checked-in corpus exists.
8. **Hybrid fusion threshold.** Some CV boxes inside UIA buttons do not fuse;
   the adapter removes them by containment before display.

---

## 6. Test status

| Check | Result |
|---|---|
| Release build | 0 errors, 0 warnings |
| Test suite | 332/332 passed |
| CV detector tests | Finds drawn rectangles; returns no synthetic targets on a blank capture; honours `Sources` |
| Adapter tests | `HybridClickableScanner` returns real `ClickableTarget`s in CV mode, handles a zero window, and falls back to CV in auto mode when UIA only sees caption buttons |
| Scan-mode tests | `auto`/`uia`/`cv`/`hybrid` normalization, source mapping and defaults |
| Real-window benchmarks | UIA, CV and Hybrid scans complete against a real WinForms window |
| UIA integration test | Real WinForms button found via FlaUI |

Benchmark tests are tagged `Category=Benchmark`. They run with the normal suite
and print their measurements; they assert completion and basic target validity,
not fabricated quality scores.

---

## 7. Release readiness

- [x] CV scanner wired into the running application (`HybridClickableScanner`).
- [x] Scan-mode setting and tray menu.
- [x] Real pixel-based CV detection (no synthetic rectangles).
- [x] Screen capture P/Invoke fixed and exercised by tests.
- [x] Single terminal callback in auto/hybrid orchestration.
- [x] Auto falls back to CV when UIA only sees caption buttons.
- [x] Contour deduplication and output cap.
- [x] Build clean; all tests pass.
- [x] No screenshot uploads or persistent captures.
- [x] Documentation corrected to remove unmeasured claims.
- [ ] Precision/recall measured against a checked-in ground-truth corpus.
- [ ] CPU/memory benchmarked.
- [ ] Cross-application compatibility matrix systematically verified.

The implementation is ready to run. The unchecked items are measurement work,
not missing wiring.

---

## 8. Recommended next steps

1. Bundle a local OCR engine (for example Tesseract) behind `IOcrEngine`.
2. Add a checked-in ground-truth corpus and compute precision/recall/F1 in CI.
3. Measure CPU, memory and scan cost on 1080p, 1440p and 4K displays.
4. Tune CV size thresholds per monitor DPI scale.
5. Verify the compatibility matrix on Notepad, Calculator, File Explorer,
   Chrome/Electron and a custom-drawn test app.
6. Consider an optional ONNX detector for low-contrast targets.
