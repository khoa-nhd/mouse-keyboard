# Changelog

All notable changes to KeyboardMouse are listed here. Format based on
[Keep a Changelog](https://keepachangelog.com).

## [0.2.0] - 2026-11-01

### Added
- Editing extras: with `editingExtras` on (default), `Y` sends Ctrl+C, `P`
  sends Ctrl+V and `Z` sends Ctrl+Z while in mouse mode.
- Every binding in `keys` can now be remapped in `settings.json` (movement,
  buttons, layers, jumps, grid/hint openers and marks). Duplicate bindings
  keep the first action; invalid names fall back to the default.
- Auto-off: `activation.autoOffSeconds` (0 = off) leaves mouse mode and
  releases all buttons after that many idle seconds.
- Mouse mode now turns off and releases everything when Windows locks the
  session or the machine suspends.
- Hook watchdog: every 5 s the hook is probed with a system-ignored key and
  reinstalled if Windows silently removed it.

### Fixed
- A physically held Shift or Alt no longer corrupts synthesized chords
  (Ctrl+C stays Ctrl+C): the chord batch temporarily releases and restores
  them.

### Internal
- `NormalController` now routes every key through a settings-built map.
- 128 unit tests (up from 117).

## [0.1.0] - 2026-10

First working build of Phases 0–7:
- Caps Lock tap to toggle mouse mode, hold for momentary mode, Shift+Caps for
  real Caps Lock, Ctrl+Alt+Backspace panic.
- IJKL movement with acceleration, precision (`S`), turbo (`A`) and scroll
  (`D`) layers, smooth scrolling, DPI scaling and TrackPoint re-sync.
- All buttons: Space left, F right, E middle, R double, U/O back/forward,
  V drag lock, T click-and-exit.
- Grid mode (`G`), hint labels (`H`), jumps to screen/window/next monitor
  (X/C/N), saved marks (`M` / `'`).
- Tray app with keymap window, settings reload, autostart toggle, per-monitor
  DPI manifest, cursor dot and screen border feedback.
