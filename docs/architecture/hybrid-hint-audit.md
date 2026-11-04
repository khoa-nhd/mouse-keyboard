# Hybrid Hint System — Implementation and Validation

**Date:** November 4, 2026
**Status:** Implemented, wired into the app, 431/431 tests passing (Release build,
0 errors; only offline NuGet `NU1900` warnings).

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
before stopping the stopwatch. No fixed sleeps are used inside the measured
interval and no synthetic scanner results are used. The harness waits for the
topmost test window to be composed before the stopwatch starts, so the capture
does not race the first frame.

### 1.1 Measured smoke results (this machine, this window)

Final two fresh Release runs, n=3 per latency sample:

| Mode | Result status | Targets returned | One-shot elapsed | Latency test P50 / max (n=3) |
|---|---|---|---|---|
| UIA only | Success | 6 (both buttons found) | 36-46 ms | 44-46 ms / 47-48 ms |
| CV only | Success | 8 classified regions | 90-105 ms | 63-64 ms / 77-82 ms |
| Hybrid | Success | 10 targets | 209-223 ms | 61-75 ms / 70-85 ms |

CV scan scopes, same process and window:

| Scope | Targets | Elapsed | Notes |
|---|---|---|---|
| Foreground window | 8 | 124-127 ms | one tile |
| Active monitor | 88 | 437-475 ms | two tiles over a 1920x1200 monitor |
| Virtual desktop | 88 | 374-393 ms | one monitor in this environment |

Run in a fresh Release test process on a 150% display, after the test host was
switched to PerMonitorV2 to match the app. Earlier fresh runs varied by roughly
10-30 ms, so these are representative values rather than fixed promises.

Notes:

- The CV target count is the number after classification and strict geometry
  validation. The raw detector still emits text fragments and inner contours;
  the classifier rejects most borderless text, geometry validation rejects
  malformed or implausible candidates, and `TargetPipeline.Finalize` applies the
  deduplication, container-selection and final rules before labels are shown.
- Monitor and desktop scans include background windows, so their target counts
  vary with whatever is on screen; the counts above are from two final runs and
  are not a fixed expectation.
- The monitor/desktop counts are lower than the pre-geometry baseline because
  full-width containers, shared-window chrome and implausible bounds are now
  rejected instead of being shown as clickable hints. The foreground-window
  control count is unchanged.
- Hybrid started from 6 UIA + 8 CV observations and returned 10 targets after
  fusing matching pairs and collapsing remaining duplicate/redundant regions.
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
| Multi-monitor and mixed-DPI behavior | Unit-tested for planner geometry (negative origins, dual monitors, tile coverage) and per-monitor capture; not benchmarked on a physical multi-monitor desk. |

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
| `ComputerVisionScanner` | GDI capture + greyscale gradient/connected-component detector, pixel-level geometry refinement and type-specific validation. |
| `CvGeometryRefiner` | Local BGRA snapshot refinement: background flood-fill, component selection, shadow trimming, rounded-corner/edge/contrast measurement and distance-transform click points. |
| `TargetGeometryValidator` | Strict CV / diagnostic UIA validation for zero, tiny, huge, off-screen, clipped and invalid-transform geometry using type-specific, DPI-aware limits. |
| `TargetGeometrySelection` | Container-vs-child selection so a real control is not hidden by a pane, and a real button is not replaced by its label. Preserves nested interactive controls with different semantics. |
| `TargetFusionEngine` | One-to-one UIA/CV matching by IoU, center distance, text, type, containment, shape and visual grouping; global assignment; prefers CV bounds when UIA only found a container; merges UIA and CV metadata. |
| `TargetSimilarity` | Deterministic geometry/text/type/shape/grouping measurements and the shared duplicate predicate. |
| `TargetAssignment` | Maximum-weight bipartite assignment (Hungarian) with per-row unmatched columns. |
| `TargetRanker` | Composite score from detection, geometry, interaction, classification, source reliability, ambiguity and duplicate likelihood. |
| `TargetDeduplicator` | Union-find duplicate groups and semantic representative selection. |
| `TargetFinalFilter` | Explicit type, size, confidence and ambiguity rules for UIA-backed and CV-only targets. |
| `TargetPipeline` | Finalization order shared by every scan mode: filter, dedup, container-vs-child selection, stable ordering and neighbour-safe click points. |
| `HintActionExecutor` | Optional action layer with foreground, staleness, duplicate and click-point safety checks. |

The scan mode and scan scope are read on every scan from
`HintSettings.ScanMode` and `HintSettings.ScanScope`, so changing them in the
tray menus or reloading `settings.json` takes effect without a restart.

`auto` compares every UIA target against the window's client rectangle. A
custom-drawn window still exposes Minimize/Maximize/Close/System to UIA, so
"UIA returned something" is not enough to skip CV. If every UIA target is
outside the client area, auto waits for the concurrent CV result and fuses the
caption buttons with the CV content. Apps with real UIA content keep the fast
UIA-only path. When `hint.scanScope` is `monitor`, `desktop` or `all-monitors`,
auto always waits for CV and fuses the window-only UIA targets as complementary
evidence, because only CV covers the requested area.

---

## 3. Computer-vision detector

`ComputerVisionScanner` is no longer a placeholder. Its pipeline is:

1. Resolve `hint.scanScope` into physical-pixel regions
   (`ScreenScanPlanner`): the window, the active monitor, or each monitor
   separately for desktop/all-monitors. Per-monitor regions keep gaps between
   differently sized monitors out of the capture.
2. Split each region into overlapping tiles of at most 1280x1280 px with 96 px
   overlap, then capture each tile with `CaptureRegion` (desktop pixels).
3. Down-sample each tile to a greyscale grid whose longest side is at most 640
   pixels.
4. Compute horizontal/vertical gradients and an adaptive edge threshold
   (clamped between 22 and 60) so flat UIs stay sensitive and photos do not
   produce edge noise everywhere.
5. Dilate the edge mask to connect letters and thin outlines, then extract
   8-connected components and convert each bounding box back to desktop
   coordinates through `HintCoordinates.FrameToDesktop`.
6. Filter tiny, huge, full-window and extreme-aspect components.
7. Re-detect compact candidates in a padded native-resolution crop
   (220 px analysis grid) so small icons and check boxes keep accurate bounds.
8. Merge coarse/native duplicates while keeping nested controls inside
   containers.
9. Classify each candidate with `CvTargetClassifier` (border/interior edge
   density and greyscale variation), reject borderless text, separators and
   blank containers, and assign a type plus detection/interaction confidence.
10. Refine accepted candidates against the captured pixels in `CvGeometryRefiner`
    (described below): estimate the surrounding background, flood-fill it,
    select the control component, trim shadow tails, measure edge support,
    contrast, fill and rounded corners, then choose an interior click point with
    a distance transform.
11. Convert the refined bounds and click point through the non-throwing
    `HintCoordinates.TryFrameToDesktop`, validate them with type-specific limits,
    clip to the capture region, drop candidates inside the hint overlay's own
    "Scanning..." badge, then deduplicate by center/IoU and cap to
    `hint.maxElements`.

OCR is an optional abstraction (`IOcrEngine`). The app currently uses
`NullOcrEngine`, so contour targets have empty text; an OCR backend can be
plugged in later without changing the detector.

The capture path previously never worked: `User32.GetDC` was declared with an
invalid `[return: MarshalAs(UnmanagedType.Bool)]` on an `IntPtr` return, which
threw `MarshalDirectiveException` for every capture. That declaration is fixed,
and the benchmark tests now exercise real GDI capture.

### 3.1 Target geometry and click points

Every target carries a `TargetGeometry`:

| Field | Meaning |
|---|---|
| `Confidence` | Heuristic geometry quality in [0, 1], separate from detection confidence. It is a ranking signal, not a calibrated probability. |
| `ClickStrategy` | Why the point was chosen: `Center`, `SafeInterior`, `TextCenter`, `IconCenter`, `ShapeCenter` or `NeighborSafe`. |
| `Issues` | Non-fatal/fatal flags such as `TooSmall`, `ImplausiblyLarge`, `OffScreen`, `PartiallyOffScreen`, `ClickPointOutside`, `InvalidTransform`, `WeakEdges`, `ShadowIncluded`, `RoundedCorners`, `SharedBoundary`, `OverlapsNeighbor`, `AmbiguousShape`, `ContainerLike` and `UsedFallback`. Flags never contain text or pixels. |
| `EdgeSupport`, `BackgroundContrast` | Measured boundary support and normalized control/background colour distance. |
| `HasRoundedCorners`, `IsClipped`, `UsedFallback` | Shape and capture-edge metadata. |

Refinement is local and deterministic:

- A process-local BGRA copy of the capture is made once per frame; it is not
  retained after the scan.
- The background colour is estimated from the ring just outside the candidate,
  using the median channel value and its mean deviation. Background pixels
  connected to the crop edge are flood-filled, so the interior of a filled
  control is not accidentally treated as background.
- Connected components are restricted to the candidate plus a small halo, so a
  nearby control cannot be merged into this target. The component overlapping
  the candidate centre wins.
- Rows/columns with weak support are trimmed to remove drop-shadow tails. The
  trimmed box is rejected if it lost most of the component, falling back to the
  untrimmed component bounds instead.
- Rounded corners are detected only when the empty corner is connected to the
  outside background, so a square control with a same-colour fill is not
  mislabelled as rounded.
- Click points are selected from a mask of the control (or its rounded
  rectangle for filled controls). The algorithm samples the bounding-box centre,
  an OCR text centre when available, and the mask centroid, then picks a point
  with a large distance to the mask boundary. A triangular or icon-shaped
  control therefore never receives a point on its empty corner or outside its
  ink. `NeighborSafe` points are chosen separately, after the final target list
  exists.

`TargetGeometryValidator` applies type-specific minimum/maximum size and aspect
rules. Minimums scale with the square root of DPI (so 150% UI does not turn a
real 5 px check box into an error), maximums scale linearly, and all coordinates
stay in physical pixels. CV candidates are validated strictly: zero, tiny,
implausibly large, mostly off-screen, invalid-transform or click-outside
geometry is rejected and counted. UIA targets are kept by default because UIA is
authoritative, but suspicious geometry is flagged and receives low confidence so
it ranks below a clear control. A target can be clipped at a capture edge and is
then marked `PartiallyOffScreen`/`IsClipped`.

`TargetGeometryNeighbors.Protect` runs on the final list. When two targets share
or nearly share an edge, each point is moved toward its own interior and away
from the neighbour, keeping the point inside the original bounds. The bounds are
never changed. The affected targets are flagged `SharedBoundary` and, when they
actually intersect, `OverlapsNeighbor`.

Geometry confidence affects ranking without overriding source evidence:
`VisualTarget.RankingConfidence = DetectionConfidence * (0.25 + 0.75 *
Geometry.Confidence)`. Full final ordering uses `TargetRanker`, where geometry
confidence is one of seven normalized inputs alongside detection, interaction,
classification, source reliability, ambiguity and duplicate likelihood.
Deduplication, fusion matching and container selection prefer clearer geometry,
but UIA still wins when both sources describe the same real control.
`TargetGeometrySelection` removes a container-like target only when it fully
contains a clearly interactive child at least 2x smaller; a real UIA button is
kept when the nested CV target is just its text, and nested interactive controls
with different semantics are kept side by side. `TargetFusionEngine` prefers CV
bounds only when the UIA target is container-like and at least 2x larger than the
CV control inside it.

Conservative type rules are deliberately biased toward keeping a clickable
region: a refinement that cannot build a reliable mask returns the original
detector box with `UsedFallback` and low confidence instead of inventing a tiny
box. If the final candidate still violates strict type limits it is dropped
rather than clicked blindly.

### 3.2 Deduplication, ranking and UIA/CV fusion

Raw observations are not shown directly. `TargetPipeline.Finalize` applies the
same rules to `uia`, `cv`, `auto` and `hybrid` results:

1. `TargetFinalFilter` drops non-actionable targets and applies explicit type,
   size, confidence and ambiguity rules.
2. `TargetDeduplicator` collapses pairwise duplicates into union-find groups and
   chooses one representative per group.
3. `TargetGeometrySelection` removes a container that only wraps a real child
   and preserves nested controls with different interaction semantics.
4. The list is ordered top-to-bottom/left-to-right and
   `TargetGeometryNeighbors.Protect` moves click points away from shared edges.

Finalization is idempotent, so the fusion engine and the `ClickableTarget`
adapter can both call it without penalizing targets twice. This keeps the
existing `IClickableScanner` → `HintController` architecture unchanged.

Deduplication uses `TargetSimilarity.TryGetDuplicate`:

- a decorative text or icon box fully inside an interactive parent (the button
  wins) when the area ratio is at most 16;
- a container-like parent around an interactive child (the child wins) when the
  parent is at most 8x larger;
- the same or compatible type detected at multiple scales when the area ratio is
  at most 2.5 and the normalized centre distance is at most 0.30;
- overlapping boxes with IoU >= 0.55;
- high intersection-over-smaller (>= 0.80) with similar size and type;
- nearly identical small boxes (centre distance <= 0.08, size similarity >=
  0.70).

Disjoint bounds never match, and nested interactive controls of different types
are explicitly excluded, so two adjacent buttons do not collapse and a check box
or action button inside a list row stays a separate target. Decorative
text/icon children still collapse into their button. The representative is
chosen by semantics (interactive parent over decoration, interactive child over
a container), then UIA over CV, then the composite ranking and stable id.

Fusion scores every UIA/CV pair with IoU (0.28), centre distance (0.14),
containment (0.18), text (0.12), type compatibility (0.10), shape (0.08) and
visual grouping (0.10). Pairs below 0.55, or pairs with no overlap/containment
and a normalized centre distance above 0.60, are discarded. Remaining candidates
are split into connected components and each component is solved as a
maximum-weight bipartite assignment (`TargetAssignment`, Hungarian algorithm),
so one UIA control cannot greedily consume another control's only good CV match.
Components above 2,000,000 cells use a deterministic greedy start followed by
pairwise swap improvement. Unmatched UIA and CV targets survive independently;
the scanners are never required to agree.

A matched pair produces one target marked `Uia | Cv`. UIA supplies the
interaction contract (type, patterns, enabled state, automation id); CV supplies
visual metadata (OCR text/confidence, detector, frame) and may supply the
bounds/click point when UIA only found a container-like ancestor at least 2x
larger. The result is one label and one action per physical control.

Ranking is composite (`TargetRanker`): detection 0.24, geometry 0.22,
interaction 0.18, classification 0.12, source reliability 0.10, uniqueness 0.09
and non-duplicate 0.05, normalized by the weight sum. Ambiguity penalizes
text/unknown/other types, low interaction/classification confidence and geometry
flags; reliability is highest for fused targets, then UIA, then CV. Because
interaction and classification are separate inputs, an OCR region with 99%
detection confidence but 15% interaction confidence ranks below a slightly less
certain real button rather than becoming a hint.

Final rules (`TargetFilterOptions`): CV-only targets need detection >= 0.40,
interaction >= 0.25, geometry >= 0.25, ambiguity <= 0.85 and a supported
semantic type. UIA-backed targets need detection >= 0.30 and may map to `Other`,
because UIA still carries a real interaction contract. Both use the
type-specific `TargetGeometryValidator.LimitsFor` minimums; UIA gets a 4x
maximum/aspect guard for legitimately large controls. Failing targets are
dropped before deduplication. These values extend the earlier DPI-aware limits
and are covered by the resolution fixtures; they are ordering heuristics, not
calibrated probabilities.

---

## 4. Security and privacy review (static inspection)

- **No screenshot uploads.** Capture is local GDI32 BitBlt into a
  `System.Drawing.Bitmap`; the scanner disposes it in a `using` block.
- **No persistent pixel buffer.** Geometry refinement copies each frame once
  into a process-local BGRA byte array (`CvPixelSource`) for random access. The
  array is discarded when the scan ends and is never written to disk.
- **No OCR uploads.** `IOcrEngine` is a local interface; the default engine
  returns an empty list.
- **No persistent screenshots.** `ComputerVisionScanner`, `CvGeometry.cs` and
  `TargetGeometry.cs` do not retain or save bitmaps.
- **No text logging.** `HintScanDiagnostics` contains counters/timings only, and
  `TargetGeometry` contains numeric measurements and flags only.
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
2. **Text-only buttons.** A button drawn as text with no border and no fill is
   rejected by the conservative classifier. This is deliberate: accepting it
   would also accept every paragraph and label.
3. **Heuristic detection.** Very low-contrast, tiny (< ~10 px) or heavily
   anti-aliased controls may be missed. Strict CV validation drops a candidate
   whose refined box is ambiguous or implausible instead of offering a click
   point that may land on the wrong control.
4. **Containers and nested controls.** A large blank framed container is
   rejected, and `TargetGeometrySelection` removes a container only when a
   clearly interactive child at least 2x smaller is inside it. Extremely unusual
   layouts can still produce both a container and its child.
5. **Busy backgrounds.** Photos and gradients can add candidates; the size/aspect
   filters, classifier, geometry confidence and 2000-candidate cap bound the
   damage.
6. **UIA depth limit.** `DefaultMaxDepth = 16`; deeper trees are truncated.
7. **Mixed DPI.** Coordinates are canonical physical pixels, and monitor captures
   report `DpiScale = 1.0`. Size thresholds have been exercised at 150% scaling;
   a physical dual-monitor desk was not benchmarked.
8. **No ground-truth quality metrics.** Precision/recall are unknown until a
   checked-in corpus exists. The geometry tests use rendered synthetic controls
   with known rectangles, so they verify IoU and click containment but not
   application coverage.
9. **Hybrid matching is still heuristic.** Thresholds decide whether two boxes
   describe one control. A CV box inside a UIA button may not fuse; containment
   selection then removes the duplicate. A container-like UIA target can yield
   to a CV control only when it is at least 2x larger. The assignment itself is
   deterministic and globally optimal within each connected component, but the
   similarity weights are not learned from a corpus.
10. **Geometry confidence is not calibrated.** It combines edge support,
    contrast, fill and detector confidence, and is used for ranking. It is not a
    probability that the control is clickable.
11. **Click points can favour visible ink.** Text/icon/shape strategies use the
    mask centroid or text box when available. On a control with a large empty
    area and a small label, the point may be closer to the label than to the
    geometric centre; it is still inside the refined bounds and mask.

---

## 6. Test status

| Check | Result |
|---|---|
| Release build | 0 errors (offline NuGet `NU1900` warnings only) |
| Test suite | 431/431 passed |
| CV detector tests | Finds drawn rectangles; returns no synthetic targets on a blank capture; honours `Sources`; excludes overlay regions |
| Geometry refinement tests | Rounded, shadowed, outlined-with-centred-label, filled and irregular controls; measured IoU >= 0.75 for the refiner fixtures, click point inside the ground-truth control; rounded/shadow/fallback flags |
| Full-scanner geometry tests | Synthetic rounded button, adjacent buttons and text field; IoU and click-containment assertions through the real capture/refine/validate path |
| Neighbour tests | Shared-edge buttons get distinct interior points; overlapping targets are flagged `OverlapsNeighbor`; re-protection does not penalize confidence twice |
| Validation tests | Zero, tiny, oversized, off-screen, partially off-screen and click-outside bounds; type-specific checkbox/edit rules; DPI scaling; invalid frame transforms |
| Filter/fusion/selection tests | Explicit click-point preservation and clipping; container-vs-child selection; real UIA button kept over nested CV text; CV control bounds preferred over a UIA container; geometry-confidence ranking |
| Target resolution tests (15) | OCR text and icon inside a button collapse to the button; multiple contours/scales collapse to one target; adjacent buttons and repeated icons stay separate; nested check box and action button survive; UIA/CV merge keeps both metadata sets and one action; conflicting scanner results keep the correct pair; valid UIA-only and CV-only targets survive; high-confidence OCR text ranks below a real button; explicit final filter drops text/tiny/oversized CV targets; Hungarian assignment chooses the global optimum instead of the first greedy edge; a dense 4x5 UIA/CV interface resolves to exactly 20 targets |
| Scope/planner tests | Window/monitor/desktop resolution, negative origins, dual monitors, tile coverage and overlap |
| Classifier tests | Buttons, icon buttons, check boxes, edit fields; rejects labels, separators, containers and tiny regions |
| Capture tests | Per-monitor tiled capture and native-pixel metadata |
| Adapter tests | `HybridClickableScanner` returns real `ClickableTarget`s in CV mode, handles a zero window, and falls back to CV in auto mode when UIA only sees caption buttons |
| Scan-mode/scope tests | `auto`/`uia`/`cv`/`hybrid` and `window`/`monitor`/`desktop`/`all-monitors` normalization, source mapping, budgets and settings propagation |
| Real-window benchmarks | UIA, CV and Hybrid scans complete against a real WinForms window |
| UIA integration test | Real WinForms button found via FlaUI |

Benchmark tests are tagged `Category=Benchmark`. They run with the normal suite
and print their measurements; they assert completion and basic target validity,
not fabricated quality scores.

Real UI Automation tests (`BenchmarkTests`, `HybridClickableScannerTests` and
`UIAutomationScannerIntegrationTests`) share one serialized xUnit collection.
Running multiple FlaUI automations concurrently made `UIA3Automation.FromHandle`
intermittently fail with `E_FAIL`, so these tests also use a test-only 5-second
UIA timeout; the production timeout remains 400 ms. Real windows are brought to
the top and given one composition interval before capture.

---

## 7. Release readiness

- [x] CV scanner wired into the running application (`HybridClickableScanner`).
- [x] Scan-mode setting and tray menu.
- [x] Scan-scope setting (`window`/`monitor`/`desktop`/`all-monitors`) and tray menu.
- [x] Per-monitor tiled capture with negative-origin and mixed-DPI handling.
- [x] Conservative pixel classifier and overlay exclusion.
- [x] Real pixel-based CV detection (no synthetic rectangles).
- [x] Pixel-refined bounds with background/shadow/rounded-corner handling.
- [x] Interior click points with centre, safe-interior, text, icon and shape strategies.
- [x] Type-specific, DPI-aware geometry validation and diagnostics.
- [x] Neighbour-safe click points and geometry-confidence ranking.
- [x] Container-vs-child selection and CV-over-container fusion.
- [x] OCR/icon-in-button, multi-contour/scale/strategy and repeated-overlap deduplication.
- [x] Deterministic global UIA/CV assignment with a Hungarian solver.
- [x] Composite ranking with a separate classification confidence.
- [x] Explicit final type, size, confidence, ambiguity and nested-control rules.
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
2. Add a checked-in ground-truth corpus and compute precision/recall/F1 plus
   per-target IoU/click validity in CI.
3. Measure CPU, memory and scan cost on 1080p, 1440p and 4K displays.
4. Calibrate geometry confidence and click-point strategies against that corpus.
5. Verify the compatibility matrix on Notepad, Calculator, File Explorer,
   Chrome/Electron and a custom-drawn test app.
6. Consider an optional ONNX detector for low-contrast targets.
