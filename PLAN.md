# KeyboardMouse — Implementation Plan

Control the mouse entirely from the keyboard on Windows, with both hands on the home row.
Right hand moves the pointer with **I J K L**, left hand holds layers and clicks, thumbs press **Space**.

Works alongside a TrackPoint / touchpad / real mouse: physical pointer movement is never blocked,
and the program re-syncs to wherever the pointer actually is.

---

## 1. Goals and non-goals

**Goals**
- Every mouse feature available from the keyboard: move, left/right/middle click, double/triple click,
  drag, drag lock, vertical + horizontal scroll, back/forward buttons, modifier clicks, hover.
- Fast long-distance travel: grid jump, hint labels, monitor/window/screen jumps, saved marks.
- Ergonomic: no arrow keys, no stretching, strongest fingers for the most frequent actions.
- Zero typing latency, no stuck keys or buttons, ever.
- Single portable `.exe`, optional autostart, fully configurable via `settings.json`.

**Non-goals (v1)**
- Games / anti-cheat protected apps (they may ignore injected input).
- macOS / Linux.
- Controlling elevated (admin) windows unless the user opts in to run elevated.

---

## 2. Tech stack

| Item | Choice | Why |
|---|---|---|
| Language | C# 12 | Easy Win32 interop, fast enough, good tooling |
| Runtime | .NET 8 (LTS) | Single-file self-contained publish |
| UI | WinForms | Tray icon, borderless overlay windows, minimal overhead |
| Win32 interop | Hand-written P/Invoke in `Native/` | Small surface, no extra dependency |
| UI Automation (hints) | FlaUI.UIA3 (NuGet) | UIA3 COM wrapper, much faster than `System.Windows.Automation` |
| Config | System.Text.Json | Built in |
| Tests | xUnit | Unit tests for all pure logic |
| Build / release | `dotnet publish` + GitHub Actions | Reproducible single-file builds |

Prerequisite: `winget install Microsoft.DotNet.SDK.8`

---

## 3. Final keymap

All keys are matched by **physical scan code**, so the layout is identical on QWERTY, AZERTY, QWERTZ.

### 3.1 Activation (always active)

| Key | Action |
|---|---|
| Tap **Caps Lock** (< 200 ms, no other key pressed meanwhile) | Toggle mouse mode on/off |
| Hold **Caps Lock** + other keys | Momentary mouse mode, off again on release |
| **Shift + Caps Lock** | Real Caps Lock (passed through) |
| **Esc** (in mouse mode) | Close overlay / cancel pending, else leave mouse mode |
| **Ctrl + Alt + Backspace** | Panic: release all buttons, leave mouse mode, reset state |

### 3.2 Mouse mode — normal layer

```
 Left hand                              Right hand
┌────┬────┬────┬────┬────┐   ┌────┬────┬────┬────┬────┐
│ Q  │ W  │ E  │ R  │ T  │   │ Y  │ U  │ I  │ O  │ P  │
│ -  │ -  │Mid │Dbl │Clk+│   │Copy│Back│ ▲  │Fwd │Pste│
│    │    │clk │clk │exit│   │    │    │    │    │    │
├────┼────┼────┼────┼────┤   ├────┼────┼────┼────┼────┤
│ A  │ S  │ D  │ F  │ G  │   │ H  │ J  │ K  │ L  │ ;  │
│Trbo│Prec│Scrl│Rght│Grid│   │Hint│ ◄  │ ▼  │ ►  │ -  │
│hold│hold│hold│clk │    │   │    │    │    │    │    │
├────┼────┼────┼────┼────┤   ├────┼────┼────┼────┼────┤
│ Z  │ X  │ C  │ V  │ B  │   │ N  │ M  │ ,  │ .  │ /  │
│Undo│Scrn│Win │Drag│ -  │   │Next│Mark│ -  │ -  │ -  │
│    │ctr │ctr │lock│    │   │mon │save│    │    │    │
└────┴────┴────┴────┴────┘   └────┴────┴────┴────┴────┘
                Space = left click (hold = drag)        ' = jump to mark
```

| Key | Action |
|---|---|
| I / J / K / L | Move up / left / down / right (combine for diagonals) |
| Hold S | Precision speed profile |
| Hold A | Turbo speed profile |
| Hold D | Scroll layer (see 3.3) |
| Space | Left button: down on press, up on release (hold + move = drag) |
| F | Right button (down/up follows the key) |
| E | Middle click |
| R | Double click |
| T | Left click, then leave mouse mode (click into a text box and type) |
| V | Drag lock toggle (left button stays down until V or Space) |
| U / O | Mouse button 4 (back) / button 5 (forward) |
| G | Grid mode |
| H | Hint mode |
| X | Jump to center of current monitor |
| C | Jump to center of foreground window |
| N | Jump to center of next monitor |
| M + letter | Save current position as mark |
| ' + letter | Jump to saved mark |
| Y / P / Z | Send Ctrl+C / Ctrl+V / Ctrl+Z (optional, `editingExtras`) |
| Shift / Ctrl / Alt | Passed through, so clicks and scrolls become modifier clicks |
| Everything else | Passed through (Enter, Tab, Backspace, digits, F-keys, arrows, Win combos) |

Unused in mouse mode: Q, W, B, `,`, `.`, `/`, `;` (reserved for future features).

### 3.3 Scroll layer (hold D)

| Key | Action |
|---|---|
| I / K | Scroll up / down (accelerating, smooth) |
| J / L | Scroll left / right |
| U / O | Send Page Up / Page Down |
| + hold S | Slow precise scroll |
| + Ctrl | Ctrl+wheel (zoom) |

### 3.4 Grid mode (G)

```
 U │ I │ O
───┼───┼───      Each press: pointer jumps to that cell's center,
 J │ K │ L       the cell becomes the new grid (3 levels max,
───┼───┼───      then the grid closes automatically).
 M │ , │ .
```

| Key | Action |
|---|---|
| U I O J K L M , . | Select cell, subdivide |
| Space | Left click here, close |
| F | Right click here, close |
| G | Close, keep position (back to normal movement) |
| Backspace | Go up one level |
| N | Move grid to next monitor |
| Esc | Cancel, restore original pointer position |

### 3.5 Hint mode (H)

- Labels appear on every visible clickable element of the foreground window (plus taskbar, optional).
- Alphabet: `a s d f j k l ;` (home row only). Labels are fixed-length, prefix-free.
- Typing a full label → left click it. Hold **Shift** while finishing → hover only. Hold **F**
  while finishing → right click.
- Backspace removes the last typed letter, Esc cancels.
- Non-matching labels fade out as you type.

---

## 4. Architecture

```
            ┌────────────────────────────────────────────────────┐
 keyboard → │ KeyboardHook (WH_KEYBOARD_LL, UI thread)           │
            │   → KeyEvent { ScanCode, IsDown, IsRepeat, Time }  │
            └──────────────┬─────────────────────────────────────┘
                           ▼
            ┌────────────────────────────────────────────────────┐
            │ InputRouter  (decides: swallow or pass through)    │
            │   ActivationController (Caps tap/hold, panic)      │
            │   → active handler by priority:                    │
            │       HintController > GridController >            │
            │       PendingController (marks) > NormalController │
            └──────┬──────────────┬──────────────┬───────────────┘
                   ▼              ▼              ▼
          MovementEngine     MouseOutput     Overlays (Grid, Hint,
          (own thread,       KeyOutput       CursorIndicator, Border)
           120 Hz)           (SendInput)
                   ▲
          AppState (mode, held layers, drag lock, marks)  ← Config
```

### 4.1 Threads
- **UI thread**: WinForms message loop, keyboard hook callback, overlays, tray.
  The hook callback must return in < 1 ms; it only updates state and posts work.
- **Movement thread**: high-resolution loop, runs only while a movement/scroll key is held,
  sleeps on an `AutoResetEvent` otherwise.
- **Hint scan thread**: UI Automation queries (can be slow / hang) with a timeout.

Shared state lives in `AppState`, guarded by a single `lock`; reads are short snapshots.

### 4.2 Folder layout

```
mouseArrow/
├── PLAN.md
├── README.md
├── LICENSE
├── KeyboardMouse.sln
├── src/KeyboardMouse/
│   ├── KeyboardMouse.csproj
│   ├── app.manifest                 # PerMonitorV2 DPI, asInvoker
│   ├── Program.cs                   # entry, single instance, Application.Run
│   ├── TrayApp.cs                   # ApplicationContext, NotifyIcon, menu
│   ├── Native/
│   │   ├── User32.cs                # SetWindowsHookEx, SendInput, GetCursorPos...
│   │   ├── Dwm.cs                   # DwmGetWindowAttribute
│   │   ├── Kernel32.cs              # GetModuleHandle, high-res timer
│   │   └── Structs.cs               # INPUT, MOUSEINPUT, KEYBDINPUT, KBDLLHOOKSTRUCT
│   ├── Input/
│   │   ├── KeyboardHook.cs
│   │   ├── PhysicalKey.cs           # enum + scan code table
│   │   ├── KeyEvent.cs
│   │   ├── TapHoldDetector.cs
│   │   └── InputRouter.cs
│   ├── Controllers/
│   │   ├── ActivationController.cs
│   │   ├── NormalController.cs
│   │   ├── GridController.cs
│   │   ├── HintController.cs
│   │   └── PendingController.cs     # marks (M / ')
│   ├── Core/
│   │   ├── AppState.cs
│   │   ├── MovementEngine.cs
│   │   ├── SpeedProfile.cs
│   │   ├── Monitors.cs
│   │   ├── Marks.cs
│   │   └── HintLabels.cs            # label generation (pure)
│   ├── Output/
│   │   ├── MouseOutput.cs
│   │   └── KeyOutput.cs
│   ├── Overlays/
│   │   ├── OverlayForm.cs           # base: layered, click-through, no-activate
│   │   ├── CursorIndicator.cs
│   │   ├── ScreenBorder.cs
│   │   ├── GridOverlay.cs
│   │   └── HintOverlay.cs
│   ├── Automation/
│   │   └── ClickableScanner.cs      # FlaUI UIA3
│   ├── Config/
│   │   ├── Settings.cs              # POCO
│   │   ├── SettingsStore.cs         # load/save/reload, defaults
│   │   └── settings.default.json
│   ├── System/
│   │   ├── Autostart.cs             # HKCU Run key
│   │   ├── SessionWatcher.cs        # lock/sleep → mode off
│   │   └── Log.cs                   # never logs keystrokes
│   └── Assets/
│       ├── tray-on.ico
│       └── tray-off.ico
└── tests/KeyboardMouse.Tests/
    ├── TapHoldDetectorTests.cs
    ├── SpeedProfileTests.cs
    ├── GridMathTests.cs
    ├── HintLabelsTests.cs
    ├── RouterTests.cs
    └── SettingsTests.cs
```

---

## 5. Module details

### 5.1 Program / TrayApp
- Named mutex `Local\KeyboardMouse-SingleInstance`; if already held, exit silently
  (optionally signal the running instance to show its tray balloon).
- `app.manifest`: `<dpiAwareness>PerMonitorV2</dpiAwareness>` so all coordinates are physical pixels.
- `TrayApp : ApplicationContext` owns everything; `NotifyIcon` with menu:
  - Mouse mode: On/Off
  - Pause (disable hook handling entirely)
  - Show keymap
  - Settings (opens `settings.json` in Notepad) / Reload settings
  - Start with Windows ✓
  - Open log folder
  - Exit
- On exit: `MouseOutput.ReleaseAll()`, unhook, dispose overlays.

### 5.2 KeyboardHook
- `SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0)`.
- Keep the delegate in a static field (prevents GC → crash).
- Read `KBDLLHOOKSTRUCT`:
  - `flags & LLKHF_INJECTED (0x10)` → **ignore and pass through** (our own `SendInput` keys,
    other tools' keys). Critical, otherwise Y→Ctrl+C would loop.
  - `scanCode` + `LLKHF_EXTENDED` → `PhysicalKey`.
  - `wParam` WM_KEYDOWN/WM_SYSKEYDOWN vs WM_KEYUP/WM_SYSKEYUP.
- Repeat detection: maintain a `HashSet<PhysicalKey>` of keys currently down; a down event
  for a key already down is a repeat.
- Return `1` to swallow, `CallNextHookEx` to pass.
- Windows silently removes hooks whose callback exceeds `LowLevelHooksTimeout`.
  Keep the callback trivial; add a watchdog (every 5 s, verify a heartbeat; reinstall if lost).

### 5.3 PhysicalKey scan code table (set 1)

| Key | Scan | Key | Scan | Key | Scan |
|---|---|---|---|---|---|
| Esc | 0x01 | Q | 0x10 | A | 0x1E |
| W | 0x11 | S | 0x1F | Z | 0x2C |
| E | 0x12 | D | 0x20 | X | 0x2D |
| R | 0x13 | F | 0x21 | C | 0x2E |
| T | 0x14 | G | 0x22 | V | 0x2F |
| Y | 0x15 | H | 0x23 | B | 0x30 |
| U | 0x16 | J | 0x24 | N | 0x31 |
| I | 0x17 | K | 0x25 | M | 0x32 |
| O | 0x18 | L | 0x26 | , | 0x33 |
| P | 0x19 | ; | 0x27 | . | 0x34 |
| ' | 0x28 | Space | 0x39 | Caps | 0x3A |
| Backspace | 0x0E | / | 0x35 | | |

Modifier state comes from the hook itself (track LShift/RShift/LCtrl/RCtrl/LAlt/RAlt down state),
not `GetAsyncKeyState`, so it is consistent with event order.

### 5.4 TapHoldDetector (pure, unit-tested)
Input: key down/up events with timestamps, plus "another key pressed while held".
Output: `Tap`, `HoldStart`, `HoldEnd`.
- Down → start; if another key goes down while held → `HoldStart` immediately (momentary use).
- Up before `tapTimeoutMs` (200) with no other key → `Tap`.
- Up after timeout or after interruption → `HoldEnd`.
- Time is injected (`Func<long> now`) for deterministic tests.

Used for Caps Lock. A, S, D are plain "while held" layers (no tap action), so they need no detector.

### 5.5 InputRouter
```
OnKey(e):
  if paused → pass
  if panic combo → Panic(); swallow
  r = Activation.Handle(e)          // Caps Lock logic, Shift+Caps passthrough
  if r.Handled → return r
  if mode == Off → pass
  for handler in [Hint, Grid, Pending, Normal] where handler.IsActive:
      r = handler.Handle(e)
      if r.Handled → return r
  → pass (unmapped key)
```
- A key whose **down** was swallowed must also have its **up** swallowed, even if the mode
  changed in between. Keep a `swallowedDown` set; on key-up, if in the set → swallow and remove.
  This prevents apps seeing orphan key-ups.
- Keys that were already down **before** mouse mode turned on are passed through on release.

### 5.6 NormalController
- Movement keys: update `AppState.HeldDirections`; on first press call
  `MovementEngine.Nudge(dir)` (instant tap step) and wake the engine. Repeats are swallowed and ignored.
- Layers: A/S/D down/up set `Turbo/Precision/Scroll` flags.
- Space: down → `MouseOutput.LeftDown()` (ignore repeats), up → `LeftUp()`.
  If drag lock is on, Space press → release lock (LeftUp) instead.
- F: right down/up. E: middle click. R: double click. U/O: XButton1/2 click.
- T: left click → mode Off.
- V: toggle drag lock (LeftDown / LeftUp).
- G / H: open Grid / Hint controller.
- X / C / N: jumps via `Monitors`.
- M / ': set Pending mode.
- Y / P / Z: `KeyOutput.SendChord(Ctrl, C|V|Z)` if `editingExtras`.

### 5.7 MovementEngine
State per tick: held direction vector, active profile, time since movement started.

```
dir   = normalize(sum of held directions)       // diagonals not faster
t     = seconds since first direction held
v     = min(profile.max, profile.base + profile.accel * t)   // px/s
pos  += dir * v * dt                            // double precision
send absolute move to round(pos) if it changed
```

- Profiles (defaults): Normal 300 → 2000 px/s, accel 3000; Precision 60 px/s, no accel;
  Turbo 1500 → 5000 px/s, accel 8000. Tap step: normal 5 px, precision 1 px.
- Speeds are scaled by monitor DPI (`dpi / 96`) so feel is the same on 4K.
- Scroll layer uses the same loop: `wheelDelta = v_scroll * dt`, accumulate, send integer deltas
  (smooth scrolling with sub-120 deltas; `settings.scroll.smooth=false` sends whole notches).
- **Re-sync**: each tick read `GetCursorPos`; if it differs from the last position we set by > 2 px,
  the user moved the TrackPoint/mouse → adopt the real position.
- Clamp to the virtual desktop bounds (and to monitor edges so the pointer doesn't vanish in
  gaps between differently sized monitors: use `MonitorFromPoint(MONITOR_DEFAULTTONEAREST)`).
- Timing: dedicated thread using `CreateWaitableTimerExW` with
  `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION` (Win10 1803+) for ~8.3 ms ticks; fallback
  `timeBeginPeriod(1)` + `Thread.Sleep(1)` with `Stopwatch` dt. Idle = blocked on event, 0% CPU.

### 5.8 MouseOutput / KeyOutput
- Everything through `SendInput`.
- Move: `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`,
  coordinates normalized `x * 65535 / (virtualWidth - 1)` relative to the virtual screen origin.
  Absolute moves avoid Windows "Enhance pointer precision" distorting distances.
- Buttons: LEFT/RIGHT/MIDDLE DOWN/UP; XDOWN/XUP with `mouseData = XBUTTON1|XBUTTON2`.
- Wheel: `MOUSEEVENTF_WHEEL` / `MOUSEEVENTF_HWHEEL`, `mouseData = delta` (120 = 1 notch).
- Double click: down, up, down, up in one `SendInput` batch.
- Tracks which buttons it holds; `ReleaseAll()` sends up for each. Called on mode off, panic,
  session lock, exit, and unhandled exceptions.
- KeyOutput: `KEYBDINPUT` with scan codes; chords sent as one batch (Ctrl down, C down, C up, Ctrl up).
  If the user physically holds Shift/Alt at that moment, temporarily release them in the batch
  and restore afterwards.

### 5.9 Monitors
- Enumerate with `EnumDisplayMonitors` + `GetMonitorInfo` (work area + bounds) and
  `GetDpiForMonitor`. Refresh on `SystemEvents.DisplaySettingsChanged`.
- Next monitor: sort by (Left, Top); pick the one after the monitor containing the pointer, wrap.
- Window center: `GetForegroundWindow` → `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`
  (excludes invisible resize borders), fallback `GetWindowRect`.

### 5.10 Overlays (base class)
- `OverlayForm : Form`: `FormBorderStyle.None`, `ShowInTaskbar = false`, `TopMost = true`.
- `CreateParams.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`.
- `ShowWithoutActivation => true` → never steals focus (important: the target app must keep focus).
- Render to a 32-bit bitmap and push with `UpdateLayeredWindow` (per-pixel alpha, smooth edges).
- **CursorIndicator**: 12 px dot offset (+16, +16) from the pointer; follows on each engine tick and
  on a 30 Hz timer while mode is on (to follow TrackPoint motion). Color by state:
  green normal, blue scroll, purple precision, red turbo, orange drag lock.
- **ScreenBorder**: 3 px colored frame on each monitor while mode is on (optional).
- **GridOverlay**: one window covering the current monitor; draws 3×3 lines and the key letter
  in each cell, slight dim outside the active rectangle.
- **HintOverlay**: one window per monitor; draws rounded label boxes at element top-left corners.

### 5.11 GridController
```
open:   rect = current monitor bounds; depth = 0; savedPos = cursor
key k:  cell = cellIndex(k)           // U=0 I=1 O=2 J=3 K=4 L=5 M=6 ,=7 .=8
        rect = subRect(rect, cell)    // pure function, unit-tested
        move cursor to center(rect); depth++
        if depth == maxDepth (3) → close (stay)
Space/F → click, close   G → close   Backspace → pop rect   N → next monitor   Esc → restore, close
```
All other keys are swallowed while the grid is open (except passthrough modifiers).

### 5.12 HintController + ClickableScanner
- On H: start a scan on the hint thread with a 400 ms budget; show a "scanning…" state if slow.
- FlaUI UIA3: `automation.FromHandle(GetForegroundWindow())`, `FindAllDescendants` with a
  `CacheRequest` for `BoundingRectangle`, `ControlType`, `IsOffscreen`, `IsEnabled`.
- Keep elements where:
  - ControlType in { Button, Hyperlink, MenuItem, TabItem, ListItem, TreeItem, CheckBox,
    RadioButton, ComboBox, Edit, SplitButton, DataItem } or InvokePattern available
  - not offscreen, enabled, rect non-empty and intersects a monitor
- Deduplicate overlapping rects (same center within 4 px), cap at 400 elements, sort top→bottom, left→right.
- Optionally include the taskbar (`Shell_TrayWnd`) elements.
- `HintLabels.Generate(n, alphabet)` (pure): length `L = max(1, ceil(log_8 n))`, enumerate in
  base-8 order; prefer labels whose letters alternate hands for speed.
- Typing filters; on full match → move pointer to element center → action (click / hover / right click).
- Chromium/Electron apps expose their accessibility tree only when a UIA client asks; the first
  scan may be slower or empty — retry once after 150 ms. Document `--force-renderer-accessibility`
  as a workaround.
- UIA calls can hang on unresponsive apps: always run with a timeout, never on the UI thread.

### 5.13 Marks
- `Dictionary<char, Point>`, letters a–z. Saved to `%LOCALAPPDATA%\KeyboardMouse\marks.json`.
- Pending state after M / ': next letter completes it, any other key / 2 s timeout cancels.

### 5.14 Activation & safety
- Mode On: show indicator/border, tray icon on. Mode Off: `ReleaseAll()`, clear held keys,
  close overlays, clear drag lock.
- `SystemEvents.SessionSwitch` (lock) and `PowerModeChanged` (suspend) → mode Off.
- Optional auto-off after `autoOffSeconds` of no mouse-mode activity.
- Global unhandled exception handler → `ReleaseAll()`, log, keep running if possible.
- Caps Lock LED / state: the hook swallows Caps Lock so its state never changes; Shift+Caps is
  passed through untouched.

### 5.15 Settings
Location: `settings.json` next to the exe if present (portable mode), else
`%APPDATA%\KeyboardMouse\settings.json` (created from defaults on first run).
Invalid file → log the error, show a tray balloon, keep previous settings.

```json
{
  "activation": { "toggleKey": "CapsLock", "tapTimeoutMs": 200, "autoOffSeconds": 0 },
  "keys": {
    "up": "I", "left": "J", "down": "K", "right": "L",
    "leftClick": "Space", "rightClick": "F", "middleClick": "E", "doubleClick": "R",
    "clickAndExit": "T", "dragLock": "V",
    "back": "U", "forward": "O",
    "scrollLayer": "D", "precisionLayer": "S", "turboLayer": "A",
    "grid": "G", "hint": "H",
    "centerScreen": "X", "centerWindow": "C", "nextMonitor": "N",
    "markSave": "M", "markJump": "Apostrophe",
    "copy": "Y", "paste": "P", "undo": "Z"
  },
  "grid": { "keys": ["U","I","O","J","K","L","M","Comma","Period"], "maxDepth": 3 },
  "hint": { "alphabet": "asdf jkl;", "maxElements": 400, "includeTaskbar": true },
  "speed": {
    "normal":    { "base": 300,  "max": 2000, "accel": 3000, "tapStep": 5 },
    "precision": { "base": 60,   "max": 60,   "accel": 0,    "tapStep": 1 },
    "turbo":     { "base": 1500, "max": 5000, "accel": 8000, "tapStep": 40 }
  },
  "scroll": { "base": 400, "max": 3000, "accel": 4000, "smooth": true },
  "feedback": { "cursorDot": true, "screenBorder": true, "dotSize": 12 },
  "editingExtras": true
}
```
Validation: no key bound twice in the same layer; grid needs exactly 9 keys; hint alphabet ≥ 2 unique chars.

### 5.16 Autostart & logging
- Autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\KeyboardMouse = "<exe path>"`.
- Elevated option (later): Task Scheduler task "at logon, run with highest privileges".
- Log: `%LOCALAPPDATA%\KeyboardMouse\log.txt`, rotate at 1 MB, **never log key contents**.

---

## 6. Build phases

Each phase ends with a working, usable program.

### Phase 0 — Project setup
- `dotnet new sln`, `dotnet new winforms -n KeyboardMouse`, `dotnet new xunit -n KeyboardMouse.Tests`.
- Add `app.manifest` (PerMonitorV2), tray icons, `.gitignore`, `git init`.
- **Done when:** app starts, shows a tray icon, Exit works, second instance exits.

### Phase 1 — Core movement and click
- Native structs + `SetWindowsHookEx`, `SendInput`.
- `PhysicalKey` table, `KeyboardHook`, `InputRouter` with swallowed-down tracking.
- `TapHoldDetector` + Caps Lock tap toggle / hold momentary, Shift+Caps passthrough, Esc.
- `MovementEngine` (fixed speed first, then acceleration), IJKL.
- Space down/up (click + drag), T click-and-exit.
- Panic combo, `ReleaseAll` on mode off / exit.
- **Done when:** you can move, click, drag a file, and type normally with mode off; no stuck keys.

### Phase 2 — All buttons
- F right, E middle, R double, U/O back/forward, V drag lock.
- Modifier passthrough verified (Ctrl+Space opens link in new tab).
- **Done when:** every mouse button works in Explorer and a browser.

### Phase 3 — Layers and feel
- A turbo, S precision, D scroll layer (vertical, horizontal, Page Up/Down, smooth).
- Speed profiles, DPI scaling, diagonal normalization, TrackPoint re-sync.
- High-resolution timer thread.
- **Done when:** movement feels smooth at 120 Hz, 1 px precision works, scrolling a long page is comfortable.

### Phase 4 — Feedback
- `OverlayForm` base, cursor dot with state colors, screen border, tray icon state.
- **Done when:** you always know the mode at a glance; overlays never steal focus or block clicks.

### Phase 5 — Grid mode
- `GridMath` (pure) + tests, `GridOverlay`, `GridController`.
- **Done when:** any point on a 4K screen reachable in ≤ 3 grid keys + fine-tune.

### Phase 6 — Jumps and marks
- `Monitors`, X / C / N, `Marks` with M / ' and persistence.
- **Done when:** jumps land correctly on mixed-DPI multi-monitor setups.

### Phase 7 — Hint mode
- FlaUI, `ClickableScanner` with timeout, `HintLabels` + tests, `HintOverlay`, `HintController`.
- **Done when:** in Explorer, Settings, Chrome, VS Code: H + 2 letters clicks the intended button,
  scan < 400 ms for typical windows.

### Phase 8 — Settings and polish
- `Settings`, `SettingsStore`, validation, reload, remappable keys.
- Editing extras (Y/P/Z), auto-off, session lock handling, autostart toggle, keymap help window.
- **Done when:** every key and speed can be changed in `settings.json` and reloaded live.

### Phase 9 — Distribution
- README (GIF demo, keymap, SmartScreen note), MIT LICENSE, PRIVACY note, CHANGELOG.
- GitHub Actions: on tag `v*` → test → `dotnet publish` single-file self-contained → zip → release.
- Optional: Inno Setup per-user installer, winget manifest, code signing.

Publish command:
```
dotnet publish src/KeyboardMouse -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

---

## 7. Testing

### 7.1 Unit tests (pure logic, no Windows needed)
- TapHoldDetector: tap, hold, hold interrupted by another key, boundary at 200 ms.
- SpeedProfile: velocity at t, clamp to max, diagonal normalization, sub-pixel accumulation.
- GridMath: sub-rectangles for each cell and depth, odd sizes, multi-monitor offsets.
- HintLabels: count, uniqueness, prefix-free, fixed length, alphabet usage.
- InputRouter (with fake outputs): swallowed key-ups match key-downs, mode off releases buttons,
  injected events ignored, unmapped keys pass through.
- Settings: defaults, invalid JSON, duplicate bindings rejected.

### 7.2 Manual checklist
- [ ] Caps tap toggles, Caps hold is momentary, Shift+Caps toggles real Caps Lock
- [ ] Typing in Word/Notepad unaffected when mode is off (no lag, no lost keys)
- [ ] Smooth diagonals, precision 1 px, turbo across screen
- [ ] Single / double / triple click; drag a file; drag-select text; V drag lock
- [ ] Right click menu, then navigate it with arrows + Enter
- [ ] Scroll a long page, horizontal scroll in Excel, Ctrl+scroll zoom
- [ ] Back/forward in browser and Explorer
- [ ] Ctrl+click new tab, Shift+click range select
- [ ] Grid reaches a tiny button; Esc restores position
- [ ] Hints in Explorer, Settings, Chrome, VS Code, taskbar
- [ ] X / C / N jumps on two monitors with different scaling
- [ ] Marks survive restart
- [ ] Esc / panic during drag → no stuck button
- [ ] Win+L while in mode → mode off, nothing stuck after unlock
- [ ] TrackPoint movement while in mode → pointer re-syncs, no jump back
- [ ] CPU ~0% idle, RAM < 60 MB

---

## 8. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Hook silently removed by Windows (slow callback) | Trivial callback, watchdog reinstall |
| Stuck mouse buttons / keys | Track everything we hold; ReleaseAll on every exit path; panic combo |
| Admin windows ignore input (UIPI) | Document; optional elevated autostart via Task Scheduler |
| Antivirus / SmartScreen flags | Open source, no network, no key logging, later code signing |
| UIA slow or hanging | Background thread, timeout, element cap, grid as fallback |
| Mixed DPI coordinate errors | PerMonitorV2 manifest, physical pixels everywhere, test on mixed setups |
| Letters "don't type" confusion | Strong visual feedback, T click-and-exit, Caps hold mode, optional auto-off |
| Conflicts with other remappers (AHK, PowerToys) | Ignore injected events; document load order |

---

## 9. Future ideas
- Hint mode for text positions (click at a word).
- Per-app profiles (different keys in specific apps).
- Settings GUI instead of JSON.
- Mouse recording / repeat last click position.
- Optional sound or haptic-style feedback.
