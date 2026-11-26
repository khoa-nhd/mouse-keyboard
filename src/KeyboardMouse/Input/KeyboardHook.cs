using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Input;

/// <summary>
/// WH_KEYBOARD_LL hook. The callback must stay trivial: it maps the event and
/// hands it to a single handler, nothing else. Injected events (our own
/// SendInput output, other remappers) are always passed through untouched.
/// Installed on the UI thread, which owns the message loop.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly User32.HookProc _proc; // keep alive: GC would crash the hook
    private readonly HashSet<PhysicalKey> _keysDown = new();
    private IntPtr _hook;
    private Func<KeyEvent, bool>? _handler;
    private bool _disposed;
    private System.Threading.Timer? _watchdog;
    private long _lastCallbackTicks;
    private int _reinstalling;
    private long _lastRawTicks;      // last WM_INPUT keyboard event (raw input sees every key)
    private long _lastReinstallTicks;
    private RawKeyboardSink? _rawSink;
    private SynchronizationContext? _uiContext; // the installing (message loop) thread

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public event Action<Exception>? Error;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install(Func<KeyEvent, bool> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _handler = handler;

        // A low-level hook delivers its callbacks through the message loop of
        // the thread that installed it, so the watchdog must reinstall here too.
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("KeyboardHook must be installed on the UI thread.");

        if (_hook != IntPtr.Zero)
        {
            return;
        }

        // For WH_KEYBOARD_LL, hMod is only used to locate the callback and may
        // be the module handle of the current executable.
        var module = Kernel32.GetModuleHandleW(null);
        _hook = User32.SetWindowsHookExW(User32.WH_KEYBOARD_LL, _proc, module, 0);
        if (_hook == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetWindowsHookEx(WH_KEYBOARD_LL) failed with error {error}.");
        }

        _keysDown.Clear();
        Volatile.Write(ref _lastCallbackTicks, Environment.TickCount64);
        Volatile.Write(ref _lastRawTicks, 0);

        // Windows silently removes slow low-level hooks. Raw input reports
        // every key without us injecting anything, so a key that raw input
        // saw but the hook did not means the hook is gone. Nothing synthetic
        // is ever sent: injected input resets the idle timer and would keep
        // the PC from sleeping or starting the screen saver.
        if (_rawSink is null)
        {
            try
            {
                _rawSink = new RawKeyboardSink(() => Volatile.Write(ref _lastRawTicks, Environment.TickCount64));
            }
            catch (Exception ex)
            {
                Log.Error("Raw keyboard input is unavailable; the hook watchdog is off", ex);
            }
        }

        if (_watchdog is null && _rawSink is not null)
        {
            _watchdog = new System.Threading.Timer(WatchdogTick, null, 2000, 2000);
        }
    }

    public void Uninstall()
    {
        _handler = null;
        _watchdog?.Dispose();
        _watchdog = null;
        _rawSink?.Dispose();
        _rawSink = null;
        if (_hook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _keysDown.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // Heartbeat: updated before any filtering, so even injected heart
        // beats prove the hook is alive.
        Volatile.Write(ref _lastCallbackTicks, Environment.TickCount64);

        try
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Never react to our own injected input: otherwise e.g. the copy
            // chord we synthesize would be routed back into the app.
            if ((data.flags & User32.LLKHF_INJECTED) != 0)
            {
                return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            bool isDown = wParam == User32.WM_KEYDOWN || wParam == User32.WM_SYSKEYDOWN;
            bool isUp = wParam == User32.WM_KEYUP || wParam == User32.WM_SYSKEYUP;
            if (!isDown && !isUp)
            {
                return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            bool extended = (data.flags & User32.LLKHF_EXTENDED) != 0;
            var key = PhysicalKeyExtensions.FromScanCode(data.scanCode, extended);
            if (key == PhysicalKey.None)
            {
                return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            bool isRepeat = false;
            if (isDown)
            {
                isRepeat = !_keysDown.Add(key);
            }
            else
            {
                _keysDown.Remove(key);
            }

            var keyEvent = new KeyEvent(key, isDown, isRepeat, Environment.TickCount64);
            var handler = _handler;
            bool swallow = handler is not null && handler(keyEvent);
            return swallow
                ? new IntPtr(1)
                : User32.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            // A throwing hook callback would take down the whole keyboard.
            Error?.Invoke(ex);
            return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Uninstall();
    }

    /// <summary>
    /// Runs on a timer thread every 2 s. Reinstalls the hook when raw input
    /// saw a key that the hook never got. Sends no input of its own.
    /// </summary>
    private void WatchdogTick(object? state)
    {
        if (_disposed || _handler is null)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (!HookLooksDead(
                Volatile.Read(ref _lastRawTicks),
                Volatile.Read(ref _lastCallbackTicks),
                now))
        {
            return;
        }

        // At most one reinstall per 10 s, in case some foreground window
        // (an elevated one, say) keeps raw input and the hook out of step.
        if (now - Interlocked.Read(ref _lastReinstallTicks) < 10_000)
        {
            return;
        }

        // Never call SetWindowsHookEx from this timer thread: it has no message
        // loop, so a hook installed here would stall every keystroke system-wide.
        if (Interlocked.CompareExchange(ref _reinstalling, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastReinstallTicks, now);
        _uiContext?.Post(_ => ReinstallSilently(), null);
    }

    /// <summary>
    /// True when raw input reported a key more than a second after the hook's
    /// last callback, and that key is old enough (300 ms) that a live hook
    /// would have seen it by now.
    /// </summary>
    internal static bool HookLooksDead(long lastRawTicks, long lastCallbackTicks, long nowTicks)
    {
        if (lastRawTicks <= 0)
        {
            return false;
        }

        return lastRawTicks - lastCallbackTicks > 1000 && nowTicks - lastRawTicks > 300;
    }

    /// <summary>Runs on the UI thread (posted by the watchdog).</summary>
    private void ReinstallSilently()
    {
        try
        {
            if (_disposed || _handler is null)
            {
                return;
            }

            Log.Info("Keyboard hook missed its heartbeat; reinstalling it.");

            if (_hook != IntPtr.Zero)
            {
                User32.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            var module = Kernel32.GetModuleHandleW(null);
            _hook = User32.SetWindowsHookExW(User32.WH_KEYBOARD_LL, _proc, module, 0);

            Volatile.Write(ref _lastCallbackTicks, Environment.TickCount64);

            if (_hook == IntPtr.Zero)
            {
                Log.Error($"Watchdog reinstall failed with error {Marshal.GetLastWin32Error()}.");
            }
            else
            {
                Log.Info("Keyboard hook reinstalled by the watchdog.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Watchdog reinstall failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _reinstalling, 0);
        }
    }
}

/// <summary>
/// A message-only window that receives raw keyboard input in the background
/// (RIDEV_INPUTSINK). It only notes when a key arrived; it never reads which.
/// </summary>
internal sealed class RawKeyboardSink : NativeWindow, IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private readonly Action _onKey;

    public RawKeyboardSink(Action onKey)
    {
        _onKey = onKey;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE

        var device = new User32.RAWINPUTDEVICE
        {
            usUsagePage = 0x01, // generic desktop
            usUsage = 0x06,     // keyboard
            dwFlags = User32.RIDEV_INPUTSINK,
            hwndTarget = Handle
        };

        if (!User32.RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<User32.RAWINPUTDEVICE>()))
        {
            int error = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new InvalidOperationException($"RegisterRawInputDevices failed with error {error}.");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            _onKey();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            var device = new User32.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = User32.RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            };
            User32.RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<User32.RAWINPUTDEVICE>());
            DestroyHandle();
        }
    }
}
