# KeyboardMouse

Control the mouse entirely from the keyboard on Windows, with both hands on
the home row and the right hand on **I J K L**.

Every mouse feature works from the keyboard: move, click, drag, drag lock,
scroll (vertical, horizontal, zoom), back/forward, double click, modifier
clicks and hover. It's fastest over long distances, thanks to a 3×3 grid jump,
hint labels on every clickable element, window/monitor jumps and saved
position marks.

It works **alongside** a TrackPoint, touchpad or real mouse. Physical pointer
movement is never blocked, and the program always re-syncs to wherever the
pointer actually is.

> 🇻🇳 **Tiếng Việt:** xem [Hướng dẫn tiếng Việt](#hướng-dẫn-tiếng-việt) ở cuối trang.

**Requirements:** Windows 10 (1803+) or Windows 11, 64-bit. Nothing else is
needed, because .NET is bundled inside the exe.

---

## 1. Download

1. Open the [**Releases page**](https://github.com/khoa-nhd/mouse-keyboard/releases/latest).
2. Under **Assets**, click `KeyboardMouse-vX.Y.Z-win-x64.zip` to download it.
3. Right-click the downloaded zip → **Extract All…** → **Extract**.

The zip contains one portable program, `KeyboardMouse.exe`. There's no
installer, and nothing is written to Program Files.

## 2. Put it in a permanent folder

Move `KeyboardMouse.exe` to a folder you won't delete, for example
`C:\Tools\KeyboardMouse\`.

This matters for **Start with Windows** (step 5): Windows remembers the
exact path of the exe. If you later move or delete the file, autostart
stops working.

## 3. Run it

Double-click `KeyboardMouse.exe` (the blue icon with the K key and pointer).

The **KeyboardMouse window** opens with an illustrated guide:

| Tab | What's in it |
|---|---|
| **Start** | Turn mouse mode on, a 30-second quick start, and one-click setup: Start with Windows, Start menu entry, desktop shortcut |
| **Keys** | An interactive keyboard. Hover a key to see what it does; in mouse mode the keys you press light up |
| **Power moves** | Animated demos of grid jump, hints, scrolling and the auto clicker |
| **Practice** | A target range to practise clicking without the mouse, with your best time |
| **Settings** | Auto-click speed and button, pointer dot, screen border, copy/paste/undo/redo keys |
| **About** | Version, privacy, links, and Quit |

The guide is available in **English and Tiếng Việt** (switch at the bottom left).

- **Closing the window doesn't stop the app.** It keeps running in the
  **system tray** (bottom-right, next to the clock). Double-click the tray icon,
  or run the exe again, to reopen the window.
- **Can't see the tray icon?** Click the **^** arrow next to the clock to show
  hidden icons. To keep it always visible, drag it onto the taskbar, or go to
  **Settings → Personalization → Taskbar → Other system tray icons** and turn
  **KeyboardMouse** on.
- The window uses the Microsoft Edge **WebView2 Runtime**, which is built into
  Windows 11 and most Windows 10 PCs. If it's missing, the app still works and the
  window shows a download link.

### "Windows protected your PC"

The exe isn't code-signed, so SmartScreen may warn you the first time.
Click **More info → Run anyway**. You only see this once.

All source code is in this repository. The app never connects to the network
and never records keystrokes (see [PRIVACY.md](PRIVACY.md)).

## 4. First 60 seconds

1. **Tap Caps Lock** once. A small **green dot** appears next to the
   pointer, which means mouse mode is on.
2. Press **I J K L** to move (up / left / down / right). Hold a key to speed up.
3. Press **Space** to left-click, and **F** to right-click.
4. Hold **D** and press **I / K** to scroll.
5. Press **G**, then one of **U I O / J K L / M , .** to jump to that part
   of the screen.
6. **Tap Caps Lock** again (or press **Esc**) to go back to normal typing.

Want to practise? Open the **Practice** tab in the window and hit 10 targets
without touching the mouse.

While mouse mode is on, letters don't type. Watch for the green dot. Use
**T** to click a text box *and* leave mouse mode in one key, so you can
type right away.

## 5. Start automatically with Windows

In the window's **Start** tab, turn on **Start with Windows**. The same tab
has **Add to Start menu** and **Desktop shortcut** buttons.
You can also right-click the tray icon → **Start with Windows** (a ✓ appears).

At sign-in the app starts silently in the tray, without opening the window.

## 6. Tray menu

| Item | What it does |
|---|---|
| **Open KeyboardMouse** | Open the guide window (same as double-clicking the icon) |
| Mouse mode (Caps Lock) | Turn mouse mode on/off with the mouse |
| Pause | Ignore all hotkeys until you un-pause |
| Show keymap | Open the guide on the Keys tab |
| Open settings file | Open `settings.json` in Notepad |
| Reload settings | Apply changes made to `settings.json` |
| Check for updates | Ask GitHub for a newer version (see below) |
| Start with Windows | Toggle autostart |
| Open log folder | Open the folder with `log.txt` (for bug reports) |
| Restart KeyboardMouse | Close and start the app again (same exe, same settings) |
| Exit | Quit the app; the keyboard goes back to normal immediately |

## 7. Update to a new version

### In the app (opt-in)

Right-click the tray icon, then **Check for updates**, or use **Settings ->
Check for updates** in the window. If a newer version exists, KeyboardMouse
asks before downloading anything, verifies the SHA-256 published with the
release, then closes, swaps the exe and starts again. Settings, marks and
autostart are kept.

To have it check by itself (once a day, silently), turn on **Settings -> Check
for updates automatically**, which is the same as:

```json
{ "update": { "checkOnStart": true } }
```

Update checks are the only network access the app has, and they are **off by
default**. Each check requests one public GitHub file and sends nothing about
you; see [PRIVACY.md](PRIVACY.md).

### By hand

1. Right-click the tray icon, then **Exit** (or **About -> Quit** in the window).
2. Download the new zip from [Releases](https://github.com/khoa-nhd/mouse-keyboard/releases/latest).
3. Replace the old `KeyboardMouse.exe` in your folder with the new one.
4. Run it again. Your settings, marks and autostart are kept.

## 8. Uninstall

1. Tray icon → untick **Start with Windows**.
2. Tray icon → **Exit**.
3. Delete `KeyboardMouse.exe`.
4. Optional: delete `%APPDATA%\KeyboardMouse` (settings) and
   `%LOCALAPPDATA%\KeyboardMouse` (log and marks).

---

## The keymap

All keys are matched by **physical position**, so the map is the same on QWERTY,
AZERTY and QWERTZ keyboards.

```
 Left hand                              Right hand
┌────┬────┬────┬────┬────┐   ┌────┬────┬────┬────┬────┐
│ Q  │ W  │ E  │ R  │ T  │   │ Y  │ U  │ I  │ O  │ P  │
│    │    │Mid │Dbl │Clk+│   │Copy│Back│ ▲  │Fwd │Pste│
│    │    │clk │clk │exit│   │    │    │    │    │    │
├────┼────┼────┼────┼────┤   ├────┼────┼────┼────┼────┤
│ A  │ S  │ D  │ F  │ G  │   │ H  │ J  │ K  │ L  │ ;  │
│Trbo│Prec│Scrl│Rght│Grid│   │Hint│ ◄  │ ▼  │ ►  │    │
│hold│hold│hold│clk │    │   │    │    │    │    │    │
├────┼────┼────┼────┼────┤   ├────┼────┼────┼────┼────┤
│ Z  │ X  │ C  │ V  │ B  │   │ N  │ M  │ ,  │ .  │ /  │
│Undo│Scrn│Win │Drag│Auto│   │Next│Mark│    │    │    │
│    │ctr │ctr │lock│clik│   │mon │    │    │    │    │
└────┴────┴────┴────┴────┘   └────┴────┴────┴────┴────┘
          Space = left click (hold = drag)      ' = jump to mark
```

### Turn on / off

| Key | Action |
|---|---|
| Tap **Caps Lock** | Toggle mouse mode on/off |
| Hold **Caps Lock** + other keys | Mouse mode only while held |
| **Shift + Caps Lock** | Real Caps Lock |
| **Esc** | Close an overlay, or leave mouse mode |
| **Ctrl + Alt + Backspace** | Panic: release all buttons and leave mouse mode |

### Move and click

| Key | Action |
|---|---|
| **I / J / K / L** | Move up / left / down / right (press two for diagonals) |
| hold **A** | Turbo speed |
| hold **S** | Precision speed (1 px per tap) |
| hold **D** | Scroll layer (see below) |
| **Space** | Left click. Hold Space and move to drag. |
| **F** | Right click |
| **E** | Middle click |
| **R** | Double click |
| **U / O** | Mouse back / forward |
| **V** | Drag lock: left button stays down until V or Space |
| **T** | Left click, then leave mouse mode |
| **B** | **Auto clicker** on/off (see below) |
| **Y / P / Z / W** | Copy / paste / undo / redo (Ctrl+C / Ctrl+V / Ctrl+Z / Ctrl+Y) |

Shift, Ctrl and Alt pass through, so `Shift+Space`, `Ctrl+Space` and so on work
like modifier clicks on a real mouse. Keys that aren't mapped (Enter, Tab,
Backspace, digits, arrows, F-keys) keep working normally.

### Auto clicker (B)

Press **B** to start clicking automatically wherever the pointer is. The dot
next to the pointer turns **yellow**. Press **B** again to stop.

- Default: left click, 10 times per second (every 100 ms).
- You can keep moving with I J K L (or the TrackPoint) while it clicks, and
  each click lands at the new position.
- It stops by itself when you leave mouse mode (Caps Lock / Esc), on panic
  (Ctrl + Alt + Backspace), and when the PC locks or sleeps.
- Change the speed or button in `settings.json`:

```json
"autoClick": { "intervalMs": 100, "button": "Left" }
```

`intervalMs` can be 10–60000 (lower = faster). `button` can be `Left`, `Right`
or `Middle`. To use a different key, set `keys.autoClick`.

### Scroll layer (hold D)

| Key | Action |
|---|---|
| I / K | Scroll up / down |
| J / L | Scroll left / right |
| U / O | Page Up / Page Down |
| + hold S | Slow, precise scrolling |
| + Ctrl | Zoom (Ctrl + wheel) |

### Grid mode (G)

```
U I O
J K L      each press jumps into that cell, and the cell becomes the
M , .      new grid, so any point on screen is 2–3 keys away
```

Space = click here and close · F = right click here and close · G = close ·
Backspace = up one level · N = next monitor · Esc = cancel and restore the pointer.

### Hint mode (H)

Labels appear on every clickable element of the foreground window (and the
taskbar). Type a label's letters (`a s d f j k l ;`) to click that element.
Shift + label hovers, F + label right-clicks, Backspace deletes a letter, Esc cancels.

Chromium-based apps (Chrome, Edge, VS Code) build their accessibility tree
lazily, so the first scan can be empty. Press H again, or start the browser
with `--force-renderer-accessibility`.

### Jumps and marks

| Key | Action |
|---|---|
| **X** | Center of the current screen |
| **C** | Center of the foreground window |
| **N** | Center of the next monitor |
| **M**, then a–z | Save the pointer position under that letter |
| **'**, then a–z | Jump to a saved position |

---

## Settings

On first run, a default `settings.json` is created at
`%APPDATA%\KeyboardMouse\settings.json`. For a portable setup, put
`settings.json` next to the exe instead.

The common options (auto-click speed and button, pointer style and size,
screen border, copy/paste/undo/redo keys) can be changed in the window's **Settings**
tab. The pointer style is also in the tray menu under **Pointer style**.
Everything else is in the file.

Tray icon → **Open settings file**, edit it, save, then tray icon →
**Reload settings**. No restart is needed.

```json
{
  "keys": {
    "up": "I", "left": "J", "down": "K", "right": "L",
    "leftClick": "Space", "rightClick": "F", "middleClick": "E",
    "doubleClick": "R", "clickAndExit": "T", "dragLock": "V",
    "back": "U", "forward": "O",
    "scrollLayer": "D", "precisionLayer": "S", "turboLayer": "A",
    "grid": "G", "hint": "H",
    "centerScreen": "X", "centerWindow": "C", "nextMonitor": "N",
    "markSave": "M", "markJump": "Apostrophe",
    "copy": "Y", "paste": "P", "undo": "Z", "redo": "W",
    "autoClick": "B"
  },
  "autoClick": { "intervalMs": 100, "button": "Left" },
  ...
}
```

- Every key can be remapped. A key can't be bound twice; the first binding wins.
- The same file holds speeds, scroll feel, the pointer indicator
  (`feedback.cursorStyle` = `dot`, `halo`, `spotlight`, `reticle`, `arrow`,
  `comet`, `orbit` or `brackets`, and `feedback.dotSize`), the tap timeout,
  auto-off and grid depth.
- To use a different toggle key, set `activation.toggleKey` (e.g. `"ScrollLock"`).
- Key names are physical names (`I`, `Space`, `Apostrophe`, `Comma`, `PageUp`, …).
  The symbols `;` `,` `.` `'` `[` `]` `-` `=` `/` `\` also work.

## Troubleshooting

| Problem | Fix |
|---|---|
| Nothing happens when I tap Caps Lock | Check the app is running (tray icon, or Task Manager → *KeyboardMouse*). Make sure it isn't **Paused** in the tray menu. |
| Letters don't type | Mouse mode is on (green indicator). Tap Caps Lock or press Esc. |
| Doesn't work in Task Manager or an admin window | Windows blocks normal apps from controlling admin windows. Run KeyboardMouse as administrator if you need that. |
| Doesn't work in a game | Many games ignore simulated input. This isn't supported. |
| Hints show nothing in Chrome / VS Code | Press H again (see Hint mode above). |
| A mouse button seems stuck | Press **Ctrl + Alt + Backspace**. |
| Antivirus flags the exe | Keyboard hooks look like keyloggers to some antivirus tools. The code is open, so check it, then add an exclusion. |
| Something else | Tray → **Open log folder** and attach `log.txt` to a [new issue](https://github.com/khoa-nhd/mouse-keyboard/issues). |

---

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(`winget install Microsoft.DotNet.SDK.8`).

```powershell
git clone https://github.com/khoa-nhd/mouse-keyboard.git
cd mouse-keyboard
dotnet build KeyboardMouse.sln -c Release
dotnet test tests/KeyboardMouse.Tests -c Release
dotnet run --project src/KeyboardMouse -c Release
```

Build the single portable exe (it lands in `publish\KeyboardMouse.exe`):

```powershell
dotnet publish src/KeyboardMouse -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

`dotnet publish` only **builds** the exe. Run `publish\KeyboardMouse.exe`
afterwards to start the app.

Releases are built automatically by GitHub Actions
([`.github/workflows/release.yml`](.github/workflows/release.yml)) when a
`v*` tag is pushed:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

### Project layout

- `PLAN.md`: the full design document (keymap, architecture, settings schema, phases, risks)
- `src/KeyboardMouse/`: the app (keyboard hook, input router, movement engine,
  controllers per mode, overlays, tray app)
- `src/KeyboardMouse/Ui/`: the guide window (`guide.html` shown in WebView2 by `GuideWindow.cs`)
- `tools/make-icons.ps1`: generates the app and tray icons
- `tests/KeyboardMouse.Tests/`: xUnit tests for the pure logic

---

## Hướng dẫn tiếng Việt

**Yêu cầu:** Windows 10 (bản 1803 trở lên) hoặc Windows 11, 64-bit. Không cần cài thêm gì.

### Tải và cài
1. Vào [**trang Releases**](https://github.com/khoa-nhd/mouse-keyboard/releases/latest).
2. Ở mục **Assets**, bấm vào `KeyboardMouse-vX.Y.Z-win-x64.zip` để tải về.
3. Chuột phải vào file zip → **Extract All…** → **Extract**.
4. Chép `KeyboardMouse.exe` vào một thư mục cố định, ví dụ `C:\Tools\KeyboardMouse\`.
   Đừng để trong Downloads rồi xóa, vì tính năng tự khởi động cần đúng đường dẫn này.
5. Nhấp đúp `KeyboardMouse.exe` để chạy (icon màu xanh có phím K và con trỏ).
   - Nếu hiện **"Windows protected your PC"**, bấm **More info → Run anyway**.
   - **Cửa sổ KeyboardMouse** mở ra với hướng dẫn có hình minh họa, gồm các tab:
     **Bắt đầu**, **Phím** (bàn phím tương tác, phím sáng lên khi bạn bấm),
     **Tuyệt chiêu** (minh họa động), **Luyện tập** (bắn mục tiêu), **Cài đặt**, **Giới thiệu**.
     Đổi ngôn ngữ English / Tiếng Việt ở góc dưới bên trái.
   - **Đóng cửa sổ không tắt app.** App vẫn chạy ở khay hệ thống (góc dưới bên phải, cạnh đồng hồ).
     Nhấp đúp vào icon ở khay, hoặc mở lại file exe, để mở lại cửa sổ.
     Không thấy icon thì bấm mũi tên **^** cạnh đồng hồ.
6. **Tự chạy khi bật máy:** trong tab **Bắt đầu**, bật **Khởi động cùng Windows**.
   Ở đó cũng có nút **Thêm vào Start menu** và **Shortcut ngoài desktop**.

### Cách dùng
1. **Bấm nhả Caps Lock** một lần → cạnh con trỏ hiện **chấm xanh lá**, nghĩa là chế độ chuột đã bật.
2. **I J K L** để di chuột (lên / trái / xuống / phải). Giữ phím thì chuột chạy nhanh dần.
3. **Space** = click trái (giữ Space rồi di = kéo thả). **F** = click phải. **R** = double click.
4. Giữ **D** + **I / K** = cuộn lên / xuống. Giữ **D** + **J / L** = cuộn ngang.
5. Giữ **S** = di chậm, chính xác. Giữ **A** = di rất nhanh.
6. **G** rồi bấm **U I O / J K L / M , .** = nhảy nhanh tới vùng đó trên màn hình.
7. **H** = hiện nhãn chữ trên các nút bấm được. Gõ nhãn để click vào nút đó.
8. **T** = click rồi tắt chế độ chuột luôn, tiện khi bấm vào ô nhập chữ để gõ.
   **B** = bật/tắt **auto click**: tự click liên tục tại vị trí con trỏ (mặc định 10 lần/giây, chấm cạnh con trỏ chuyển màu vàng).
   Tự dừng khi tắt chế độ chuột. Đổi tốc độ trong `settings.json` → `"autoClick": { "intervalMs": 100, "button": "Left" }`.
9. **Bấm Caps Lock** lần nữa hoặc **Esc** để quay lại gõ chữ bình thường.
   Cần Caps Lock thật thì bấm **Shift + Caps Lock**.

Khi chế độ chuột đang bật, các phím chữ không gõ ra chữ, nên hãy để ý chấm xanh cạnh con trỏ.
Nếu chuột bị "kẹt" nút: **Ctrl + Alt + Backspace**.

### Cập nhật / gỡ
- **Cập nhật:** chuột phải icon → **Exit**, tải bản mới, chép đè `KeyboardMouse.exe`, chạy lại.
- **Gỡ:** bỏ dấu ✓ ở **Start with Windows** → **Exit** → xóa `KeyboardMouse.exe`.
  Muốn xóa sạch thì xóa thêm thư mục `%APPDATA%\KeyboardMouse` và `%LOCALAPPDATA%\KeyboardMouse`.

---

## License

[MIT](LICENSE) © 2026 khoa-nhd
