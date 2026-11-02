using System.IO;
using KeyboardMouse.Config;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;
using KeyboardMouse.Platform;
using Xunit;

namespace KeyboardMouse.Tests;

/// <summary>
/// Auto-off would kill the mode on its first poll whenever the router had been
/// idle longer than the timeout before the mode was turned on, because the
/// idle clock was stamped at construction instead of at mode start.
/// </summary>
public class AutoOffModeStartRegressionTests
{
    [Fact]
    public void LongIdleBeforeActivation_DoesNotTurnTheModeOffInstantly()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(10);

        h.Now = 60_000; // the app was idle for a minute before activation
        h.TapCaps();
        Assert.True(h.State.MouseModeOn);

        h.Now += 9_999;
        h.Tick();
        Assert.True(h.State.MouseModeOn); // the idle clock restarted at activation

        // The boundary is ">" so exactly autoOffSeconds is still on.
        h.Now += 1;
        h.Tick();
        Assert.True(h.State.MouseModeOn);

        h.Now += 1;
        h.Tick();
        Assert.False(h.State.MouseModeOn);
        Assert.Contains("release-all", h.Mouse.Calls);
    }

    [Fact]
    public void HoldingCaps_ThenIdling_TimesOutFromTheHold()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(10);

        h.Down(PhysicalKey.CapsLock); // momentary activation while held
        h.Now += 300;                 // past the tap timeout: hold promoted
        h.Tick();
        Assert.True(h.State.MouseModeOn);

        h.Now += 10_001;
        h.Tick();
        Assert.False(h.State.MouseModeOn);
    }
}

/// <summary>
/// The chord batch used the left shift/alt scan codes for both sides, so a
/// right-side Shift/Alt hold stayed stuck and chords arrived modified.
/// </summary>
public class KeyOutputChordTests
{
    [Fact]
    public void RightSideHolds_AreReleasedWithTheirOwnScanCodes()
    {
        var inputs = KeyOutput.ChordInputs(
            ctrl: true,
            scan: 0x2E, // C
            extended: false,
            leftShiftDown: false,
            rightShiftDown: true,
            leftAltDown: false,
            rightAltDown: true);

        Assert.Equal(
            new (ushort Scan, bool Extended, bool Up)[]
            {
                (0x36, false, true), // right Shift up (own code, not extended)
                (0x38, true, true),  // right Alt up (extended)
                (0x1D, false, false), // left Ctrl down
                (0x2E, false, false), // C down
                (0x2E, false, true),  // C up
                (0x1D, false, true),  // left Ctrl up
                (0x38, true, false),  // right Alt back down (extended)
                (0x36, false, false), // right Shift back down
            },
            inputs);
    }

    [Fact]
    public void LeftSideHolds_KeepTheClassicNonExtendedCodes()
    {
        var inputs = KeyOutput.ChordInputs(
            ctrl: true,
            scan: 0x2E,
            extended: false,
            leftShiftDown: true,
            rightShiftDown: false,
            leftAltDown: true,
            rightAltDown: false);

        Assert.Equal(
            new (ushort Scan, bool Extended, bool Up)[]
            {
                (0x2A, false, true),  // left Shift up
                (0x38, false, true),  // left Alt up (not extended)
                (0x1D, false, false), // Ctrl down
                (0x2E, false, false), // C down
                (0x2E, false, true),  // C up
                (0x1D, false, true),  // Ctrl up
                (0x38, false, false), // left Alt back down
                (0x2A, false, false), // left Shift back down
            },
            inputs);
    }

    [Fact]
    public void NoSideHeld_TheChordIsJustCtrlKeyEvent()
    {
        var inputs = KeyOutput.ChordInputs(
            ctrl: true,
            scan: 0x2E,
            extended: false,
            leftShiftDown: false,
            rightShiftDown: false,
            leftAltDown: false,
            rightAltDown: false);

        Assert.Equal(
            new (ushort Scan, bool Extended, bool)[]
            {
                (0x1D, false, false),
                (0x2E, false, false),
                (0x2E, false, true),
                (0x1D, false, true),
            },
            inputs);
    }
}

/// <summary>A fourth version segment used to be ignored when comparing releases.</summary>
public class VersionCompareRegressionTests
{
    [Fact]
    public void Revision_BreaksTies()
    {
        Assert.True(UpdateService.Compare(new Version(0, 4, 0, 1), new Version(0, 4, 0)) > 0);
        Assert.True(UpdateService.Compare(new Version(0, 4, 0), new Version(0, 4, 0, 1)) < 0);
        Assert.Equal(0, UpdateService.Compare(new Version(0, 4, 0), new Version(0, 4, 0, 0)));
        Assert.Equal(0, UpdateService.Compare(new Version(0, 4, 4), new Version(0, 4, 4, 0)));
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "km-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var store = new SettingsStore(SettingsPath);
        var loaded = store.Load();

        Assert.NotNull(store.LastError);
        Assert.Equal(0, loaded.Activation.AutoOffSeconds); // defaults, not a crash
        Assert.EndsWith(".json", SettingsPath);

        string? backup = Directory.GetFiles(_directory, "settings.json.corrupt-*")
            .Select(f => Path.GetFileName(f))
            .FirstOrDefault();
        Assert.NotNull(backup);
        Assert.Contains("{ this is not json", File.ReadAllText(Path.Combine(_directory, backup!)));
    }

    [Fact]
    public void Save_IsAtomic_RoundTripsAndLeavesNoTemp()
    {
        var store = new SettingsStore(SettingsPath);
        store.Load();
        store.Current.Activation.AutoOffSeconds = 30;
        store.Save(store.Current);

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));

        var reloaded = new SettingsStore(SettingsPath).Load();
        Assert.Equal(30, reloaded.Activation.AutoOffSeconds);
    }

    [Fact]
    public void Save_OverAnExistingFile_ReplacesItAtomically()
    {
        Directory.CreateDirectory(_directory);

        var first = new SettingsStore(SettingsPath);
        first.Load();
        first.Current.Activation.AutoOffSeconds = 5;
        first.Save(first.Current);

        first.Current.Activation.AutoOffSeconds = 45;
        first.Save(first.Current);
        Assert.False(File.Exists(SettingsPath + ".tmp"));

        var second = new SettingsStore(SettingsPath).Load();
        Assert.Equal(45, second.Activation.AutoOffSeconds);
    }
}

/// <summary>Set after Dispose used to reach the disposed wake event.</summary>
public class MarksDisposeRegressionTests
{
    [Fact]
    public void Set_AfterDispose_IsSilentlyIgnored()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "km-marks-" + Guid.NewGuid().ToString("N") + ".json");
        var marks = new Marks(path);
        marks.Set('a', new Point(11, 22));
        marks.Dispose();

        marks.Set('b', new Point(33, 44)); // must not throw

        Assert.False(marks.TryGet('b', out _));
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
