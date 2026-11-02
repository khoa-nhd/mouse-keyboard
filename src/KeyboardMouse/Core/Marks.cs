using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Core;

/// <summary>
/// Saved pointer marks (a-z) persisted to
/// <c>%LOCALAPPDATA%\KeyboardMouse\marks.json</c>.
///
/// The in-memory dictionary is updated immediately. Disk writing happens on a
/// small background thread so the keyboard hook callback never waits on I/O.
/// Dispose (or Flush) writes one final snapshot.
/// </summary>
public sealed class Marks : IMarkStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly Dictionary<char, Point> _marks = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _writer;
    private readonly string _path;
    private bool _dirty;
    private bool _disposed;

    public Marks(string? path = null)
    {
        _path = path ?? Path.Combine(Log.DirectoryPath, "marks.json");
        Load();

        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "KeyboardMouse.Marks"
        };
        _writer.Start();
    }

    public string FilePath => _path;

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
            if (_disposed)
            {
                return;
            }

            _marks[key] = position;
            _dirty = true;
            // Inside the lock: it cannot race with Dispose finishing after
            // _disposed was observed under the same lock.
            try
            {
                _wake.Set();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown race with an earlier Dispose; nothing to do.
            }
        }
    }

    /// <summary>Writes the current snapshot synchronously (tests and shutdown).</summary>
    public void Flush() => WriteSnapshot();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _wake.Set();
        // Dispose the event only if the writer really stopped: a stale writer
        // waiting on a disposed event throws and kills the process.
        if (_writer.Join(2000))
        {
            _wake.Dispose();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<Dictionary<string, int[]>>(json);
            if (data is null)
            {
                return;
            }

            foreach (var (name, xy) in data)
            {
                if (name.Length == 1 &&
                    MarkNames.TryNormalize(name[0], out char key) &&
                    xy is { Length: >= 2 })
                {
                    _marks[key] = new Point(xy[0], xy[1]);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not load marks", ex);
        }
    }

    private void WriteLoop()
    {
        while (true)
        {
            _wake.WaitOne();

            while (true)
            {
                lock (_gate)
                {
                    if (!_dirty)
                    {
                        break;
                    }

                    _dirty = false;
                }

                WriteSnapshot();
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }
        }
    }

    private void WriteSnapshot()
    {
        Dictionary<char, Point> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<char, Point>(_marks);
        }

        lock (_writeGate)
        {
            try
            {
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var data = snapshot.ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => new[] { pair.Value.X, pair.Value.Y });

                string json = JsonSerializer.Serialize(data, JsonOptions);
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                // Marks are a convenience; never let a read-only disk stop the app.
                Debug.WriteLine($"[Marks] Could not save: {ex.Message}");
                Log.Error("Could not save marks", ex);
            }
        }
    }
}
