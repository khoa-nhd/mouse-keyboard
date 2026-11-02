using System.Diagnostics;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;
using KeyboardMouse.Input;
using KeyboardMouse.Native;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;
using KeyboardMouse.Platform;
using KeyboardMouse.Ui;

namespace KeyboardMouse;

/// <summary>
/// Owns the whole application: settings, hook, router, movement engine, tray.
/// Lives on the UI thread (WinForms ApplicationContext).
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly AppState _state = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly MouseOutput _mouse = new();
    private readonly KeyOutput _keyOutput = new();
    private readonly Monitors _monitors;
    private readonly Marks _marks;
    private readonly MovementEngine _movement;
    private readonly KeyboardHook _hook = new();
    private readonly InputRouter _router;
    private readonly CursorIndicator _cursorIndicator;
    private readonly ScreenBorder _screenBorder;
    private readonly GridOverlay _gridOverlay;
    private readonly HintOverlay _hintOverlay;
    private readonly ClickableScanner _clickableScanner;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly Control _uiDispatcher = new();
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private readonly ToolStripMenuItem _modeItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _pointerStyleItem;
    private readonly Dictionary<CursorStyle, ToolStripMenuItem> _pointerStyleItems = new();
    private readonly ToolStripMenuItem _checkUpdatesItem;
    private readonly UpdateService _updates = new();

    private const string RepoUrl = "https://github.com/khoa-nhd/mouse-keyboard";

    private readonly Icon _appIcon;

    private Settings _settings;
    private SpeedProfiles _profiles;
    private bool _shutdown;
    private GuideWindow? _guide;
    private bool _toldAboutTray;
    private bool _checkingForUpdates;
    private bool _downloadingUpdate;

    public TrayApp(bool showGuide = false)
    {
        Current = this;

        // Keep an existing autostart entry pointing at this exe, with the
        // argument that keeps sign-in starts silent.
        Autostart.Refresh();
        _appIcon = LoadIcon("app.ico", SystemInformation.IconSize);

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();

        _monitors = new Monitors();
        _marks = new Marks();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Message target used to defer UI work out of the keyboard hook and the
        // hint scanner threads. Create it before anything can dispatch to it.
        _ = _uiDispatcher.Handle;

_profiles = BuildProfiles(_settings);
_movement = new MovementEngine(_mouse, _profiles, _monitors);
        _movement = new MovementEngine(_mouse, _profiles, _monitors);
        _gridOverlay = new GridOverlay();
        _hintOverlay = new HintOverlay();
        _clickableScanner = new ClickableScanner(_monitors);
        _router = new InputRouter(
            _state,
            _movement,
            _mouse,
            _settings.Activation.TapTimeoutMs,
            keyOutput: _keyOutput,
            profiles: _profiles,
            monitors: _monitors,
            gridOverlay: _gridOverlay,
            gridSettings: _settings.Grid,
            marks: _marks,
            keys: _settings.Keys,
            clickableScanner: _clickableScanner,
            hintOverlay: _hintOverlay,
            hintSettings: _settings.Hint,
            foreground: User32.GetForegroundWindow,
            dispatch: DispatchToUi,
            editingExtras: _settings.EditingExtras,
            autoOffSeconds: _settings.Activation.AutoOffSeconds,
            toggleKey: ParseToggleKey(_settings),
            autoClick: _settings.AutoClick);
        _router.PollingChanged += UpdatePollingTimer;
        _router.AutoClickChanged += ScheduleUiUpdate;

        // Locking the session or suspending must never leave a button held.
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _cursorIndicator = new CursorIndicator(_state);
        _cursorIndicator.SetDotSize(_settings.Feedback.DotSize);
        _cursorIndicator.SetStyle(ParseCursorStyle());
        _screenBorder = new ScreenBorder();
        _screenBorder.Refresh(_monitors);

        _iconOn = LoadIcon("tray-on.ico", SystemInformation.SmallIconSize);
        _iconOff = LoadIcon("tray-off.ico", SystemInformation.SmallIconSize);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _pollTimer.Tick += OnPollTick;

        _modeItem = new ToolStripMenuItem("Mouse mode (Caps Lock)");
        _modeItem.Click += (_, _) => ToggleMode();

        _pauseItem = new ToolStripMenuItem("Pause");
        _pauseItem.Click += (_, _) => _router.SetPaused(!_state.Paused);

        _autostartItem = new ToolStripMenuItem("Start with Windows");
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _pointerStyleItem = new ToolStripMenuItem("Pointer style");
        foreach (var style in CursorStyleNames.All)
        {
            var chosen = style;
            var item = new ToolStripMenuItem(CursorStyleNames.DisplayName(chosen))
            {
                CheckOnClick = false
            };
            item.Click += (_, _) => SetCursorStyle(chosen);
            _pointerStyleItems[chosen] = item;
            _pointerStyleItem.DropDownItems.Add(item);
        }

        _checkUpdatesItem = new ToolStripMenuItem("Check for updates…");
        _checkUpdatesItem.Click += (_, _) => _ = CheckForUpdatesAsync(silent: false);

        _menu = new ContextMenuStrip();
        var openItem = CreateItem("Open KeyboardMouse", () => ShowGuide());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        _menu.Items.Add(openItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_modeItem);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(_pointerStyleItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("Show keymap", () => ShowGuide("keys")));
        _menu.Items.Add(CreateItem("Open settings file", OpenSettingsFile));
        _menu.Items.Add(CreateItem("Reload settings", ReloadSettings));
        _menu.Items.Add(_checkUpdatesItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(CreateItem("Open log folder", OpenLogFolder));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("Exit", ExitThread));

        _tray = new NotifyIcon
        {
            Icon = _iconOff,
            Text = "KeyboardMouse - off",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) => ShowGuide();

        _state.MouseModeChanged += ScheduleUiUpdate;
        _state.PausedChanged += ScheduleUiUpdate;
        _autostartItem.Checked = Autostart.IsEnabled();
        UpdatePointerStyleChecks();

        _hook.Error += ex => Log.Error("Keyboard hook callback error", ex);
        try
        {
            _hook.Install(OnKey);
            Log.Info("Keyboard hook installed.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not install the keyboard hook", ex);
            MessageBox.Show(
                "KeyboardMouse could not install its keyboard hook.\r\n\r\n" + ex.Message,
                "KeyboardMouse",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        UpdateUi();

        if (showGuide)
        {
            // Open once the message loop runs.
            DispatchToUi(() => ShowGuide());
        }

        if (_settings.Update.CheckOnStart)
        {
            DispatchToUi(() => _ = CheckForUpdatesAsync(silent: true));
        }
    }

    public static TrayApp? Current { get; private set; }

    /// <summary>Hook callback: route the key, and mirror it to the guide's
    /// keyboard while the guide itself is the active window (never otherwise,
    /// so typing in other apps is never forwarded anywhere).</summary>
    private bool OnKey(KeyEvent keyEvent)
    {
        bool swallow = _router.HandleKey(keyEvent);

        if (_guide is { } guide && !keyEvent.IsRepeat && Form.ActiveForm == guide)
        {
            string name = keyEvent.Key.ToString();
            bool down = keyEvent.IsDown;
            DispatchToUi(() => _guide?.PostKey(name, down));
        }

        return swallow;
    }

    /// <summary>Thread-safe: another launch of the exe asked us to show the window.</summary>
    public void RequestShowGuide() => DispatchToUi(() => ShowGuide());

    private void ShowGuide(string page = "home")
    {
        if (_shutdown)
        {
            return;
        }

        if (_guide is null || _guide.IsDisposed)
        {
            _guide = new GuideWindow(_appIcon, page);
            _guide.CommandReceived += OnGuideCommand;
            _guide.FormClosed += (_, _) => OnGuideClosed();
            _guide.Show();
        }
        else
        {
            if (_guide.WindowState == FormWindowState.Minimized)
            {
                _guide.WindowState = FormWindowState.Normal;
            }

            _guide.ShowPage(page);
        }

        _guide.Activate();
        User32.SetForegroundWindow(_guide.Handle);
    }

    private void OnGuideClosed()
    {
        _guide = null;
        if (!_toldAboutTray && !_shutdown)
        {
            _toldAboutTray = true;
            _tray.ShowBalloonTip(
                4000,
                "KeyboardMouse is still running",
                "Tap Caps Lock for mouse mode. Double-click this icon to open the guide again.",
                ToolTipIcon.Info);
        }
    }

    private void PushGuideState()
    {
        if (_guide is null || _guide.IsDisposed)
        {
            return;
        }

        var version = typeof(TrayApp).Assembly.GetName().Version;
        _guide.PushState(new
        {
            version = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}",
            mouseMode = _state.MouseModeOn,
            paused = _state.Paused,
            autoClicking = _router.AutoClicking,
            autostart = Autostart.IsEnabled(),
            shortcuts = new
            {
                desktop = Shortcuts.Exists(ShortcutLocation.Desktop),
                startMenu = Shortcuts.Exists(ShortcutLocation.StartMenu)
            },
            settings = new
            {
                toggleKey = _settings.Activation.ToggleKey,
                editingExtras = _settings.EditingExtras,
                cursorDot = _settings.Feedback.CursorDot,
                cursorStyle = _settings.Feedback.CursorStyle,
                dotSize = _settings.Feedback.DotSize,
                screenBorder = _settings.Feedback.ScreenBorder,
                autoClick = new { intervalMs = _settings.AutoClick.IntervalMs, button = _settings.AutoClick.Button },
                updateCheckOnStart = _settings.Update.CheckOnStart,
                keys = _settings.Keys
            }
        });
    }

    /// <summary>Commands from guide.html. Every input is validated here; the
    /// page can only change the handful of settings listed below.</summary>
    private void OnGuideCommand(GuideCommand command)
    {
        switch (command.Name)
        {
            case "ready":
                break;

            case "setMouseMode":
                if (_state.Paused)
                {
                    _router.SetPaused(false);
                }

                _router.SetMode(command.GetBool("on"));
                break;

            case "setPaused":
                _router.SetPaused(command.GetBool("on"));
                break;

            case "setAutostart":
                if (!Autostart.SetEnabled(command.GetBool("on")))
                {
                    _guide?.Toast("toast.failed");
                }

                _autostartItem.Checked = Autostart.IsEnabled();
                break;

            case "setSetting":
                ApplyGuideSetting(command.GetString("key"), command.Get("value"));
                break;

            case "createShortcut":
                var where = command.GetString("where") == "desktop" ? ShortcutLocation.Desktop : ShortcutLocation.StartMenu;
                _guide?.Toast(Shortcuts.Create(where) ? "toast.shortcut" : "toast.failed");
                break;

            case "openSettingsFile":
                OpenSettingsFile();
                break;

            case "reloadSettings":
                ReloadSettings();
                _guide?.Toast("toast.reloaded");
                break;

            case "checkForUpdates":
                _ = CheckForUpdatesAsync(silent: false);
                break;

            case "openLogFolder":
                OpenLogFolder();
                break;

            case "openUrl":
                // Only our own pages; the page cannot open arbitrary URLs.
                GuideWindow.OpenUrl(command.GetString("id") == "issues" ? RepoUrl + "/issues" : RepoUrl);
                break;

            case "exitApp":
                ExitThread();
                return;

            default:
                Log.Info($"Ignored unknown guide command '{command.Name}'.");
                return;
        }

        PushGuideState();
    }

    private void ApplyGuideSetting(string? key, System.Text.Json.JsonElement? value)
    {
        if (value is not { } v)
        {
            return;
        }

        bool isBool = v.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False;
        switch (key)
        {
            case "cursorDot" when isBool:
                _settings.Feedback.CursorDot = v.GetBoolean();
                break;
            case "cursorStyle" when v.ValueKind == System.Text.Json.JsonValueKind.String:
                if (!CursorStyleNames.TryParse(v.GetString(), out var style))
                {
                    return;
                }

                _settings.Feedback.CursorStyle = CursorStyleNames.ToId(style);
                break;
            case "dotSize" when v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out int size):
                _settings.Feedback.DotSize = size;
                break;
            case "update.checkOnStart" when isBool:
                _settings.Update.CheckOnStart = v.GetBoolean();
                break;
            case "screenBorder" when isBool:
                _settings.Feedback.ScreenBorder = v.GetBoolean();
                break;
            case "editingExtras" when isBool:
                _settings.EditingExtras = v.GetBoolean();
                break;
            case "autoClick.intervalMs" when v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out int ms):
                _settings.AutoClick.IntervalMs = ms;
                break;
            case "autoClick.button" when v.ValueKind == System.Text.Json.JsonValueKind.String:
                _settings.AutoClick.Button = v.GetString() ?? "Left";
                break;
            default:
                return;
        }

        _settings.Normalize();
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
            _guide?.Toast("toast.failed");
            return;
        }

        ApplySettings();
        _guide?.Toast("toast.saved");
    }

    /// <summary>Best-effort release from a crash handler on any thread.</summary>
    public static void EmergencyRelease()
    {
        try
        {
            Current?._mouse.ReleaseAll();
        }
        catch
        {
            // Nothing safe left to do.
        }
    }

    /// <summary>Panic on the UI thread (used by the unhandled-exception handler).</summary>
    public void EmergencyStop()
    {
        if (_shutdown)
        {
            return;
        }

        try
        {
            _router.Panic();
            UpdateUi();
        }
        catch (Exception ex)
        {
            Log.Error("Emergency stop failed", ex);
        }
    }

    private static ToolStripMenuItem CreateItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private void ToggleMode()
    {
        if (_state.Paused)
        {
            _router.SetPaused(false);
        }

        _router.SetMode(!_state.MouseModeOn);
    }

    private void UpdateUi()
    {
        if (_shutdown)
        {
            return;
        }

        bool on = _state.MouseModeOn;
        _modeItem.Checked = on;
        _pauseItem.Checked = _state.Paused;
        _tray.Icon = on ? _iconOn : _iconOff;
        _tray.Text = !on
            ? "KeyboardMouse - off"
            : _router.AutoClicking
                ? "KeyboardMouse - auto clicking (B to stop)"
                : "KeyboardMouse - mouse mode ON";

        bool feedbackOn = on && !_state.Paused;
        _cursorIndicator.SetEnabled(feedbackOn && _settings.Feedback.CursorDot);
        _screenBorder.SetVisible(feedbackOn && _settings.Feedback.ScreenBorder);
        PushGuideState();
    }

    /// <summary>
    /// Queue a UI refresh instead of doing tray work inside the hook callback.
    /// </summary>
    private void ScheduleUiUpdate()
    {
        if (_shutdown)
        {
            return;
        }

        if (_uiDispatcher.IsHandleCreated)
        {
            try
            {
                _uiDispatcher.BeginInvoke(new Action(UpdateUi));
                return;
            }
            catch (ObjectDisposedException)
            {
                // Fall through to a direct update during shutdown races.
            }
        }

        UpdateUi();
    }

    /// <summary>
    /// Runs hint-scan callbacks on the UI thread. The scanner calls this from
    /// its MTA worker thread; overlay windows must only be touched here.
    /// </summary>
    private void DispatchToUi(Action action)
    {
        if (_shutdown)
        {
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
        catch (InvalidOperationException)
        {
            // No window handle (should not happen after the constructor).
        }
    }

    private void UpdatePollingTimer()
    {
        if (_shutdown)
        {
            return;
        }

        if (_router.PollingRequired)
        {
            if (!_pollTimer.Enabled)
            {
                _pollTimer.Start();
            }
        }
        else if (_pollTimer.Enabled)
        {
            _pollTimer.Stop();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        _router.Tick();

        if (!_router.PollingRequired)
        {
            _pollTimer.Stop();
        }
    }

    private void OpenSettingsFile()
    {
        try
        {
            _settingsStore.Save(_settings);
            Process.Start(new ProcessStartInfo(_settingsStore.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the settings file", ex);
            _tray.ShowBalloonTip(3000, "KeyboardMouse", "Could not open settings: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void ReloadSettings()
    {
        _settings = _settingsStore.Load();
        ApplySettings();

        string message = _settingsStore.LastError is null
            ? "Settings reloaded."
            : "Settings reloaded with errors: " + _settingsStore.LastError;
        _tray.ShowBalloonTip(3000, "KeyboardMouse", message,
            _settingsStore.LastError is null ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";

    /// <summary>Startup checks are silent and throttled to once a day.</summary>
    private bool ShouldCheckOnStart()
    {
        if (!_settings.Update.CheckOnStart)
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(_settings.Update.LastCheckedUtc, out var last))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - last >= TimeSpan.FromHours(24);
    }

    /// <summary>Opt-in: the only network call the app ever makes.</summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_shutdown || _checkingForUpdates || _downloadingUpdate)
        {
            return;
        }

        if (silent && !ShouldCheckOnStart())
        {
            return;
        }

        _checkingForUpdates = true;
        _checkUpdatesItem.Enabled = false;
        string originalText = _checkUpdatesItem.Text ?? "Check for updates…";
        _checkUpdatesItem.Text = "Checking for updates…";

        try
        {
            var result = await _updates.CheckAsync(CancellationToken.None);

            _settings.Update.LastCheckedUtc = DateTimeOffset.UtcNow.ToString("O");
            SaveSettingsQuietly();

            if (!result.UpdateAvailable || result.Release is not { } release)
            {
                if (!silent)
                {
                    string message = result.Error switch
                    {
                        "rate-limited" => "GitHub is limiting update checks right now. Try again later.",
                        "offline" => "Could not reach GitHub. Check your connection and try again.",
                        "cancelled" => "The update check was cancelled.",
                        "missing-assets" => "A newer release exists, but it is missing its checksum file, so it will not be installed automatically. You can download it from GitHub instead.",
                        "too-large" => "The newer release is much larger than expected. Download it from GitHub instead.",
                        _ => $"You are on the newest version ({FormatVersion(UpdateService.CurrentVersion)})."
                    };
                    MessageBox.Show(message, "KeyboardMouse update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            _checkUpdatesItem.Text = $"Update to {release.Tag}…";

            if (!silent && release.Tag == _settings.Update.SkippedVersion)
            {
                // The user already said "not now" to this exact version; the
                // manual menu entry still offers it.
            }

            if (silent)
            {
                if (release.Tag != _settings.Update.SkippedVersion)
                {
                    _tray.ShowBalloonTip(
                        6000,
                        "KeyboardMouse update",
                        $"Version {FormatVersion(release.Version)} is available. Right-click the tray icon → Check for updates.",
                        ToolTipIcon.Info);
                }

                return;
            }

            long megabytes = release.SizeBytes > 0 ? release.SizeBytes / (1024 * 1024) : 0;
            string size = megabytes > 0 ? $"\r\n\r\nAbout {megabytes} MB to download." : string.Empty;
            var answer = MessageBox.Show(
                $"Version {FormatVersion(release.Version)} is available (you have {FormatVersion(UpdateService.CurrentVersion)}).{size}\r\n\r\nDownload and install it now? KeyboardMouse will restart when the update is ready.",
                "KeyboardMouse update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                _settings.Update.SkippedVersion = release.Tag;
                SaveSettingsQuietly();
                return;
            }

            await DownloadAndInstallAsync(release);
        }
        finally
        {
            _checkingForUpdates = false;
            if (!_shutdown)
            {
                _checkUpdatesItem.Enabled = true;
                if (_checkUpdatesItem.Text == "Checking for updates…")
                {
                    _checkUpdatesItem.Text = originalText;
                }

                UpdateUi();
            }
        }
    }

    private async Task DownloadAndInstallAsync(ReleaseInfo release)
    {
        _downloadingUpdate = true;
        _checkUpdatesItem.Enabled = false;
        _tray.Text = "KeyboardMouse — downloading update";

        var progress = new Progress<int>(percent =>
        {
            _tray.Text = $"KeyboardMouse — update {percent}%";
        });

        try
        {
            var prepared = await _updates.DownloadAsync(release, progress, CancellationToken.None);

            _settings.Update.SkippedVersion = string.Empty;
            SaveSettingsQuietly();

            var answer = MessageBox.Show(
                $"Version {FormatVersion(release.Version)} is downloaded and verified.\r\n\r\nKeyboardMouse will close, swap the file and start again. Continue?",
                "KeyboardMouse update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                InstallPreparedUpdate(prepared);
                return;
            }

            Log.Info("Update downloaded but not installed.");
            try
            {
                File.Delete(prepared.StagedExePath);
            }
            catch (Exception ex)
            {
                Log.Info($"Could not remove the staged update: {ex.GetType().Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not download the update", ex);
            _tray.ShowBalloonTip(
                5000,
                "KeyboardMouse update",
                "The update could not be downloaded or verified. Nothing was changed — see the log for details.",
                ToolTipIcon.Error);
        }
        finally
        {
            _downloadingUpdate = false;
            if (!_shutdown)
            {
                _checkUpdatesItem.Enabled = true;
                _tray.Text = "KeyboardMouse";
                UpdateUi();
            }
        }
    }

    private void InstallPreparedUpdate(PreparedUpdate update)
    {
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!UpdateService.InstallOnExit(update, arguments))
        {
            MessageBox.Show(
                "The update was downloaded, but the file could not be replaced.\r\n\r\nIf KeyboardMouse lives in Program Files, move it to a folder you own or run it as administrator.",
                "KeyboardMouse update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ExitThread();
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    /// <summary>Pushes the current <see cref="_settings"/> into every component.</summary>
    private void ApplySettings()
    {
        _profiles = BuildProfiles(_settings);
        _router.UpdateProfiles(_profiles);
        _router.SetTapTimeout(_settings.Activation.TapTimeoutMs);
        _autostartItem.Checked = Autostart.IsEnabled();

        _cursorIndicator.SetDotSize(_settings.Feedback.DotSize);
        _cursorIndicator.SetStyle(ParseCursorStyle());
        UpdatePointerStyleChecks();
        _screenBorder.Refresh(_monitors);
        _router.UpdateGrid(_settings.Grid);
        _router.UpdateKeys(_settings.Keys);
        _router.UpdateHint(_settings.Hint);
        _router.UpdateEditingExtras(_settings.EditingExtras);
        _router.UpdateAutoOff(_settings.Activation.AutoOffSeconds);
        _router.UpdateAutoClick(_settings.AutoClick);
        _router.SetToggleKey(ParseToggleKey(_settings));
        UpdateUi();
    }

    private static SpeedProfiles BuildProfiles(Settings settings) => new(
        settings.Speed.Normal.ToProfile(),
        settings.Speed.Precision.ToProfile(),
        settings.Speed.Turbo.ToProfile(),
        settings.Scroll.ToProfile(),
        settings.Scroll.Smooth);

    /// <summary>activation.toggleKey — invalid or modifier values fall back to Caps Lock (also enforced in Settings.Normalize).</summary>
    private static PhysicalKey ParseToggleKey(Settings settings) =>
        KeyNames.TryParse(settings.Activation.ToggleKey, out var key)
            ? key
            : PhysicalKey.CapsLock;

    private CursorStyle ParseCursorStyle() =>
        CursorStyleNames.TryParse(_settings.Feedback.CursorStyle, out var style)
            ? style
            : CursorStyle.Dot;

    /// <summary>Tray submenu: switch the pointer indicator without opening the guide.</summary>
    private void SetCursorStyle(CursorStyle style)
    {
        _settings.Feedback.CursorStyle = CursorStyleNames.ToId(style);

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the pointer style", ex);
        }

        ApplySettings();
    }

    private void UpdatePointerStyleChecks()
    {
        var current = ParseCursorStyle();
        foreach (var (style, item) in _pointerStyleItems)
        {
            item.Checked = style == current;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_shutdown)
        {
            return;
        }

        try
        {
            _monitors.Refresh();
            _screenBorder.Refresh(_monitors);
            UpdateUi();
            Log.Info("Monitor layout refreshed.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not refresh the monitor layout", ex);
        }
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
        {
            EmergencyEndSession("Session locked");
        }
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            EmergencyEndSession("System suspending");
        }
    }

    /// <summary>
    /// Session lock / suspend: leave mouse mode and release everything.
    /// SystemEvents raises this on a worker thread, so hop to the UI thread
    /// before touching controllers and overlays.
    /// </summary>
    private void EmergencyEndSession(string reason)
    {
        if (_shutdown)
        {
            return;
        }

        DispatchToUi(() =>
        {
            if (_shutdown)
            {
                return;
            }

            try
            {
                _router.Panic();
                UpdateUi();
                Log.Info(reason + ": mouse mode off, all buttons released.");
            }
            catch (Exception ex)
            {
                Log.Error("Emergency end of session failed", ex);
            }
        });
    }

    private void ToggleAutostart()
    {
        bool target = !Autostart.IsEnabled();
        if (!Autostart.SetEnabled(target))
        {
            _tray.ShowBalloonTip(3000, "KeyboardMouse", "Could not change the autostart setting.", ToolTipIcon.Error);
        }

        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Log.DirectoryPath);
            Process.Start(new ProcessStartInfo(Log.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the log folder", ex);
        }
    }

    /// <summary>Loads an embedded .ico, picking the frame that fits <paramref name="size"/>
    /// (the icons carry several sizes for different DPI scales).</summary>
    private static Icon LoadIcon(string fileName, Size size)
    {
        var assembly = typeof(TrayApp).Assembly;
        using var stream = assembly.GetManifestResourceStream($"KeyboardMouse.Assets.{fileName}");
        if (stream is not null)
        {
            return new Icon(stream, size);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Shutdown();
        }

        base.Dispose(disposing);
    }

    private void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;

        _state.MouseModeChanged -= ScheduleUiUpdate;
        _state.PausedChanged -= ScheduleUiUpdate;
        _router.PollingChanged -= UpdatePollingTimer;
        _router.AutoClickChanged -= ScheduleUiUpdate;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        try
        {
            _hook.Uninstall();
        }
        catch
        {
            // Ignore: we are shutting down.
        }

        _router.Panic();

        _guide?.Close();
        _guide = null;

        _pollTimer.Stop();
        _pollTimer.Dispose();

        _cursorIndicator.Dispose();
        _screenBorder.Dispose();
        _gridOverlay.Dispose();
        _hintOverlay.Dispose();
        _marks.Dispose();

        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _uiDispatcher.Dispose();

        _movement.Dispose();
        _hook.Dispose();

        _iconOn.Dispose();
        _iconOff.Dispose();
        _appIcon.Dispose();

        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }
}
