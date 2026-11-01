using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyboardMouse.Native;

namespace KeyboardMouse.Output;

/// <summary>
/// Mouse output via SendInput. Absolute moves in virtual-desktop coordinates
/// avoid the "enhance pointer precision" distortion of relative moves.
///
/// Every button that goes down is tracked here and released by ReleaseAll(),
/// which is called on mode off, panic, session lock, exit and crash.
/// </summary>
public sealed class MouseOutput : IMouseOutput
{
    private const int LeftFlag = 1 << 0;
    private const int RightFlag = 1 << 1;
    private const int MiddleFlag = 1 << 2;
    private const int X1Flag = 1 << 3;
    private const int X2Flag = 1 << 4;

    private readonly object _gate = new();
    private int _held;

    public MouseOutput()
    {
        // Catch a broken union layout early rather than sending garbage input.
        int expected = IntPtr.Size == 8 ? 40 : 28;
        int actual = Marshal.SizeOf<INPUT>();
        if (actual != expected)
        {
            throw new InvalidOperationException($"INPUT struct layout is {actual} bytes, expected {expected}.");
        }
    }

    public bool LeftButtonHeld
    {
        get
        {
            lock (_gate)
            {
                return (_held & LeftFlag) != 0;
            }
        }
    }

    public void MoveTo(int x, int y)
    {
        int left = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        int width = User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN);
        int height = User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN);

        if (width <= 1 || height <= 1)
        {
            return;
        }

        int nx = (int)Math.Round((double)(x - left) * 65535 / (width - 1));
        int ny = (int)Math.Round((double)(y - top) * 65535 / (height - 1));
        nx = Math.Clamp(nx, 0, 65535);
        ny = Math.Clamp(ny, 0, 65535);

        var input = MouseInput(User32.MOUSEEVENTF_MOVE | User32.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF_VIRTUALDESK, nx, ny);
        Send(input);
    }

    public void LeftDown() => Press(LeftFlag, User32.MOUSEEVENTF_LEFTDOWN);
    public void LeftUp() => Release(LeftFlag, User32.MOUSEEVENTF_LEFTUP);
    public void RightDown() => Press(RightFlag, User32.MOUSEEVENTF_RIGHTDOWN);
    public void RightUp() => Release(RightFlag, User32.MOUSEEVENTF_RIGHTUP);
    public void MiddleDown() => Press(MiddleFlag, User32.MOUSEEVENTF_MIDDLEDOWN);
    public void MiddleUp() => Release(MiddleFlag, User32.MOUSEEVENTF_MIDDLEUP);

    public void XButtonDown(MouseXButton button) => Press(XFlag(button), User32.MOUSEEVENTF_XDOWN, XData(button));

    public void XButtonUp(MouseXButton button) => Release(XFlag(button), User32.MOUSEEVENTF_XUP, XData(button));

    public void LeftClick() => Send(
        MouseInput(User32.MOUSEEVENTF_LEFTDOWN),
        MouseInput(User32.MOUSEEVENTF_LEFTUP));

    public void RightClick() => Send(
        MouseInput(User32.MOUSEEVENTF_RIGHTDOWN),
        MouseInput(User32.MOUSEEVENTF_RIGHTUP));

    public void MiddleClick() => Send(
        MouseInput(User32.MOUSEEVENTF_MIDDLEDOWN),
        MouseInput(User32.MOUSEEVENTF_MIDDLEUP));

    public void DoubleClick() => Send(
        MouseInput(User32.MOUSEEVENTF_LEFTDOWN),
        MouseInput(User32.MOUSEEVENTF_LEFTUP),
        MouseInput(User32.MOUSEEVENTF_LEFTDOWN),
        MouseInput(User32.MOUSEEVENTF_LEFTUP));

    public void XButtonClick(MouseXButton button) => Send(
        MouseInput(User32.MOUSEEVENTF_XDOWN, mouseData: XData(button)),
        MouseInput(User32.MOUSEEVENTF_XUP, mouseData: XData(button)));

    public void Wheel(int delta) =>
        Send(MouseInput(User32.MOUSEEVENTF_WHEEL, mouseData: unchecked((uint)delta)));

    public void HorizontalWheel(int delta) =>
        Send(MouseInput(User32.MOUSEEVENTF_HWHEEL, mouseData: unchecked((uint)delta)));

    private void Press(int flag, uint mouseFlag, uint mouseData = 0)
    {
        lock (_gate)
        {
            if ((_held & flag) != 0)
            {
                return;
            }

            _held |= flag;
        }

        Send(MouseInput(mouseFlag, mouseData: mouseData));
    }

    private void Release(int flag, uint mouseFlag, uint mouseData = 0)
    {
        bool wasHeld;
        lock (_gate)
        {
            wasHeld = (_held & flag) != 0;
            if (wasHeld)
            {
                _held &= ~flag;
            }
        }

        // Do not emit an orphan button-up for a button we never pressed.
        if (wasHeld)
        {
            Send(MouseInput(mouseFlag, mouseData: mouseData));
        }
    }

    public void ReleaseAll()
    {
        var inputs = new List<INPUT>(5);

        lock (_gate)
        {
            if ((_held & LeftFlag) != 0)
            {
                inputs.Add(MouseInput(User32.MOUSEEVENTF_LEFTUP));
            }

            if ((_held & RightFlag) != 0)
            {
                inputs.Add(MouseInput(User32.MOUSEEVENTF_RIGHTUP));
            }

            if ((_held & MiddleFlag) != 0)
            {
                inputs.Add(MouseInput(User32.MOUSEEVENTF_MIDDLEUP));
            }

            if ((_held & X1Flag) != 0)
            {
                inputs.Add(MouseInput(User32.MOUSEEVENTF_XUP, mouseData: User32.XBUTTON1));
            }

            if ((_held & X2Flag) != 0)
            {
                inputs.Add(MouseInput(User32.MOUSEEVENTF_XUP, mouseData: User32.XBUTTON2));
            }

            _held = 0;
        }

        if (inputs.Count > 0)
        {
            Send(inputs.ToArray());
        }
    }

    private static int XFlag(MouseXButton button) => button == MouseXButton.X1 ? X1Flag : X2Flag;

    private static uint XData(MouseXButton button) => button == MouseXButton.X1 ? User32.XBUTTON1 : User32.XBUTTON2;

    private static INPUT MouseInput(uint flags, int dx = 0, int dy = 0, uint mouseData = 0) => new()
    {
        type = User32.INPUT_MOUSE,
        u = new INPUTUNION
        {
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = mouseData,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        uint sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            Debug.WriteLine($"[MouseOutput] SendInput sent {sent}/{inputs.Length}, error {Marshal.GetLastWin32Error()}");
        }
    }
}
