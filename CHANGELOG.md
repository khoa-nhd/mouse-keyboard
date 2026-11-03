# Changelog

All notable changes to KeyboardMouse are listed here. Format based on
[Keep a Changelog](https://keepachangelog.com).

## [1.0.1] - 2026-11-03

### Enhanced
- **Longer comet trail (especially in Turbo / fast mode):**
  - Doubled maximum trail samples (up to 300 samples when holding `A` / Turbo mode).
  - Increased trail lifetime duration and smoother point spacing for a much more dramatic, fluid comet tail.
  - Expanded reach bounds so fast-moving trails never get clipped.

## [1.0.0] - 2026-11-02

### Changed
- **Final public release polish pass:**
  - Fixed category filter chips layout on Keys page (`.legend` missing flex display causing 0 px gap).
  - Cleaned dead CSS declarations on `.keyboard`.
  - Added `:focus-visible` styling across all interactive elements for accessible keyboard navigation.
  - Added subtle hover highlights to hero action buttons (light and ghost variants).
  - Fixed duplicate lead description sentence in Vietnamese localization.

## [0.4.7] - 2026-11-02

### Fixed
- **A short guide window could not scroll to the end of a page.** The window
  grid sized its only row from the content, so with a shrunk window the row
  grew past the window and the clipped bottom (including several hundred
  pixels of the Keys and Settings pages) was unreachable: the app grid row is
  pinned now (`minmax(0, 1fr)`), the page list scrolls its full height and the
  bottom padding is always visible.

## [0.4.6] - 2026-11-02

### Changed
- The three sidebar buttons (status, Shutdown, Restart) are now one tight
  group with 6 px between buttons instead of the previous 26-28 px spread:
  the sidebar's 18 px gaps now only separate the brand, the action group and
  the page list, and the same holds in the narrow icon rail.

## [0.4.5] - 2026-11-02

Full code audit; fixes ordered by impact.

### Fixed
- **Auto-off killed the mode at once.** With an auto-off timeout set,
  activating mouse mode after the app had been idle longer than that timeout
  turned the mode off again on the very first poll, because the idle clock was
  stamped when the app started, not when the mode started. It now restarts at
  every activation (tap or hold).
- **The app could vanish after an update.** The install helper that swaps the
  exe on exit did not check whether the install folder is writable, and if the
  swap failed the app was simply never started again. The folder is now probed
  before the app exits, and a failed swap still starts the old exe, so
  "Install" never leaves the taskbar without a tray icon.
- **Split-restart helper.** The restart helper now logs when it fails to start
  the app again (see `%TEMP%\KeyboardMouse\restart.log`) instead of quitting
  silently, and `Restart` returns false if the helper never started.
- **Chords broken with right-side Shift/Alt.** When a chord (Ctrl+key, Alt+key)
  was sent while the user held the *right* Shift or Alt, the batch released the
  *left* scan codes instead: the chord arrived as Ctrl+Shift+C or Alt+C, and
  the right-sided modifier stayed stuck afterwards. Each half (left/right
  Shift, left/right Alt) is now released and restored on its own scan code,
  with Right Alt correctly marked extended.
- **The guide window could open empty after a restart.** When the app restarted
  fast, the WebView2 user-data folder was still held by the closing instance
  and initialization failed once into the "runtime missing" fallback. The
  guide now retries once before falling back.
- **Overlay rendering inside the keyboard hook.** Opening the grid drew a
  full-monitor bitmap layout synchronously in the hook callback, risking the
  low-level hook timeout (Windows then silently unhooks the keyboard). The
  grid overlay is shown through the UI dispatcher now, like the hint layer.
- **Monitor-change handler ran on the SystemEvents worker thread.** It touched
  tray UI and overlays directly; it is marshalled to the UI thread now, like
  the session-lock and suspend handlers.
- **Colour and border changes did not apply live.** Changing cursor colours or
  the screen-border style/thickness on the guide page now re-paints the
  indicator and border immediately instead of waiting for an app restart.
- **Settings could be lost twice.** `settings.json` is now written atomically
  (temp file + replace), so a crash mid-write can no longer truncate it, and a
  file that cannot be parsed is copied to `settings.json.corrupt-<time>`
  before defaults take over, so nothing is silently destroyed.
- **Stuck buttons after failed input injection.** If `SendInput` fails (only
  possible under heavy system pressure), a pressed button no longer stays
  marked as held for an orphan button-up later, a click typed while that very
  button is held no longer breaks a drag, and the failure is logged in the log
  file instead of `Debug.WriteLine`.
- **Keyboard-hook error handler deferred.** An exception inside a key callback
  used to write to the log file synchronously on the hook call stack; the
  write is posted to the UI thread now, so logging cannot throttle keystrokes.
- **Update checks more honest.** A timeout now reports "could not reach
  GitHub" instead of "cancelled"; the last-check stamp is only recorded when a
  check actually succeeds (offline starts can retry), a failed or cancelled
  download no longer leaves a partial zip in the staging folder, and version
  comparison no longer ignores the revision segment (0.4.0.1 > 0.4.0).
- **Duplicate engine instance removed.** A leftover second `MovementEngine`
  allocation (merge artefact) started an extra background thread and leaked a
  waitable timer plus a high-resolution timer period for the app's lifetime.
- **Marks writer shutdown race.** Disposing the marks store disposed its wake
  event while the writer thread could still wait on it; the event is now
  disposed only after the writer has really stopped (the movement engine gets
  the same treatment and guards its wake calls).
- **Stale hint windows released.** Hint overlay windows keep a monitor-sized
  bitmap each; the ones for monitors that disappeared or changed resolution
  are disposed instead of living hidden forever.
- **Guide origin guard hardened.** Navigation is allowed only for the exact
  scheme + host + port of the private origin (a prefix match would have
  admitted `app.keyboardmouse.invalid.evil.com`).
- **Paused pass-through bookkeeping.** A key-up arriving while the app is
  paused now clears the swallowed-key bookkeeping instead of keeping the entry
  forever.

## [0.4.4] - 2026-11-02

### Added
- A **Shutdown** button directly above the sidebar's Restart app button. It
  stops the app completely (the same action as About → Shutdown and the tray
  Exit item), so both app controls are visible on every guide page. Under
  narrow windows it collapses to the icon only, like Restart does.

## [0.4.3] - 2026-11-02

### Fixed
- Every **Restart** button now uses a circular-arrow icon of its own
  (`#i-restart`, a nearly closed ring with a corner arrowhead) instead of the
  refresh glyph on the About tab, which read as a power button at small sizes,
  and instead of the power glyph on the two Settings buttons.

### Changed
- The guide page is served with `Cache-Control: no-cache`, so an app update can
  no longer be masked by an old cached page in the WebView2 user-data folder.

## [0.4.2] - 2026-11-02

### Fixed
- The comet trail now works: samples are recorded along the head's path (not
  the pointer's), carry an age and are smoothed with two rounds of Chaikin
  subdivision before being stroked into one tapered tail. It reaches about
  `5 x size + 24` pixels, keeps samples for 0.9 s, and dissolves when the
  pointer stops instead of freezing on screen.

- The screen frame no longer drops segments: the side strips run the full
  height (so corners are drawn twice, and the Glow band no longer stops short of
  the corner), and every overlay re-asserts its topmost place when it is shown.
- The frame is configurable: **Frame** (clean line with a lighter inner edge),
  **Glow** (soft gradient band) or **Corners** (four corner marks), with its own
  colour and a 2-12 px thickness (`feedback.borderStyle`, `borderColor`,
  `borderThickness`).

- **New icon**: the app icon is now a single clear metaphor, a white keyboard
  key with the pointer cut into it. The "K" letter and the heavy outline are
  gone, the tray icons lost their outline and use the app's indigo/slate
  palette, and every size is drawn for its slot (16 and 20 px drop the key and
  show just the pointer) instead of being scaled down.

- **Restart app** now sits in the sidebar, so it is in view on every page
  instead of buried in Settings → Advanced, and a reminder bar appears after
  every setting change asking for a restart.

- **No frame**: the border look picker gained a **None** option, so the frame
  can be switched off from there instead of only from the switch above it.

- The **Arrow** indicator sits further from the pointer and **Classic dot**
  sits closer, so each one reads clearly next to the system cursor instead of
  touching it or floating away.

- **Restart KeyboardMouse** in the tray menu and **Restart app** in Settings: a
  helper waits for the app to exit, then starts it again, so the handful of
  changes that only apply on a fresh start no longer need a manual close and
  reopen.

- Settings controls no longer apply eagerly: moving a slider, opening the colour
  picker or nudging a value now only edits a draft (shown in the value chip and
  an **Apply** bar), and the app changes when that field loses focus, Enter is
  pressed, or **Apply** is used.

### Changed
- Halo, spotlight and reticle are drawn around the pointer again (only the dot,
  comet and orbit sit beside it), and the halo, spotlight, reticle, arrow and
  comet are redrawn to suit: no rotating arc, no hard inner ring, a solid
  crosshair and a slim dart.
- The shapes that sit beside the pointer (dot, comet, orbit) sit a small,
  even gap below-right of it (15 x 17 px), and the arrow is now the familiar
  upright pointer, drawn further out instead of a tilted wedge.
- The buddy face is gone: that slot is now **Brackets**, four corner marks
  around the pointer. Existing `feedback.cursorStyle: "buddy"` settings load as
  `brackets`.
- The indicator's colours are now yours to pick: **Pointer colour** plus a
  **State colours** row for scroll, precision, turbo, drag lock and auto click
  (`feedback.cursorColor`, `scrollColor`, `precisionColor`, `turboColor`,
  `dragLockColor`, `autoClickColor`). The legend in the window and the live
  pointer both follow your choice.

## [0.4.1] - 2026-11-02

### Added
- **Shutdown** and **Restart** buttons in the About tab. Restart closes the
  app and starts the exe again (the guide reopens); a tiny PowerShell helper
  waits for this process to end first so the single-instance mutex is free.

### Fixed
- The scroll-layer demo in the guide drew its fake document lines outside the
  screen frame, past the border onto the card background. The demo now clips
  the lines inside the frame.

## [0.4.0] - 2026-11-02

### Added
- Eight pointer indicator styles, so mouse mode no longer has to be a plain
  dot: **Classic dot**, **Halo ring**, **Spotlight**, **Reticle**, **Arrow**,
  **Comet trail**, **Orbit** and **Buddy** (a small face that blinks and bobs).
  Pick one in **Settings → Pointer style** or from the tray menu; each tile in
  the window previews the real design. Colour continues to show the active
  layer, auto click and drag lock.
- **Pointer size** slider in Settings, backed by `feedback.dotSize`.
- `feedback.cursorStyle` in `settings.json` (`dot`, `halo`, `spotlight`,
  `reticle`, `arrow`, `comet`, `orbit`, `buddy`); unknown values fall back to
  `dot`.
- Opt-in updates from GitHub Releases, off by default. **Check for updates**
  in the tray menu or Settings asks GitHub for the latest stable release, and
  **Check for updates automatically** repeats the check once a day, silently.
  When a newer version exists it asks first, downloads the zip, verifies the
  published SHA-256, then closes, swaps the exe and starts again.
  `update.checkOnStart` in `settings.json` controls the automatic check;
  settings, marks and autostart are untouched by an update.
- The release workflow now publishes `<zip>.sha256` next to the zip; without it
  the updater refuses to install anything.

### Changed
- The guide's "What the dot means" section is now "What the colours mean",
  since the indicator is not always a dot.
- `PRIVACY.md`, the About tab and the README no longer claim the app never
  touches the network: they now say it stays offline unless you enable update
  checks, and that a check sends nothing about the user.

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
