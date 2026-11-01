using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>Mark store used by tests and as a safe fallback when no file is needed.</summary>
public sealed class InMemoryMarkStore : IMarkStore
{
    private readonly object _gate = new();
    private readonly Dictionary<char, Point> _marks = new();

    public IReadOnlyDictionary<char, Point> All
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<char, Point>(_marks);
            }
        }
    }

    public bool TryGet(char name, out Point position)
    {
        lock (_gate)
        {
            if (MarkNames.TryNormalize(name, out char key) && _marks.TryGetValue(key, out position))
            {
                return true;
            }
        }

        position = default;
        return false;
    }

    public void Set(char name, Point position)
    {
        if (!MarkNames.TryNormalize(name, out char key))
        {
            return;
        }

        lock (_gate)
        {
            _marks[key] = position;
        }
    }
}
