# Changelog

All notable changes to KeyboardMouse are listed here. Format based on
[Keep a Changelog](https://keepachangelog.com).

## [0.3.0] - 2026-11-01

### Added
- A real app window. Launching the exe opens an illustrated guide (English and
  Tiếng Việt, light and dark): quick start, an interactive keyboard that
  lights up the keys you press, animated demos of grid / hints / scroll / auto
  click, a practice range, and settings (autostart, Start menu and desktop
  shortcuts, auto-click speed and button, pointer dot, screen border,
  copy/paste keys). Built on the Edge WebView2 runtime, served from embedded
  files only; falls back to a short message with a download link if the
  runtime is missing.
- New app icon (16–256 px) and matching tray icons.
- Running the exe again reopens the window instead of doing nothing; the
  autostart entry now starts silently with `--background` (existing entries
  are updated on the next launch).
- Auto clicker: `B` in mouse mode starts/stops clicking at the pointer
  position (default left button, every 100 ms). The pointer can keep moving
  while it runs; the dot turns yellow. It stops when mouse mode turns off, on
  panic, session lock and suspend. Configure with
  `autoClick.intervalMs` (10–60000), `autoClick.button` (Left/Right/Middle)
  and `keys.autoClick`.

### Changed
- Double-clicking the tray icon opens the guide; "Show keymap" opens its Keys
  tab. Mouse mode is toggled from the menu or Caps Lock as before.
- Release builds ship only `KeyboardMouse.exe` (no .pdb).

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
