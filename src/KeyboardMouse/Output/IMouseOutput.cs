namespace KeyboardMouse.Output;

public enum MouseXButton
{
    X1 = 1,
    X2 = 2
}

/// <summary>
/// All pointer output goes through this interface, which keeps controllers
/// testable with a fake.
/// </summary>
public interface IMouseOutput
{
    void MoveTo(int x, int y);

    void LeftDown();
    void LeftUp();
    void RightDown();
    void RightUp();
    void MiddleDown();
    void MiddleUp();

    void LeftClick();
    void RightClick();
    void MiddleClick();
    void DoubleClick();

    void XButtonDown(MouseXButton button);
    void XButtonUp(MouseXButton button);
    void XButtonClick(MouseXButton button);

    /// <summary>Positive = scroll up (one notch = 120).</summary>
    void Wheel(int delta);

    /// <summary>Positive = scroll right.</summary>
    void HorizontalWheel(int delta);

    /// <summary>Releases every button this instance is holding.</summary>
    void ReleaseAll();
}
