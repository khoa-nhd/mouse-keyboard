# Mouse Keyboard — Release Criteria

## Supported UI Types

### Fully Supported
- Standard Win32 buttons and controls
- WPF/UWP applications with UI Automation support
- Web browsers (Chrome, Edge, Firefox) via UI Automation
- Custom-drawn applications via CV fallback
- Toolbar icons and button groups
- Menu items and dropdowns
- Checkboxes and radio buttons
- Tab strips
- Input fields and text boxes
- Hyperlinks

### Partially Supported
- Icon-only controls (detected via CV, clickability uncertain without UIA)
- Small controls (<16px) — high-resolution path available
- Dark mode applications — visual contrast may be reduced
- High-DPI displays — coordinates mapped correctly
- Multiple monitors — full desktop scan available

### Not Supported
- 3D-rendered UI (games, CAD applications)
- Terminal/console applications
- Remote desktop sessions
- Virtual machine guest UI
- Non-Windows platforms (Windows only)

## Known Limitations

1. **UI Automation dependency**: Best results require UIA support from the target application. Custom-drawn UI relies on CV heuristics with lower accuracy.
2. **Icon clickability**: Icon-only controls without UIA metadata have reduced clickability confidence.
3. **Decorative elements**: CV may detect decorative icons as candidates; clickability reasoning filters most but not all.
4. **Dynamic content**: Rapidly changing UI (animations, video) may produce stale targets.
5. **Small controls**: Controls below 12px may be missed by the CV pipeline.
6. **OCR accuracy**: Text recognition depends on font size, contrast, and language.
7. **No universal detection**: The system does not claim 100% detection rate.

## Required Model Files

- None required for core functionality (heuristic CV + UIA)
- Optional ML models (if integrated in future): MobileNetUI-v1.onnx (~5MB)
- Model files are NOT downloaded at runtime
- Models must be manually placed in the application directory

## Model Licenses

- No third-party ML models currently integrated
- FlaUI.UIA3: MIT License
- Microsoft.Web.WebView2: Microsoft Software License

## Hardware Requirements

- **Minimum**: Windows 10 1903+, 4GB RAM, 1GHz CPU
- **Recommended**: Windows 11, 8GB RAM, multi-core CPU
- **GPU**: Not required (CPU-only inference)
- **Display**: Any resolution, high-DPI supported
- **Monitors**: Single or multiple monitors supported

## Privacy Behavior

- **No network access** unless auto-update is explicitly enabled by the user
- **No screenshots uploaded** — all diagnostic data stays local
- **No OCR text logged** unless explicitly enabled for controlled testing
- **No user behavior tracking**
- **Diagnostic collection is opt-in** and clearly indicated
- **All diagnostic samples can be deleted** by the user

## Recovery Behavior

### Detector Failure
- If CV fails: UIA targets still available, scan continues
- If UIA fails: CV targets still available, scan continues
- If both fail: Empty target list, no crash, user notified
- If ML model fails (if integrated): Falls back to CV-only mode
- If OCR fails: Contour-based detection still works

### Resource Disposal
- All GDI bitmaps disposed after capture
- All ONNX sessions disposed on shutdown
- CancellationToken used for all async operations
- Bounded concurrency prevents resource exhaustion

### Timeout Behavior
- Default scan budget: 400ms for UIA, 2s for CV
- Overall scan timeout: 10s maximum
- Individual source timeouts are independent
- Cancelled scans return partial results when available

## Performance Characteristics

- **Full-screen scan (CV)**: ~200-500ms on typical desktop
- **UIA scan**: ~50-200ms depending on application complexity
- **Hybrid scan**: ~200-600ms (parallel UIA + CV)
- **Memory footprint**: ~50-100MB typical
- **CPU usage**: Low when idle, moderate during scan

## Release Checklist

- [ ] Build succeeds with 0 errors
- [ ] All tests pass (excluding 5 pre-existing known failures)
- [ ] No performance regression vs baseline
- [ ] Documentation updated
- [ ] Version number incremented
- [ ] Changelog updated
- [ ] Privacy behavior verified
- [ ] Recovery behavior tested
- [ ] Multi-monitor tested
- [ ] High-DPI tested
- [ ] Dark mode tested