# Privacy

KeyboardMouse is an offline desktop app. There is no telemetry, no analytics,
no account and no server of ours. It only uses the network if you explicitly
turn on update checks (see below), and even then it only talks to GitHub.

- **Keystrokes are never recorded.** The app watches keys through a global
  keyboard hook to decide what to swallow or pass through, but nothing about
  the keys is stored. It never logs key contents.
- **Keystrokes never leave your computer.** The hook data is used in memory
  only.
- **Only its own files are written:** `settings.json`, `marks.json` (saved
  pointer positions, created when you press `M`), downloaded updates and a
  plain rolling `log.txt` with status messages. They live in
  `%APPDATA%\KeyboardMouse` and `%LOCALAPPDATA%\KeyboardMouse` and never
  leave your machine.
- Autostart adds one value to `HKEY_CURRENT_USER\...\Run` so Windows knows to
  start the app at logon. Removing it from the tray menu deletes that value.

## Update checks (off by default)

Turning on **Settings → Check for updates automatically** (or pressing
**Check for updates** yourself) makes the app request one public file:

```
https://api.github.com/repos/khoa-nhd/mouse-keyboard/releases/latest
```

That request contains only the standard HTTP headers and a
`KeyboardMouse/<version>` user agent. No identifier, no settings, no usage
data and no keystrokes are sent, and the response is not stored beyond the
version number and the time of the check. If a newer version exists, the
release zip and its published SHA-256 checksum are downloaded from GitHub,
the checksum is verified, and only then is the exe replaced. Nothing is
downloaded without your confirmation.

The app never contacts any other host.

The only input there is: your keyboard, for controlling the mouse. The only
output there is: mouse buttons and clicks through the Windows pointer.
