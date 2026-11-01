namespace KeyboardMouse.Platform;

/// <summary>
/// Tiny rolling log. Never pass key contents in here: the program must never
/// log keystrokes.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyboardMouse");

    public static string FilePath => Path.Combine(DirectoryPath, "log.txt");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                RotateIfNeeded();

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take down the app.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        string old = Path.Combine(DirectoryPath, "log.old.txt");
        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(FilePath, old);
        }
        catch
        {
            // If rotation fails, truncate instead of growing forever.
            File.WriteAllText(FilePath, string.Empty);
        }
    }
}
