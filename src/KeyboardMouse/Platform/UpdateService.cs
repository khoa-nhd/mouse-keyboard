using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyboardMouse.Platform;

/// <summary>A published release that is newer than the running build.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    Uri AssetUri,
    Uri ChecksumUri,
    long SizeBytes,
    string PageUrl);

/// <summary>Result of a check: either a newer release, or nothing (with the reason).</summary>
public sealed record UpdateCheckResult(ReleaseInfo? Release, string? Error)
{
    public bool UpdateAvailable => Release is not null;
}

/// <summary>A verified exe waiting to replace the running one.</summary>
public sealed record PreparedUpdate(string StagedExePath, string TargetExePath);

/// <summary>
/// Opt-in update check against GitHub Releases. Nothing but the public release
/// JSON is requested, and no information about the machine or the user is sent.
/// Downloads are only accepted from github.com over HTTPS and must match the
/// SHA-256 published with the release before anything is extracted.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "khoa-nhd/mouse-keyboard";
    public const string ExeEntryName = "KeyboardMouse.exe";

    private const long MaxDownloadBytes = 400L * 1024 * 1024;

    private static readonly Uri LatestRelease =
        new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static Version CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Returns the exe that an update should replace.</summary>
    public static string CurrentExePath
    {
        get
        {
            string? process = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(process) &&
                !Path.GetFileName(process).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return process;
            }

            // Hosted (or launched through the dotnet muxer): the app folder is
            // the only reliable answer, since a single-file app has no Location.
            return Path.Combine(AppContext.BaseDirectory, "KeyboardMouse.exe");
        }
    }

    public static string StagingDirectory => Path.Combine(Log.DirectoryPath, "update");

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"KeyboardMouse/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(TimeSpan.FromSeconds(30));
            using var response = await client.GetAsync(LatestRelease, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                return new UpdateCheckResult(null, "rate-limited");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(null, "no-releases");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(null, $"http-{(int)response.StatusCode}");
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseCheck(json, CurrentVersion);
        }
        catch (OperationCanceledException oce) when (oce.InnerException is TimeoutException)
        {
            // The HttpClient timeout surfaces as a cancellation: report it as
            // "offline" so users do not see "the check was cancelled".
            return new UpdateCheckResult(null, "offline");
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(null, "cancelled");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            Log.Info($"Update check failed: {ex.Message}");
            return new UpdateCheckResult(null, "offline");
        }
    }

    /// <summary>
    /// Pure JSON handling, so the release rules can be tested without network.
    /// Returns a release only when it is a newer, stable, fully published one.
    /// </summary>
    public static UpdateCheckResult ParseCheck(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
        {
            return new UpdateCheckResult(null, "draft");
        }

        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
        {
            return new UpdateCheckResult(null, "prerelease");
        }

        if (!root.TryGetProperty("tag_name", out var tagElement) ||
            tagElement.GetString() is not { Length: > 0 } tag ||
            !TryParseTag(tag, out var version))
        {
            return new UpdateCheckResult(null, "bad-tag");
        }

        if (Compare(version, current) <= 0)
        {
            return new UpdateCheckResult(null, null);
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return new UpdateCheckResult(null, "no-assets");
        }

        string? pageUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;

        Uri? assetUri = null;
        Uri? checksumUri = null;
        long size = 0;
        string? assetName = null;

        foreach (var asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            string? url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrEmpty(url) || !IsTrustedUri(url, out var uri))
            {
                continue;
            }

            if (assetName is null &&
                name.StartsWith("KeyboardMouse-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
            {
                assetName = name;
                assetUri = uri;
                size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out long bytes) ? bytes : 0;
            }
            else if (assetName is not null &&
                     name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksumUri = uri;
            }
        }

        // The checksum may also appear after the zip; look again if needed.
        if (assetName is not null && checksumUri is null)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                if (name is not null &&
                    url is not null &&
                    name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase) &&
                    IsTrustedUri(url, out var uri))
                {
                    checksumUri = uri;
                    break;
                }
            }
        }

        if (assetUri is null || checksumUri is null)
        {
            return new UpdateCheckResult(null, "missing-assets");
        }

        if (size > MaxDownloadBytes)
        {
            return new UpdateCheckResult(null, "too-large");
        }

        return new UpdateCheckResult(
            new ReleaseInfo(version, tag, assetUri, checksumUri, size, pageUrl ?? string.Empty),
            null);
    }

    /// <summary>Downloads, verifies the SHA-256 and extracts the exe next to the running one.</summary>
    public async Task<PreparedUpdate> DownloadAsync(
        ReleaseInfo release,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StagingDirectory);

        string zipPath = Path.Combine(StagingDirectory, Path.GetFileName(release.AssetUri.LocalPath));
        try
        {
            using (var client = CreateClient(TimeSpan.FromMinutes(15)))
            using (var response = await client
                       .GetAsync(release.AssetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? release.SizeBytes;
                if (total > MaxDownloadBytes)
                {
                    throw new InvalidOperationException("The update is larger than expected.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long written = 0;
                int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;

                    if (written > MaxDownloadBytes)
                    {
                        throw new InvalidOperationException("The update is larger than expected.");
                    }

                    if (total > 0)
                    {
                        int percent = (int)Math.Min(100, written * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(percent);
                        }
                    }
                }
            }

            string expected = await FetchChecksumAsync(release, cancellationToken).ConfigureAwait(false);
            string actual = ComputeSha256(zipPath);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(zipPath);
                throw new InvalidDataException("The downloaded update did not match its published checksum.");
            }

            string staged = ExtractExe(zipPath);
            TryDelete(zipPath);
            return new PreparedUpdate(staged, CurrentExePath);
        }
        catch
        {
            // A cancelled or failed download must not leave a partial zip in
            // the staging folder.
            TryDelete(zipPath);
            throw;
        }
    }

    private async Task<string> FetchChecksumAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        using var client = CreateClient(TimeSpan.FromSeconds(60));
        string text = await client.GetStringAsync(release.ChecksumUri, cancellationToken).ConfigureAwait(false);
        return ParseChecksum(text);
    }

    /// <summary>Accepts "hash", "hash  file.zip" and the sha256sum two-column form.</summary>
    public static string ParseChecksum(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 64)
            {
                continue;
            }

            string candidate = trimmed[..64];
            if (candidate.All(Uri.IsHexDigit))
            {
                return candidate.ToLowerInvariant();
            }
        }

        throw new InvalidDataException("The release checksum file was not readable.");
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private string ExtractExe(string zipPath)
    {
        string targetExe = CurrentExePath;
        string? directory = string.IsNullOrEmpty(targetExe) ? null : Path.GetDirectoryName(targetExe);
        return ExtractExe(zipPath, directory ?? StagingDirectory, StagingDirectory);
    }

    /// <summary>
    /// Pulls only KeyboardMouse.exe out of the archive. The entry name is
    /// matched exactly (no path separators), so a crafted zip cannot write
    /// anywhere else, and the file is staged in the app folder when that folder
    /// is writable, otherwise in the update folder.
    /// </summary>
    public static string ExtractExe(string zipPath, string preferredDirectory, string fallbackDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals(ExeEntryName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new InvalidDataException("The update archive does not contain KeyboardMouse.exe.");
        }

        string stagedInPlace = Path.Combine(preferredDirectory, ExeEntryName + ".new");
        try
        {
            Directory.CreateDirectory(preferredDirectory);
            entry.ExtractToFile(stagedInPlace, overwrite: true);
            return stagedInPlace;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Log.Info($"Could not stage the update in {preferredDirectory} ({ex.GetType().Name}); using the update folder.");
        }

        Directory.CreateDirectory(fallbackDirectory);
        string staged = Path.Combine(fallbackDirectory, ExeEntryName + ".new");
        entry.ExtractToFile(staged, overwrite: true);
        return staged;
    }

    /// <summary>
    /// Starts a tiny helper that waits for this process to exit, swaps the exe,
    /// and starts the new build. Windows locks the running exe, so the swap has
    /// to happen after we are gone. If the swap itself fails, the helper still
    /// starts whatever is at the target path (the old build), so the app is
    /// never simply gone after "Install".
    /// </summary>
    public static bool InstallOnExit(PreparedUpdate update, IReadOnlyList<string> restartArguments)
    {
        try
        {
            if (!IsDirectoryWritable(Path.GetDirectoryName(update.TargetExePath)))
            {
                Log.Error("The install folder is not writable, so the update cannot replace the exe.");
                return false;
            }

            Directory.CreateDirectory(StagingDirectory);

            string scriptPath = Path.Combine(StagingDirectory, "apply-update.ps1");
            string logPath = Path.Combine(StagingDirectory, "update.log");
            string arguments = restartArguments.Count == 0
                ? "@()"
                : "@(" + string.Join(", ", restartArguments.Select(PsLiteral)) + ")";
            string nl = Environment.NewLine;

            string script =
                "$ErrorActionPreference = 'Stop'" + nl +
                "$targetPid = " + Environment.ProcessId + nl +
                "Wait-Process -Id $targetPid -ErrorAction SilentlyContinue" + nl +
                "Start-Sleep -Milliseconds 400" + nl +
                "try {" + nl +
                "  Move-Item -LiteralPath " + PsLiteral(update.StagedExePath) +
                " -Destination " + PsLiteral(update.TargetExePath) + " -Force" + nl +
                "} catch {" + nl +
                "  ((Get-Date -Format o) + ' swap failed: ' + $_) | Out-File -LiteralPath " + PsLiteral(logPath) +
                " -Append -Encoding utf8" + nl +
                "}" + nl +
                "if (Test-Path -LiteralPath " + PsLiteral(update.TargetExePath) + ") {" + nl +
                "  try {" + nl +
                "    Start-Process -FilePath " + PsLiteral(update.TargetExePath) +
                " -ArgumentList " + arguments + nl +
                "  } catch {" + nl +
                "    ((Get-Date -Format o) + ' start failed: ' + $_) | Out-File -LiteralPath " + PsLiteral(logPath) +
                " -Append -Encoding utf8" + nl +
                "  }" + nl +
                "} else {" + nl +
                "  ((Get-Date -Format o) + ' app is missing after the swap; nothing restarted')" +
                " | Out-File -LiteralPath " + PsLiteral(logPath) + " -Append -Encoding utf8" + nl +
                "}" + nl +
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue" + nl;

            File.WriteAllText(scriptPath, script);

            string powershell = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            Process.Start(new ProcessStartInfo(
                powershell,
                $@"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{scriptPath}""")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = StagingDirectory
            });

            Log.Info($"Update staged: {update.StagedExePath} -> {update.TargetExePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the update helper", ex);
            return false;
        }
    }

    /// <summary>
    /// Probes the folder the way the helper would need to write into it: the
    /// app exits to apply an update, and discovering "Program Files" is not
    /// writable only after it is gone is far too late.
    /// </summary>
    private static bool IsDirectoryWritable(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, "KeyboardMouse-write-test.tmp");
            File.WriteAllText(probe, "writable");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"Folder {directory} is not writable ({ex.GetType().Name}).");
            return false;
        }
    }

    /// <summary>Only HTTPS downloads from GitHub's own hosts are accepted.</summary>
    public static bool IsTrustedUri(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        string host = parsed.Host;
        bool trusted = host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
                       host.Equals("githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

        if (!trusted)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool TryParseTag(string tag, out Version version)
    {
        string trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int dash = trimmed.IndexOf('-');
        if (dash > 0)
        {
            trimmed = trimmed[..dash];
        }

        return Version.TryParse(trimmed, out version!);
    }

    /// <summary>
    /// Compares major.minor.build, then revision, so 0.4.0.0 and 0.4.0 are the
    /// same release while 0.4.0.1 is genuinely newer.
    /// </summary>
    public static int Compare(Version left, Version right)
    {
        int byBase = new Version(left.Major, left.Minor, Math.Max(left.Build, 0))
            .CompareTo(new Version(right.Major, right.Minor, Math.Max(right.Build, 0)));
        if (byBase != 0)
        {
            return byBase;
        }

        return Math.Max(left.Revision, 0).CompareTo(Math.Max(right.Revision, 0));
    }

    /// <summary>PowerShell single-quoted literal.</summary>
    private static string PsLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A leftover download is harmless.
        }
    }
}
