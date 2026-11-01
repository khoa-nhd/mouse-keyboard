# Privacy

KeyboardMouse is an offline desktop app. There is no telemetry, no analytics,
and no network access of any kind.

- **Keystrokes are never recorded.** The app watches keys through a global
  keyboard hook to decide what to swallow or pass through, but nothing about
  the keys is stored. It never logs key contents.
- **Keystrokes never leave your computer.** The hook data is used in memory
  only.
- **Only its own files are written:** `settings.json`, `marks.json` (saved
  pointer positions, created when you press `M`) and a plain rolling
  `log.txt` with status messages. They live in
  `%APPDATA%\KeyboardMouse` and `%LOCALAPPDATA%\KeyboardMouse` and never
  leave your machine.
- Autostart adds one value to `HKEY_CURRENT_USER\...\Run` so Windows knows to
  start the app at logon. Removing it from the tray menu deletes that value.

The only input there is: your keyboard, for controlling the mouse. The only
output there is: mouse buttons and clicks through the Windows pointer.
