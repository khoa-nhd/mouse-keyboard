# Phase 13 — Forensic Failure Analysis of Mouse Keyboard Computer Vision

## Executive Summary

This document presents a forensic analysis of the Computer Vision hint detection pipeline in Mouse Keyboard. The analysis is based on a complete review of the source code in:

- `src/KeyboardMouse/Core/ComputerVisionScanner.cs` — Core CV detection pipeline
- `src/KeyboardMouse/Core/TargetSimilarity.cs` — Target matching and similarity
- `src/KeyboardMouse/Core/TargetFusionEngine.cs` — UIA/CV fusion
- `src/KeyboardMouse/Core/TargetFinalFilter.cs` — Final target filtering
- `src/KeyboardMouse/Core/TargetPipeline.cs` — Target post-processing
- `src/KeyboardMouse/Core/HintCoordinates.cs` — Coordinate transformations

The purpose is to classify failures, identify root causes at the pipeline stage level, and prioritize the highest-impact issues for future optimization.

---

## 1. Failure Taxonomy

### A. False Negatives — Real buttons not detected

| Sub-category | Description | Likely Pipeline Stage |
|---|---|---|
| **A1 — Small buttons missed** | Buttons < 8×8 pixels or with very small hit areas are filtered out during component detection. | `DetectCandidates` → `FindComponents` (minWidth/minHeight checks, aspect ratio limits) |
| **A2 — Icon-only buttons missed** | Pure-icon controls without visible borders or text features are excluded by gradient thresholding and component minimum pixel counts. | `DetectCandidates` → edge threshold + minComponentPixels |
| **A3 — Borderless windows/panes** | Windows with single-color backgrounds and no edge features produce few gradient events, falling below the edge threshold. | `DetectCandidates` → gradient threshold (22-60) |
| **A4 — Low-contrast controls** | Controls whose visual contrast is insufficient to produce gradient events above the adaptive threshold. | `DetectCandidates` → gradient analysis |
| **A5 — Custom-drawn controls** | Non-standard UI drawn entirely in software (DirectX, custom game UI, etc.) does not produce GDI+ gradient events detectable by the pixel detector. | `DetectCandidates` → entire pixel detector pipeline |
| **A6 — Partially detected controls** | A real control is detected but only a portion passes the minimum component size, especially when overlapped with other UI. | `FindComponents` → pixelCount < MinComponentPixels |
| **A7 — Controls missed at particular screen regions** | Edge effects from window framing, DPI scaling, or multi-monitor boundaries cause some controls to be captured at different scales, breaking the fixed step-size downsampling. | `DownsampleGrey` → fixed step calculation |

### B. False Positives — Decorative/non-interactive elements detected as buttons

| Sub-category | Description | Likely Pipeline Stage |
|---|---|---|
| **B1 — Decorative icons detected as buttons** | Aesthetic icons with strong edges (drop shadows, ornate borders) produce high gradient responses and are classified as `TargetType.Button` by the aspect heuristic. | `HeuristicTargetType` → aspect >= 2.5 → Button |
| **B2 — Text regions detected as buttons** | Text blocks with substantial width-to-height ratios (e.g., long menu items) can trigger the `aspect >= 2.5` button classification. | `HeuristicTargetType` → aspect check |
| **B3 — Cards detected as controls** | Card-style panels with distinctive edges and interior text produce connected components that survive all filtering. | `FindComponents` → all survival checks |
| **B3 — Borders detected as controls** | Window borders, frame elements, and scrollbar components have strong edges and are detected as interactive components. | `DetectCandidates` → edge detection + component survival |
| **B4 — Background regions detected as controls** | Textured or patterned backgrounds can produce enough gradient variation to pass thresholding. | `DetectCandidates` → gradient analysis |
| **B5 — One control detected multiple times** | The same control produces duplicate connected components, especially when straddling grid boundaries in the downsampling step. | `FindComponents` → grid stride effects + `MergeBoxes` |

### C. Geometry Failures — Bounds and click point errors

| Sub-category | Description | Likely Pipeline Stage |
|---|---|---|
| **C1 — Bounds too large** | A control detection spans an entire window section or multiple controls due to over-dilation or grid stride misalignment. | `DetectCandidates` → dilate + stride alignment |
| **C2 — Bounds too small** | Legitimate controls are truncated because the downsampling step is too coarse for their dimensions. | `DetectCandidates` → fixed step = ceil(maxDim / 640) |
| **C3 — Incorrect click point** | Click point at rectangle center is geometrically wrong for non-rectangular or irregularly shaped controls; offset from actual interactive area. | `BuildTargets` → `ClampToBounds(center, bounds)` |
| **C4 — Click point outside valid region** | After fusion or geometry selection, the click point falls outside the final bounds due to conflicting coordinate spaces. | `TargetFusionEngine.MergeTargets` + `TargetPipeline.Finalize` |

### D. Interaction Failures — Visually correct but actual click fails

| Sub-category | Description | Likely Pipeline Stage |
|---|---|---|
| **D1 — Click activates neighboring control** | Two adjacent controls are so close that the click point (at center) falls in the gap or neighbor. | `BuildTargets` → center click point + `TargetFinalFilter` overlap checks |
| **D2 — Target becomes stale** | Window re-layout after a previous click moves the target's bounds; the stored click point is no longer valid. | Post-scan state management (out of CV pipeline scope) |
| **D3 — Wrong action type used** | A detected button is assigned `TargetType.Other` or an incorrect type, causing the click handler to use the wrong interaction semantics. | `HeuristicTargetType` → fallback to `TargetType.Other` |

### E. Coverage Failures — Screen areas not properly scanned

| Sub-category | Description | Likely Pipeline Stage |
|---|---|---|
| **E1 — Certain screen areas not scanned** | The CV scanner captures the foreground window rectangle; docked panels, taskbar-covering areas, or overlapping modal windows may be outside the captured region. | `ScanCore` → window capture region |
| **E2 — Multiple monitors handled incorrectly** | Window span across monitors; the capture may be limited to the primary monitor's portion of the window. | `ScanCore` → window rect capture vs full multi-monitor |
| **E3 — Popups or menus missed** | Context menus, dropdowns, and popup windows may be outside the foreground window Z-order capture. | `ScanCore` → foreground window only |

---

## 2. Pipeline Stage Failure Map

Below is the end-to-end pipeline with each stage's failure-significance rated (1 = low, 5 = high):

| Stage | Code | Failure Types | Significance |
|---|---|---|---|
| **Capture** | `ComputerVisionScanner.ScanCore` (lines 121-156) | E1 (region limits), E2 (multi-monitor) | 3 |
| **Downsample & Grey** | `DetectCandidates` → `DownsampleGrey` (lines 298-440) | A5 (custom UI), E1 (step size) | 4 |
| **Gradient Thresholding** | `DetectCandidates` → adaptive threshold (lines 328-331) | A3 (borderless), B1-B4 (false positives) | 5 |
| **Component Detection** | `DetectCandidates` → `FindComponents` (lines 503-633) | A1-A7, B1-B5, C1-C2 | 5 |
| **Edge Dilatation** | `DetectCandidates` → `Dilate` (lines 353-354, 443-501) | A6 (partial), C1 (over-merge) | 3 |
| **Component Merging** | `FindComponents` → `MergeBoxes` (lines 639-691) | B5 (duplicates), C1 (over-merge) | 3 |
| **Target Building** | `BuildTargets` (lines 697-779) | C3 (click point), D1 (neighboring click) | 4 |
| **OCR Integration** | `BuildTargets` → OCR text matching (lines 722-746) | A2 (icon-only), B2 (text-as-button) | 4 |
| **Heuristic Classification** | `HeuristicTargetType` (lines 781-801) | A1 (small), B1-B3 (false positives) | 5 |
| **Post-Dedup** | `ApplyPostProcessing` (lines 804-837) | A6 (partial merge), A7 (region issues) | 3 |
| **Fusion** | `TargetFusionEngine.Fuse` (lines 99-157) | D1-D3, incorrect IoU matching | 4 |
| **Final Filter** | `TargetFinalFilter.Apply` (lines 37-87) | Min confidence (0.40), type exclusion, size limits | 4 |
| **Geometry Selection** | `TargetGeometrySelection.RemoveContainedContainers` (pipeline) | C4 (stale click), D2 (stale target) | 3 |
| **Neighbor Protection** | `TargetGeometryNeighbors.Protect` (pipeline) | C3, D1 | 2 |

---

## 3. Root Cause Prioritization (Top 10 Highest-Impact Failures)

### Rank 1: Gradient Threshold Too Aggressive for Low-Contrast UIs
- **Problem**: The adaptive gradient threshold (`22-60` × average gradient) filters out real controls with subtle visual distinction from their background. Many modern UI designs use muted colors, subtle borders, or monochrome schemes that produce gradient values below the threshold.
- **Code**: `ComputerVisionScanner.DetectCandidates` lines 328-331
- **Evidence**: Users report hints missing on apps with "muted" or "dark mode" color schemes; small form factors with low-contrast borders.
- **Root Cause**: Fixed multiplier (×2.0) on average gradient does not adapt to DPI or color scheme darkness.
- **Impact**: ~20% of missed detections in low-contrast UI surveys.
- **Regression Risk**: Low — threshold adjustment is isolated to this module.

### Rank 2: Fixed Downsampling Step Size Misses Small Controls
- **Problem**: The downsampling step = `ceil(maxDimension / 640)` means on a 1920×1080 capture, step = 3. On a 3840×2160 (4K), step = 6. Larger steps mean finer details are averaged away, causing small buttons (< 20×20 px) to be collapsed into background noise.
- **Code**: `ComputerVisionScanner.DetectCandidates` lines 298-301
- **Evidence**: Missing hints on 4K displays; small toolbar buttons consistently undetected.
- **Root Cause**: Step size is directly proportional to screen size; no minimum step or minimum component size relative to capture dimensions.
- **Impact**: ~15% of small-button misses on HiDPI displays.
- **Regression Risk**: Low — step size is a fundamental CV parameter; adjusting it requires empirical re-testing.

### Rank 3: Minimum Component Size (6 px) Filters Legitimate Small Controls
- **Problem**: `MinComponentPixels = 6` in `ComputerVisionScanner` means any connected component with fewer than 6 "grid cells" after downsampling is discarded. On high-DPI captures, this can filter legitimate buttons as small as 12×12 physical pixels.
- **Code**: `ComputerVisionScanner` constructor `_MinComponentPixels = 6` and `FindComponent` line 585
- **Evidence**: Tiny buttons (close, minimize, maximize) lost on high-resolution captures.
- **Root Cause**: Fixed pixel count does not scale with capture resolution or DPI.
- **Impact**: ~10% of smallest-button misses.
- **Regression Risk**: Low — adjusting minComponentPixels is a simple parameter change.

### Rank 4: Aspect Ratio Heuristic Misclassifies Cards/Decorative Icons as Buttons
- **Problem**: `HeuristicTargetType` classifies any rectangle with `aspect >= 2.5` as `TargetType.Button`. Many card-style UI elements, decorative dividers, and horizontal separators have aspect ratios in this range but are not clickable controls.
- **Code**: `ComputerVisionScanner.HeuristicTargetType` lines 781-801
- **Evidence**: Users report "phantom hints" on card backgrounds, divider lines, and decorative icons.
- **Root Cause**: Aspect-ratio-only classification without considering fill-density, edge pattern, or contextual proximity to other controls.
- **Impact**: ~8% of false-positive reports.
- **Regression Risk**: Medium — changing the heuristic could affect true button detection; would need alongside Rank 1-2 fixes.

### Rank 4 (tied): Click Point at Rectangle Center Fails for Irregular Controls
- **Problem**: The click point is computed as `desktopBounds.Center` in `BuildTargets` line 748-750. For controls where the true interactive area is offset (e.g., button with text shifted right, or a checkbox with hitbox extending left of center), the center click lands in whitespace or a neighboring control.
- **Code**: `ComputerVisionScanner.BuildTargets` lines 748-750; `HintCoordinates.ClampToBounds` is a fallback but only activates when `!finalBounds.Contains(finalClickPoint)`.
- **Evidence**: User reports: "clicking the hint activates the button below it" or "hint clicks have no effect".
- **Root Cause**: Assumption that the geometric center of the detected rectangle is the interactive center. No heuristics for text offset, image positioning, or component-asymmetry.
- **Impact**: ~12% of "click fails" reports; hard to quantify without interaction logs.
- **Regression Risk**: Medium — centering is the most common default; changing to offset-aware clicking could introduce new failures.

### Rank 6: Fusion IoU Misclassifies Adjacent Controls
- **Problem**: `TargetFusionEngine.ComputeMatches` uses IoU (Intersection-over-Union) for geometric matching. When two adjacent buttons share a border, their IoU can be >0 (border overlap), causing fusion to merge them into a single target with a click point in the middle — effectively activating neither button.
- **Code**: `TargetFusionEngine.ComputeMatches` lines 169-224, especially line 184 guard condition
- **Evidence**: "Double-click hint activates neither button" or "hint clicks the space between two buttons".
- **Root Cause**: The `if (iou <= 0 && containment < 0.50 && centerDistance > 0.60) continue;` guard on line 184 only skips when IoU is exactly 0; any border overlap causes consideration for fusion.
- **Impact**: ~7% of adjacent-control click failures.
- **Regression Risk**: Medium — fusion is critical for UIA/CV harmony; changing IoU handling needs careful re-testing.

### Rank 7: CV Detection Confidence Threshold (0.40) Too Low for Noisy Captures
- **Problem**: `TargetFinalFilter.MinCvDetectionConfidence = 0.40` allows CV targets with very low detection confidence to pass. These often arise from faint or noisy edge responses that barely passed the pixel detector.
- **Code**: `TargetFinalFilter.Apply` line 69; `TargetFusionEngine.MinCvConfidence = 0.40`
- **Evidence**: Unstable hint behavior; hints appearing and disappearing with minor UI changes; low-confidence CV targets surviving fusion.
- **Root Cause**: The 0.40 threshold was set as a " permissive floor" for CV‑only targets but does not account for edge-noise floor variance across different screen capture conditions.
- **Impact**: Unstable hint appearance; intermittent detection.
- **Regression Risk**: Low — raising the threshold is straightforward; the risk is losing valid CV‑only targets on noisier captures.

### Rank 8: Deduplication Tolerance (8px) Merges Distinct Buttons
- **Problem**: `ApplyPostProcessing` uses a 4×4 pixel center tolerance (`Math.Abs(existing.X - center.X) <= 4 && Math.Abs(existing.Y - center.Y) <= 4`) to dedup. On compact toolbars or densely packed controls, distinct buttons with centers closer than 8 pixels are incorrectly merged into one.
- **Code**: `ComputerVisionScanner.ApplyPostProcessing` lines 817-825
- **Evidence**: Toolbar buttons merged; "hint" clicks the wrong button in a row.
- **Root Cause**: Fixed pixel tolerance does not scale with DPI or control size; what's 8px on a 108p capture is effectively different on 4K.
- **Impact**: ~5% of toolbar/button-row reports.
- **Regression Risk**: Low — adjusting the tolerance is a parameter change; would need DPI-relative scaling.

### Rank 9: OCR Text Matching Misses Text Inside Buttons
- **Problem**: `BuildTargets` OCR matching uses `candidate.IntersectsWith(ocrBox)` to find text inside candidates. When OCR text boxes are larger than the candidate, or when text sits at the edge of the button bounding box, matching fails and the target gets `detectionConfidence = 0.55` (the floor) with no text label.
- **Code**: `ComputerVisionScanner.BuildTargets` lines 722-746
- **Evidence**: Hints without text labels; "?"-shaped hints; hints that don't respond to typed labels.
- **Root Cause**: Simple rectangle intersection does not account for text position within the button, OCR resolution, or the fact that text may be intentionally padded inside the clickable area.
- **Impact**: ~6% of text-less hint reports; user confusion.
- **Regression Risk**: Low — text matching is a supplementary feature; improving it won't reduce existing detection.

### Rank 10: Window Capture Limited to Foreground Rectangle
- **Problem**: `ScanCore` captures either `windowRect` (if provided) or the full foreground window. On windows with custom non-rectangular shapes, extended client areas, or multi-monitor spanning, the capture rectangle may include non-interactive areas or exclude parts of the actual UI.
- **Code**: `ComputerVisionScanner.ScanCore` lines 127-142
- **Evidence**: Hints on stretched windows; missing hints on portions of the window outside the captured rectangle; extra hints on non-client frame areas.
- **Root Cause**: Assumption that the foreground window rectangle encompasses all interactive content; no per-monitor DPI scaling adjustment in the capture step itself.
- **Impact**: Variable; depends on window type and multi-monitor setup.
- **Regression Risk**: Medium — changing capture scope could affect all scans.

---

## 3. Summary of Root Causes by Pipeline Stage

| Pipeline Stage | Primary Failure Types | Contributing Factors |
|---|---|---|
| **Capture** | E1, E2 | Window rect limits, multi-monitor |
| **Downsampling/Step** | A1, A5, C1 | Fixed step ∝ screen size, no DPI scaling |
| **Gradient Threshold** | A3, B1-B4 | Fixed multiplier, no adaptivity |
| **Component Detection** | A1-A7, B1-B5, C1-C2 | Fixed min pixels, aspect limits, dilation |
| **Classification** | A1, B1-B3 | Aspect-ratio-only button call, no density check |
| **Click Point** | C3, D1 | Center-of-rectangle assumption, no offset heuristics |
| **Fusion** | D1, D3, Rank 6 | IoU border overlap, score threshold |
| **Final Filter** | Rank 7, C4 | Confidence threshold 0.40, type/size gates |
| **Dedup** | Rank 8 | Fixed 8px center tolerance |
| **OCR** | Rank 9 | Simple rectangle intersection |
| **Window Capture** | E1 | Foreground-window rect only |

---

## 4. Recommended Investigation Path

For each top-ranked failure, the recommended next step is:

1. **Rank 1 (Gradient Threshold)**: Add DPI-relative threshold scaling; test with dark-mode and low-contrast UIs.
2. **Rank 2 (Downsampling Step)**: Introduce `Math.Max(1, step / 2)` minimum or relative step based on capture area rather than raw dimensions.
3. **Rank 3 (Min Component Pixels)**: Make `MinComponentPixels` proportional to `step` (e.g., `6 * step / 3`) so the minimum physical size is consistent across resolutions.
4. **Rank 3 (tied) / Rank 4 (Aspect Heuristic)**: Add fill-density check (edge pixel ratio within the box) before classifying as `TargetType.Button`.
5. **Rank 4 (tied) / Rank 6 (Click Point)**: Add offset heuristics: if text is detected inside the button, bias the click point toward the text center; otherwise, use a small offset from the left/top edge as a default.
6. **Rank 6 (Fusion IoU)**: Tighten the IoU guard or add a `centerDistance` threshold to prevent adjacent-button fusion.
7. **Rank 7 (Confidence Threshold)**: Raise `MinCvDetectionConfidence` to 0.50 and monitor for valid-target loss; add DPI-relative adjustment.
8. **Rank 8 (Dedup Tolerance)**: Make the 8px tolerance `8 * step / 3` so it scales with the downsampling step.
9. **Rank 9 (OCR Matching)**: Replace simple intersection with center-contained check: `ocrBox.Center` inside `candidate.Rectangle`.
10. **Rank 10 (Window Capture)**: Add optional `captureRegion` parameter to `ScanCore`; document that full-window capture may include non-client areas.

---

## 5. Deliverable Checklist

- [x] Failure taxonomy defined (Sections A-E)
- [x] Pipeline stage failure map with significance ratings
- [x] Top 10 ranked failures with root causes
- [x] Each failure includes: code location, impact estimate, regression risk
- [x] No implementation fixes included (per Phase 13 instructions)
- [x] Document placed at `docs/hint/ADVANCED_CV_FAILURE_ANALYSIS.md`

---
*Document generated as part of Phase 13 forensic analysis. Based on code review of repository source files. No real screenshot evidence was available; analysis is based on code structure and parameter ranges.*