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
    private int _staleChecks;
    private int _reinstalling;
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
        _staleChecks = 0;
        if (_watchdog is null)
        {
            // Windows silently removes slow low-level hooks. Every 5 s we
            // poke with a system-ignored key and reinstall if no callback saw it.
            _watchdog = new System.Threading.Timer(WatchdogTick, null, 5000, 5000);
        }
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
    /// Runs on a timer thread every 5 s. If the last callback is older than
    /// 3.5 s the hook has seen nothing for a while — expected when nobody is
    /// pressing keys. We inject one system-ignored key and wait briefly; a
    /// live hook sees it and refreshes the heartbeat. If not, the hook was
    /// silently removed and we reinstall it.
    /// </summary>
    private void WatchdogTick(object? state)
    {
        if (_disposed || _handler is null)
        {
            return;
        }

        long elapsed = Environment.TickCount64 - Volatile.Read(ref _lastCallbackTicks);
        if (elapsed < 3500)
        {
            _staleChecks = 0;
            return;
        }

        SendHeartbeatPoke();
        System.Threading.Thread.Sleep(300);

        if (_disposed)
        {
            return;
        }

        elapsed = Environment.TickCount64 - Volatile.Read(ref _lastCallbackTicks);
        if (elapsed < 1000)
        {
            _staleChecks = 0;
            return; // alive: the poke refreshed the heartbeat
        }

        if (_staleChecks++ < 1)
        {
            return; // one more poke cycle before assuming the hook is gone
        }

        // Never call SetWindowsHookEx from this timer thread: it has no message
        // loop, so a hook installed here would stall every keystroke system-wide.
        if (Interlocked.CompareExchange(ref _reinstalling, 1, 0) != 0)
        {
            return;
        }

        _uiContext?.Post(_ => ReinstallSilently(), null);
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
            _staleChecks = 0;

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

    /// <summary>
    /// Sends down+up of an unassigned virtual key (VK 0xFF). Nothing maps it,
    /// so foreground apps ignore it, but low-level hooks still receive it.
    /// </summary>
    private static void SendHeartbeatPoke()
    {
        const ushort unassignedVk = 0xFF;

        try
        {
            var down = new INPUT
            {
                type = User32.INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = unassignedVk, wScan = 0 }
                }
            };

            var up = down;
            up.u.ki.dwFlags |= User32.KEYEVENTF_KEYUP;

            User32.SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
        }
        catch
        {
            // The poke must never crash anything.
        }
    }
}
