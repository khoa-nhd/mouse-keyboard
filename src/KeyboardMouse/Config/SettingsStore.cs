using System.Text.Json;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Config;

/// <summary>
/// Loads and saves settings.json. Portable mode: if settings.json sits next to
/// the exe, use it; otherwise the file lives in %APPDATA%\KeyboardMouse.
/// Invalid files never crash the app: the error is logged and defaults stay.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public SettingsStore()
    {
        Path = ResolvePath();
    }

    /// <summary>Test seam: a store pinned to an explicit settings.json path.</summary>
    public SettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public Settings Current { get; private set; } = new();

    public string? LastError { get; private set; }

    public Settings Load()
    {
        LastError = null;

        if (!File.Exists(Path))
        {
            Current = new Settings();
            Current.Normalize();
            TrySave();
            return Current;
        }

        try
        {
            string json = File.ReadAllText(Path);
            Current = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
            Current.Normalize();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error($"Could not read settings from {Path}", ex);
            BackUpCorruptFile();
            Current = new Settings();
            Current.Normalize();
        }

        return Current;
    }

    public void Save(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = settings;
        TrySave();
    }

    private bool TrySave()
    {
        string? tempPath = null;
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a sibling temp file first and swap it in, so an app that
            // dies mid-write leaves the previous settings intact.
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            tempPath = Path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(Path))
            {
                File.Replace(tempPath, Path, null);
                tempPath = null;
            }
            else
            {
                File.Move(tempPath, Path);
                tempPath = null;
            }

            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error($"Could not write settings to {Path}", ex);
            TryDelete(tempPath);
            return false;
        }
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>Keeps unreadable settings for recovery instead of silently replacing them.</summary>
    private void BackUpCorruptFile()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(Path, $"{Path}.corrupt-{stamp}", overwrite: false);
            Log.Info($"The unreadable settings file was copied to {Path}.corrupt-{stamp}.");
        }
        catch (Exception ex)
        {
            Log.Info($"Could not back up the unreadable settings file: {ex.GetType().Name}");
        }
    }

    private static string ResolvePath()
    {
        string exeDirectory = AppContext.BaseDirectory;
        string portable = System.IO.Path.Combine(exeDirectory, "settings.json");
        if (File.Exists(portable))
        {
            return portable;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "KeyboardMouse", "settings.json");
    }
}
