using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>Saved pointer positions, addressed by a single letter a-z.</summary>
public interface IMarkStore
{
    bool TryGet(char name, out Point position);

    void Set(char name, Point position);
}

/// <summary>Name rules shared by every mark store.</summary>
public static class MarkNames
{
    public static bool TryNormalize(char name, out char normalized)
    {
        normalized = char.ToLowerInvariant(name);
        return normalized is >= 'a' and <= 'z';
    }
}
