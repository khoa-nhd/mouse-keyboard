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
            Current = new Settings();
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
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(Path, json);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error($"Could not write settings to {Path}", ex);
            return false;
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
