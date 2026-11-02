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

        // If the user physically holds Shift or Alt right now, release them
        // inside the batch (otherwise Ctrl+C would arrive as Ctrl+Shift+C)
        // and restore them afterwards. See PLAN.md §5.8.
        var chord = ChordInputs(
            ctrl: ctrl,
            scan: scanCode,
            extended: extended,
            leftShiftDown: IsPhysicallyDown(User32.VK_LSHIFT),
            rightShiftDown: IsPhysicallyDown(User32.VK_RSHIFT),
            leftAltDown: IsPhysicallyDown(User32.VK_LMENU),
            rightAltDown: IsPhysicallyDown(User32.VK_RMENU));

        var inputs = new List<INPUT>(chord.Count);
        foreach (var (scan, isExtended, up) in chord)
        {
            inputs.Add(KeyInput(scan, isExtended, up));
        }

        Send(inputs.ToArray());
    }

    private static bool IsPhysicallyDown(ushort vk) =>
        (User32.GetAsyncKeyState((short)vk) & 0x8000) != 0;

    /// <summary>
    /// The scan-code sequence (code, extended, up) of one chord as a single
    /// batch. Every Shift/Alt half the user is holding is released on its own
    /// scan code before the chord and pressed again afterwards: releasing the
    /// left halves for a right-side hold would leave the modifier stuck and
    /// turn the chord into Ctrl+Shift+C or Alt+key.
    /// </summary>
    internal static List<(ushort Scan, bool Extended, bool Up)> ChordInputs(
        bool ctrl,
        ushort scan,
        bool extended,
        bool leftShiftDown,
        bool rightShiftDown,
        bool leftAltDown,
        bool rightAltDown)
    {
        const ushort leftShift = 0x2A;
        const ushort rightShift = 0x36;
        const ushort leftAlt = 0x38;
        const ushort rightAlt = 0x38;

        var inputs = new List<(ushort, bool, bool)>(8);

        if (rightShiftDown)
        {
            inputs.Add((rightShift, false, true));
        }

        if (leftShiftDown)
        {
            inputs.Add((leftShift, false, true));
        }

        if (rightAltDown)
        {
            inputs.Add((rightAlt, true, true));
        }

        if (leftAltDown)
        {
            inputs.Add((leftAlt, false, true));
        }

        if (ctrl)
        {
            var (ctrlScan, ctrlExtended) = PhysicalKey.LeftCtrl.ToScanCode();
            inputs.Add((ctrlScan, ctrlExtended, false));
        }

        inputs.Add((scan, extended, false));
        inputs.Add((scan, extended, true));

        if (ctrl)
        {
            var (ctrlScan, ctrlExtended) = PhysicalKey.LeftCtrl.ToScanCode();
            inputs.Add((ctrlScan, ctrlExtended, true));
        }

        if (leftAltDown)
        {
            inputs.Add((leftAlt, false, false));
        }

        if (rightAltDown)
        {
            inputs.Add((rightAlt, true, false));
        }

        if (leftShiftDown)
        {
            inputs.Add((leftShift, false, false));
        }

        if (rightShiftDown)
        {
            inputs.Add((rightShift, false, false));
        }

        return inputs;
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
