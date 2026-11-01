using System.Runtime.InteropServices;

namespace KeyboardMouse.Platform;

public enum ShortcutLocation
{
    Desktop,
    StartMenu
}

/// <summary>Creates .lnk shortcuts to this exe (per user, no elevation).</summary>
public static class Shortcuts
{
    private const string FileName = "KeyboardMouse.lnk";

    public static string PathFor(ShortcutLocation location) => Path.Combine(
        Environment.GetFolderPath(location == ShortcutLocation.Desktop
            ? Environment.SpecialFolder.DesktopDirectory
            : Environment.SpecialFolder.Programs),
        FileName);

    public static bool Exists(ShortcutLocation location)
    {
        try
        {
            return File.Exists(PathFor(location));
        }
        catch
        {
            return false;
        }
    }

    public static bool Create(ShortcutLocation location)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        object? shell = null;
        object? link = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            string path = PathFor(location);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            shell = Activator.CreateInstance(shellType);
            dynamic shellApi = shell!;
            link = shellApi.CreateShortcut(path);
            dynamic linkApi = link!;
            linkApi.TargetPath = exe;
            linkApi.WorkingDirectory = Path.GetDirectoryName(exe);
            linkApi.IconLocation = exe + ",0";
            linkApi.Description = "Control the mouse from the keyboard";
            linkApi.Save();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not create the {location} shortcut", ex);
            return false;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
