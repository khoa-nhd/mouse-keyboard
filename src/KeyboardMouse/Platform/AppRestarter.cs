using System.Diagnostics;
using System.Text;

namespace KeyboardMouse.Platform;

/// <summary>
/// Relaunches the app from the same exe. Windows keeps the exe locked while it
/// runs (and the app refuses a second instance), so a tiny PowerShell helper
/// waits for this process to exit, then starts it again.
/// </summary>
public static class AppRestarter
{
    /// <summary>Queues the relaunch. Returns false when the exe cannot be found.</summary>
    public static bool Schedule()
    {
        string exe = UpdateService.CurrentExePath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            return false;
        }

        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "KeyboardMouse");
            Directory.CreateDirectory(directory);
            string scriptPath = Path.Combine(directory, "restart.ps1");
            string workingDirectory = Path.GetDirectoryName(exe) ?? directory;

            File.WriteAllText(scriptPath, BuildScript(Environment.ProcessId, exe, workingDirectory), new UTF8Encoding(false));

            string powershell = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            var startInfo = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo);
            Log.Info(process is null ? "Restart scheduled without a helper process." : "Restart scheduled.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not schedule a restart", ex);
            return false;
        }
    }

    /// <summary>The helper script: wait for this process, wait a beat, start the exe again.</summary>
    internal static string BuildScript(int processId, string exe, string workingDirectory)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        script.AppendLine($"Wait-Process -Id {processId}");
        script.AppendLine("Start-Sleep -Milliseconds 300");
        script.AppendLine(
            $"Start-Process -FilePath '{Escape(exe)}' -WorkingDirectory '{Escape(workingDirectory)}'");
        script.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force");
        return script.ToString();
    }

    private static string Escape(string path) => path.Replace("'", "''");
}
