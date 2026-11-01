using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyboardMouse.Input;
using KeyboardMouse.Native;

namespace KeyboardMouse.Output;

/// <summary>
/// Keyboard output through SendInput using scan codes, so it follows the
/// physical key rather than the active layout. Chords are sent as one batch.
/// </summary>
public sealed class KeyOutput : IKeyOutput
{
    public void SendKey(PhysicalKey key)
    {
        var (scanCode, extended) = key.ToScanCode();
        if (scanCode == 0)
        {
            Debug.WriteLine($"[KeyOutput] No scan code for {key}.");
            return;
        }

        Send(
            KeyInput(scanCode, extended, up: false),
            KeyInput(scanCode, extended, up: true));
    }

    public void SendChord(bool ctrl, PhysicalKey key)
    {
        var (scanCode, extended) = key.ToScanCode();
        if (scanCode == 0)
        {
            Debug.WriteLine($"[KeyOutput] No scan code for {key}.");
            return;
        }

        var inputs = new List<INPUT>(4);
        // If the user physically holds Shift or Alt right now, release them
        // inside the batch (otherwise Ctrl+C would arrive as Ctrl+Shift+C)
        // and restore them afterwards. See PLAN.md §5.8.
        bool shiftHeld = IsPhysicallyDown(User32.VK_LSHIFT) || IsPhysicallyDown(User32.VK_RSHIFT);
        bool altHeld = IsPhysicallyDown(User32.VK_LMENU) || IsPhysicallyDown(User32.VK_RMENU);

        if (shiftHeld)
        {
            inputs.Add(ScanInput(0x2A, extended: false, up: true)); // LeftShift
        }

        if (altHeld)
        {
            inputs.Add(ScanInput(0x38, extended: false, up: true)); // LeftAlt
        }

        if (ctrl)
        {
            var (ctrlScan, ctrlExtended) = PhysicalKey.LeftCtrl.ToScanCode();
            inputs.Add(KeyInput(ctrlScan, ctrlExtended, up: false));
        }

        inputs.Add(KeyInput(scanCode, extended, up: false));
        inputs.Add(KeyInput(scanCode, extended, up: true));

        if (ctrl)
        {
            var (ctrlScan, ctrlExtended) = PhysicalKey.LeftCtrl.ToScanCode();
            inputs.Add(KeyInput(ctrlScan, ctrlExtended, up: true));
        }

        if (altHeld)
        {
            inputs.Add(ScanInput(0x38, extended: false, up: false));
        }

        if (shiftHeld)
        {
            inputs.Add(ScanInput(0x2A, extended: false, up: false));
        }

        Send(inputs.ToArray());
    }

    private static bool IsPhysicallyDown(ushort vk) =>
        (User32.GetAsyncKeyState((short)vk) & 0x8000) != 0;

    /// <summary>Key input for a fixed scan-code modifier (Shift/Alt restore).</summary>
    private static INPUT ScanInput(ushort scanCode, bool extended, bool up)
    {
        var input = KeyInput(scanCode, extended, up);
        return input;
    }

    private static INPUT KeyInput(ushort scanCode, bool extended, bool up)
    {
        uint flags = User32.KEYEVENTF_SCANCODE;
        if (extended)
        {
            flags |= User32.KEYEVENTF_EXTENDEDKEY;
        }

        if (up)
        {
            flags |= User32.KEYEVENTF_KEYUP;
        }

        return new INPUT
        {
            type = User32.INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        uint sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            Debug.WriteLine($"[KeyOutput] SendInput sent {sent}/{inputs.Length}, error {Marshal.GetLastWin32Error()}");
        }
    }
}
