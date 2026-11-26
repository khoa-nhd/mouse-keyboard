using System.Runtime.InteropServices;
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
    private int _missedPolls;        // polls that saw a key held while the hook was silent
    private long _lastReinstallTicks;
    private int _probePending;
    private int _pollCount;
#if DEBUG
    private System.Threading.Timer? _testDropper;
#endif
    private readonly bool[] _heldBefore = new bool[256]; // watchdog thread only
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
        Interlocked.Exchange(ref _missedPolls, 0);

        // Windows silently removes slow low-level hooks. Every 100 ms the
        // watchdog asks Windows which keys are held down: new key presses
        // while the hook has heard nothing for a while mean the hook is gone.
        // It only reads key state. It never sends input (that would reset
        // the idle timer and keep the PC awake) and never registers for raw
        // input (that stops the hook while our own window is in front).
        _watchdog ??= new System.Threading.Timer(WatchdogTick, null, 1000, 50);
#if DEBUG
        if (Environment.GetEnvironmentVariable("KM_TEST_DROP_HOOK") == "1")
        {
            // Simulates Windows silently removing the hook.
            var context = _uiContext;
            var dropper = new System.Threading.Timer(_ => context.Post(__ =>
            {
                User32.UnhookWindowsHookEx(_hook);
                Log.Info("[test] hook dropped on purpose");
            }, null));
            dropper.Change(4000, System.Threading.Timeout.Infinite);
            GC.KeepAlive(dropper);
            _testDropper = dropper;
        }
#endif
    }

    public void Uninstall()
    {
        _handler = null;
        _watchdog?.Dispose();
        _watchdog = null;
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

        // Heartbeat: updated before any filtering, so every event the hook
        // sees proves it is alive.
        Volatile.Write(ref _lastCallbackTicks, Environment.TickCount64);

        try
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Never react to our own injected input: otherwise e.g. the copy
            // chord we synthesize would be routed back into the app.
            if ((data.flags & User32.LLKHF_INJECTED) != 0 && !IsTestKey(data))
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
            long started = Environment.TickCount64;
            bool swallow = handler is not null && handler(keyEvent);
            long took = Environment.TickCount64 - started;
            if (took > 250)
            {
                // Windows drops a hook that is this slow; say which key did it.
                Log.Info($"Slow key handler: {key} {(isDown ? "down" : "up")} took {took} ms.");
            }
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

#if DEBUG
    // Debug builds only: with KM_TEST_KEYS=1, injected keys tagged with this
    // extra-info value count as real, so UI tests can drive the hook.
    private static readonly bool TestKeysEnabled = Environment.GetEnvironmentVariable("KM_TEST_KEYS") == "1";

    private static bool IsTestKey(KBDLLHOOKSTRUCT data) =>
        TestKeysEnabled && data.dwExtraInfo == (UIntPtr)0x4B4D5445u;
#else
    private static bool IsTestKey(KBDLLHOOKSTRUCT data) => false;
#endif

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

        // Every 2 s (40 polls), probe the UI thread: the hook runs there, so a stall over
        // the low-level hook timeout is what makes Windows drop the hook.
        if (++_pollCount % 40 == 0 && Interlocked.CompareExchange(ref _probePending, 1, 0) == 0)
        {
            long posted = now;
            _uiContext?.Post(_ =>
            {
                long delay = Environment.TickCount64 - posted;
                Interlocked.Exchange(ref _probePending, 0);
                if (delay > 500)
                {
                    Log.Info($"UI thread was busy for {delay} ms.");
                }
            }, null);
        }

        bool newKeyDown = NewKeyWentDown(_heldBefore);
        long silentFor = now - Volatile.Read(ref _lastCallbackTicks);
        if (!HookLooksDead(silentFor))
        {
            Interlocked.Exchange(ref _missedPolls, 0);
            return;
        }

        // Three fresh key presses that the hook never heard. A key stuck
        // down counts once at most, so it cannot trigger this on its own.
        if (!newKeyDown || Interlocked.Increment(ref _missedPolls) < 3)
        {
            return;
        }

        Interlocked.Exchange(ref _missedPolls, 0);

        // A non-elevated hook never sees keys typed into an elevated window:
        // that is not a dead hook.
        if (ForegroundIsOutOfReach())
        {
            return;
        }

        // At most one reinstall per 30 s.
        if (now - Interlocked.Read(ref _lastReinstallTicks) < 30_000)
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
    /// A live hook hears every key press within milliseconds, so a press
    /// after more than a second of silence was not delivered to the hook.
    /// </summary>
    internal static bool HookLooksDead(long silentForMs) => silentForMs > 1000;

    /// <summary>
    /// Reads which keyboard keys Windows reports as held and returns true
    /// when one of them was not held at the previous poll. Keys the hook
    /// swallows never show up here, so only keys that reached Windows count.
    /// </summary>
    private static bool NewKeyWentDown(bool[] heldBefore)
    {
        bool fresh = false;
        for (int vk = 0x08; vk <= 0xFE; vk++)
        {
            bool held = vk != 0xE7 && (User32.GetAsyncKeyState((short)vk) & 0x8000) != 0;
            if (held && !heldBefore[vk])
            {
                fresh = true;
            }

            heldBefore[vk] = held;
        }

        return fresh;
    }

    private static bool ForegroundIsOutOfReach()
    {
        try
        {
            IntPtr window = User32.GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return true;
            }

            User32.GetWindowThreadProcessId(window, out uint pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            _ = process.Handle; // throws for an elevated process we cannot open
            return false;
        }
        catch
        {
            return true;
        }
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
            Interlocked.Exchange(ref _missedPolls, 0);

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
