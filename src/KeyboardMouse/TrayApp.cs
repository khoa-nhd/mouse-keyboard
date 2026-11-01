using System.Diagnostics;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Hints;
using KeyboardMouse.Input;
using KeyboardMouse.Native;
using KeyboardMouse.Output;
using KeyboardMouse.Overlays;
using KeyboardMouse.Platform;

namespace KeyboardMouse;

/// <summary>
/// Owns the whole application: settings, hook, router, movement engine, tray.
/// Lives on the UI thread (WinForms ApplicationContext).
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private const string KeymapText = """
        KeyboardMouse - keymap

        MODE
          Tap Caps Lock ............ toggle mouse mode on/off
          Hold Caps Lock + key ..... momentary mouse mode (off on release)
          Shift + Caps Lock ........ real Caps Lock (passed through)
          Esc ...................... leave mouse mode
          Ctrl+Alt+Backspace ....... panic: release everything, leave mode

        MOVE
          I / J / K / L ............ up / left / down / right (diagonals work)
          Hold A ................... turbo speed
          Hold S ................... precision speed
          Hold D ................... scroll layer
            I / K .................. scroll up / down
            J / L .................. scroll left / right
            U / O .................. Page Up / Page Down
            (hold S too) ........... slow, precise scroll

        BUTTONS
          Space .................... left click (hold and move = drag)
          F ........................ right button (hold for menus)
          E ........................ middle click
          R ........................ double click
          U / O .................... back / forward
          G ........................ grid mode (U I O / J K L / M , .)
          H ........................ hint mode: label clickable elements
          V ........................ drag lock on/off
          T ........................ left click, then leave mouse mode
          Y / P / Z ................ send Ctrl+C / Ctrl+V / Ctrl+Z

        HINTS
          H ........................ show labels (Esc cancels)
          type a label ............. left click it
          Shift + label ............ move there without clicking
          Ctrl + label ............. right click it
          F + label ................ right click it
          Backspace ................ remove the last typed letter

        JUMPS
          X ........................ center of the current screen
          C ........................ center of the foreground window
          N ........................ next monitor
          M then a-z ............... save a mark at the pointer
          ' then a-z ............... jump to a saved mark

        The pointer keeps working normally with the TrackPoint or a mouse.

        The toggle key is Caps Lock by default; remap it with
        activation.toggleKey in settings.json, then Reload settings.
        """;

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

    private Settings _settings;
    private SpeedProfiles _profiles;
    private bool _shutdown;

    public TrayApp()
    {
        Current = this;

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
            toggleKey: ParseToggleKey(_settings));
        _router.PollingChanged += UpdatePollingTimer;

        // Locking the session or suspending must never leave a button held.
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _cursorIndicator = new CursorIndicator(_state);
        _cursorIndicator.SetDotSize(_settings.Feedback.DotSize);
        _screenBorder = new ScreenBorder();
        _screenBorder.Refresh(_monitors);

        _iconOn = LoadIcon("tray-on.ico");
        _iconOff = LoadIcon("tray-off.ico");

        _pollTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _pollTimer.Tick += OnPollTick;

        _modeItem = new ToolStripMenuItem("Mouse mode (Caps Lock)");
        _modeItem.Click += (_, _) => ToggleMode();

        _pauseItem = new ToolStripMenuItem("Pause");
        _pauseItem.Click += (_, _) => _router.SetPaused(!_state.Paused);

        _autostartItem = new ToolStripMenuItem("Start with Windows");
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_modeItem);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("Show keymap", ShowKeymap));
        _menu.Items.Add(CreateItem("Open settings file", OpenSettingsFile));
        _menu.Items.Add(CreateItem("Reload settings", ReloadSettings));
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
        _tray.DoubleClick += (_, _) => ToggleMode();

        _state.MouseModeChanged += ScheduleUiUpdate;
        _state.PausedChanged += ScheduleUiUpdate;
        _autostartItem.Checked = Autostart.IsEnabled();

        _hook.Error += ex => Log.Error("Keyboard hook callback error", ex);
        try
        {
            _hook.Install(_router.HandleKey);
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

        _tray.ShowBalloonTip(
            4000,
            "KeyboardMouse is running",
            "Tap Caps Lock to toggle mouse mode. Hold Caps Lock for momentary mode.",
            ToolTipIcon.Info);
    }

    public static TrayApp? Current { get; private set; }

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
        _tray.Text = on ? "KeyboardMouse - mouse mode ON" : "KeyboardMouse - off";

        bool feedbackOn = on && !_state.Paused;
        _cursorIndicator.SetEnabled(feedbackOn && _settings.Feedback.CursorDot);
        _screenBorder.SetVisible(feedbackOn && _settings.Feedback.ScreenBorder);
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

    private void ShowKeymap()
    {
        MessageBox.Show(
            KeymapText,
            "KeyboardMouse keymap",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
        _profiles = BuildProfiles(_settings);
        _router.UpdateProfiles(_profiles);
        _router.SetTapTimeout(_settings.Activation.TapTimeoutMs);
        _autostartItem.Checked = Autostart.IsEnabled();

        _cursorIndicator.SetDotSize(_settings.Feedback.DotSize);
        _screenBorder.Refresh(_monitors);
        _router.UpdateGrid(_settings.Grid);
        _router.UpdateKeys(_settings.Keys);
        _router.UpdateHint(_settings.Hint);
        _router.UpdateEditingExtras(_settings.EditingExtras);
        _router.UpdateAutoOff(_settings.Activation.AutoOffSeconds);
        _router.SetToggleKey(ParseToggleKey(_settings));
        UpdateUi();

        string message = _settingsStore.LastError is null
            ? "Settings reloaded."
            : "Settings reloaded with errors: " + _settingsStore.LastError;
        _tray.ShowBalloonTip(3000, "KeyboardMouse", message,
            _settingsStore.LastError is null ? ToolTipIcon.Info : ToolTipIcon.Warning);
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

    private static Icon LoadIcon(string fileName)
    {
        var assembly = typeof(TrayApp).Assembly;
        using var stream = assembly.GetManifestResourceStream($"KeyboardMouse.Assets.{fileName}");
        if (stream is not null)
        {
            return new Icon(stream);
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

        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }
}
