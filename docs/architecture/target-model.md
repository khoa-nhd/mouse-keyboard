# Shared Hint Target Model

**Status:** Shared model and independent `UIAutomationScanner`, `ComputerVisionScanner`, and `HybridHintScanner` implemented. The scanners are not wired into Hint mode by default, and no fusion or action executor is implemented yet. A `TargetFusionEngine` is available for future hybrid-target merging.

## Canonical coordinate system

All target geometry uses **physical pixels in the Windows virtual-desktop coordinate space**.

- Origin is `(SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN)` and may be negative.
- X grows right; Y grows down.
- Rectangles use `TargetBounds(X, Y, Width, Height)` with exclusive right/bottom edges.
- Width and height must be positive.
- A target's click point must be inside its bounds.
- Bounds are never DIPs, monitor-local pixels, or normalized 0–1 values. DPI scale is metadata for sizing thresholds only.

This is the same space already used by `MonitorInfo.Bounds`, UIA `BoundingRectangle` in this PerMonitorV2 process, `GridMath`, `MouseOutput.MoveTo`, and `HintOverlay` placement.

Frame-local CV observations are converted with:

```csharp
HintCoordinates.FrameToDesktop(frameBounds, localBounds, scaleX, scaleY)
```

`scaleX` and `scaleY` are desktop pixels per frame pixel; use `1.0` for a native-resolution capture. `DesktopToFrame` reverses the conversion for diagnostics.

## Shared types

| Type | Purpose |
|---|---|
| `VisualTarget` | Immutable target crossing the scanner, fusion, overlay, and action boundaries. Carries id, source flags, semantic type, optional text, canonical bounds, click point, detection/interaction confidence, enabled/visible state, and optional metadata. |
| `TargetSource` | Flags: `Uia`, `CvOcr`, `CvContour`, plus the `Cv` and `All` combinations. |
| `TargetType` | Semantic kind such as button, hyperlink, menu item, edit, or icon. |
| `TargetActionType` | `LeftClick`, `RightClick`, or `Hover`. |
| `TargetBounds` | Canonical physical-pixel rectangle with validation, containment, intersection, and clipping helpers. |
| `TargetMetadata` | Optional `Uia` and `Cv` metadata. |
| `UiaTargetMetadata` | Plain UIA fields only: automation id, runtime id, control type, class name, Invoke support, raw source bounds, keyboard-focusability, offscreen state, and supported patterns. No COM element or FlaUI type. |
| `UiaPatternFlags` | Neutral flags for Invoke, SelectionItem, Toggle, ExpandCollapse, Value, ScrollItem, Selection, GridItem, and RangeValue. |
| `CvTargetMetadata` | OCR text/confidence, frame id, detector name, and frame-local source bounds. |
| `HintScanOptions` | Existing UIA options plus `Sources` and an overall `Budget`. |
| `HintScanRequest` | Foreground window, options, and cancellation token. |
| `HintScanResult` | Terminal status, targets, optional diagnostics, and failure reason. |
| `HintScanStatus` | `Success`, `NoTargets`, `TimedOut`, `Canceled`, or `Failed`. |
| `HintScanDiagnostics` | Local-only counts and timings; never contains text or pixels. |
| `IHintScanner` | Asynchronous scan port with exactly-once completion. |
| `IHintActionExecutor` | Executes a click or hover against a resolved target. |

## UIA scanner

`UIAutomationScanner` implements `IHintScanner` and emits only normalized `VisualTarget` values.

- It scans only the requested foreground window and, when enabled, `Shell_TrayWnd`.
- It traverses direct children breadth-first with a default depth limit of 16 and a visited-node limit of 5000.
- It skips structural containers, disabled elements, offscreen elements, invalid bounds, and elements outside every monitor.
- It prioritizes Button, Edit, CheckBox, RadioButton, ComboBox, TabItem, MenuItem, Hyperlink, ListItem, TreeItem, and related item controls. Other control types are included only when they expose an actionable pattern.
- Each property, pattern, and child enumeration read has its own error boundary; a broken element cannot abort its siblings.
- UIA3 is created, used, and disposed on one MTA background thread.
- The scan deadline is the lower of `HintScanOptions.Budget` and the 400 ms UIA cap. Cancellation and timeout each complete exactly once.
- Results are clipped to the first intersecting monitor, deduplicated by visible center, sorted top-to-bottom/left-to-right, and capped by `MaxElements`.
- The scanner seals targets with a `TargetMetadata` copy. It retains no UIA element, COM object, or `UIA3Automation` after the scan.
- Labels are not generated here; the controller will own label assignment.

## Boundaries

- Shared models reference only BCL types and `System.Drawing` value types.
- They do not reference FlaUI, OpenCV, OCR libraries, WinForms controls, overlays, or scanners.
- `VisualTarget.Text` and metadata text are in-memory only. `ToString()` omits text.
- Scanners own raw observations and normalize them before producing `VisualTarget` values.
