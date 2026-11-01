using Microsoft.Win32;

namespace KeyboardMouse.Platform;

/// <summary>
/// HKCU Run-key autostart (per user, no elevation). The entry passes
/// <see cref="BackgroundArgument"/> so a sign-in start stays silent in the
/// tray instead of opening the guide window.
/// </summary>
public static class Autostart
{
    public const string BackgroundArgument = "--background";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyboardMouse";

    /// <summary>
    /// Points an existing entry at this exe with the current arguments. Fixes
    /// entries written by older versions and copies that were moved.
    /// </summary>
    public static void Refresh()
    {
        if (IsEnabled())
        {
            SetEnabled(true);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{exe}\" {BackgroundArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
