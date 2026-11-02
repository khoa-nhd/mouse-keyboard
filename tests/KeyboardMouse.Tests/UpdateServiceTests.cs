using System.IO.Compression;
using KeyboardMouse.Platform;

namespace KeyboardMouse.Tests;

public class UpdateServiceTests
{
    private const string Zip = "KeyboardMouse-v0.4.0-win-x64.zip";

    private static string ReleaseJson(
        string tag = "v0.4.0",
        bool draft = false,
        bool prerelease = false,
        string? zipUrl = "https://github.com/khoa-nhd/mouse-keyboard/releases/download/v0.4.0/KeyboardMouse-v0.4.0-win-x64.zip",
        string? checksumUrl = "https://github.com/khoa-nhd/mouse-keyboard/releases/download/v0.4.0/KeyboardMouse-v0.4.0-win-x64.zip.sha256")
    {
        var assets = new List<string>();
        if (zipUrl is not null)
        {
            assets.Add($$"""{ "name": "{{Zip}}", "size": 150000000, "browser_download_url": "{{zipUrl}}" }""");
        }

        if (checksumUrl is not null)
        {
            assets.Add($$"""{ "name": "{{Zip}}.sha256", "size": 80, "browser_download_url": "{{checksumUrl}}" }""");
        }

        return $$"""
            {
              "tag_name": "{{tag}}",
              "draft": {{(draft ? "true" : "false")}},
              "prerelease": {{(prerelease ? "true" : "false")}},
              "html_url": "https://github.com/khoa-nhd/mouse-keyboard/releases/tag/{{tag}}",
              "assets": [{{string.Join(",", assets)}}]
            }
            """;
    }

    [Fact]
    public void ParseCheck_FindsNewerReleaseWithChecksum()
    {
        var result = UpdateService.ParseCheck(ReleaseJson(), new Version(0, 3, 0));

        Assert.True(result.UpdateAvailable);
        var release = result.Release!;
        Assert.Equal("v0.4.0", release.Tag);
        Assert.Equal(new Version(0, 4, 0), release.Version);
        Assert.Equal(150000000, release.SizeBytes);
        Assert.Equal(Zip, Path.GetFileName(release.AssetUri.LocalPath));
        Assert.EndsWith(".sha256", release.ChecksumUri.ToString());
    }

    [Fact]
    public void ParseCheck_IgnoresSameOrOlderVersion()
    {
        Assert.False(UpdateService.ParseCheck(ReleaseJson(), new Version(0, 4, 0)).UpdateAvailable);
        Assert.False(UpdateService.ParseCheck(ReleaseJson(), new Version(0, 4, 0, 0)).UpdateAvailable);
        Assert.False(UpdateService.ParseCheck(ReleaseJson(), new Version(0, 5, 0)).UpdateAvailable);
    }

    [Fact]
    public void ParseCheck_SkipsDraftAndPrerelease()
    {
        Assert.False(UpdateService.ParseCheck(ReleaseJson(draft: true), new Version(0, 3, 0)).UpdateAvailable);
        Assert.False(UpdateService.ParseCheck(ReleaseJson(prerelease: true), new Version(0, 3, 0)).UpdateAvailable);
    }

    [Fact]
    public void ParseCheck_RequiresTheChecksumAsset()
    {
        var result = UpdateService.ParseCheck(ReleaseJson(checksumUrl: null), new Version(0, 3, 0));

        Assert.False(result.UpdateAvailable);
        Assert.Equal("missing-assets", result.Error);
    }

    [Theory]
    [InlineData("http://github.com/khoa-nhd/mouse-keyboard/releases/download/v0.4.0/x.zip")]
    [InlineData("https://example.com/KeyboardMouse-v0.4.0-win-x64.zip")]
    [InlineData("https://github.com.evil.com/KeyboardMouse-v0.4.0-win-x64.zip")]
    public void ParseCheck_RejectsDownloadsFromUntrustedHosts(string url)
    {
        var result = UpdateService.ParseCheck(ReleaseJson(zipUrl: url), new Version(0, 3, 0));

        Assert.False(result.UpdateAvailable);
        Assert.Equal("missing-assets", result.Error);
    }

    [Theory]
    [InlineData("https://github.com/x/y.zip", true)]
    [InlineData("https://objects.githubusercontent.com/x/y.zip", true)]
    [InlineData("http://github.com/x/y.zip", false)]
    [InlineData("https://raw.githubusercontent.com.evil.com/y.zip", false)]
    [InlineData("https://evil.com/y.zip", false)]
    [InlineData("not a url", false)]
    public void IsTrustedUri_OnlyAllowsHttpsFromGitHub(string url, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsTrustedUri(url, out var uri));
        if (expected)
        {
            Assert.NotNull(uri);
        }
    }

    [Theory]
    [InlineData("v0.4.0", 0, 4, 0)]
    [InlineData("0.4.0", 0, 4, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("v0.4.0-beta.1", 0, 4, 0)]
    public void TryParseTag_ReadsVersions(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateService.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("vX.Y")]
    public void TryParseTag_RejectsJunk(string tag)
    {
        Assert.False(UpdateService.TryParseTag(tag, out _));
    }

    [Fact]
    public void ParseChecksum_AcceptsCommonFormats()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        Assert.Equal(hash, UpdateService.ParseChecksum(hash));
        Assert.Equal(hash, UpdateService.ParseChecksum($"{hash}  {Zip}\n"));
        Assert.Equal(hash, UpdateService.ParseChecksum("ignored line\n" + hash.ToUpperInvariant() + " *" + Zip));
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseChecksum("nope"));
    }

    [Fact]
    public void Compare_IgnoresUnsetBuildAndRevision()
    {
        Assert.Equal(0, UpdateService.Compare(new Version(0, 4, 0), new Version(0, 4, 0, 0)));
        Assert.True(UpdateService.Compare(new Version(0, 4, 1), new Version(0, 4, 0)) > 0);
        Assert.True(UpdateService.Compare(new Version(0, 3, 0), new Version(0, 4, 0)) < 0);
    }

    [Fact]
    public void ExtractExe_WritesOnlyTheProgramFromTheArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), "km-update-" + Guid.NewGuid().ToString("N"));
        string preferred = Path.Combine(root, "app");
        string fallback = Path.Combine(root, "staging");
        string zipPath = Path.Combine(root, "update.zip");

        try
        {
            Directory.CreateDirectory(root);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var exe = zip.CreateEntry("KeyboardMouse.exe");
                using (var stream = exe.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }

                var decoy = zip.CreateEntry("settings.json");
                using var decoyStream = decoy.Open();
                decoyStream.WriteByte(0x7B);
            }

            string staged = UpdateService.ExtractExe(zipPath, preferred, fallback);

            Assert.Equal(Path.Combine(preferred, "KeyboardMouse.exe.new"), staged);
            Assert.Equal(new byte[] { 0x4D, 0x5A }, File.ReadAllBytes(staged));
            Assert.False(File.Exists(Path.Combine(preferred, "settings.json")));
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public void ExtractExe_RejectsArchivesWithoutTheProgram()
    {
        string root = Path.Combine(Path.GetTempPath(), "km-update-" + Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(root, "update.zip");

        try
        {
            Directory.CreateDirectory(root);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("readme.txt");
                using var stream = entry.Open();
                stream.WriteByte(0x41);
            }

            Assert.Throws<InvalidDataException>(() =>
                UpdateService.ExtractExe(zipPath, Path.Combine(root, "app"), Path.Combine(root, "staging")));
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public void ComputeSha256_MatchesTheFileContents()
    {
        string path = Path.Combine(Path.GetTempPath(), "km-sha-" + Guid.NewGuid().ToString("N"));

        try
        {
            File.WriteAllText(path, "keyboardmouse");
            Assert.Equal("454100ec1864f5bb93391a741200ef62db5e80cf7176d0281fd41332fedd75a4", UpdateService.ComputeSha256(path));
        }
        finally
        {
            CleanUp(path);
        }
    }

    private static void CleanUp(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: the temp folder is disposable.
        }
    }

    [Fact]
    public void RestartScript_WaitsForThePidAndStartsTheExeAgain()
    {
        string exe = Path.Combine("C:", "Tools", "Mouse's", "KeyboardMouse.exe");
        string directory = Path.Combine("C:", "Tools", "Mouse's");

        string script = AppRestarter.BuildScript(4242, exe, directory);

        Assert.Contains("Wait-Process -Id 4242", script);
        Assert.Contains("-FilePath '" + exe.Replace("'", "''") + "'", script);
        Assert.Contains("-WorkingDirectory '" + directory.Replace("'", "''") + "'", script);
        Assert.Contains("Start-Sleep -Milliseconds 300", script);
    }
}
