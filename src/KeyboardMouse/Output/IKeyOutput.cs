using KeyboardMouse.Input;

namespace KeyboardMouse.Output;

/// <summary>
/// Synthesized keyboard output (Page Up/Down now, editing shortcuts later).
/// Injected events are ignored by our own hook, so they never loop back.
/// </summary>
public interface IKeyOutput
{
    /// <summary>Press and release one physical key.</summary>
    void SendKey(PhysicalKey key);

    /// <summary>Send Ctrl+key (or just key) as one batch.</summary>
    void SendChord(bool ctrl, PhysicalKey key);
}

/// <summary>No-op implementation used by tests that do not care about key output.</summary>
internal sealed class NullKeyOutput : IKeyOutput
{
    public static readonly NullKeyOutput Instance = new();

    public void SendKey(PhysicalKey key)
    {
    }

    public void SendChord(bool ctrl, PhysicalKey key)
    {
    }
}
