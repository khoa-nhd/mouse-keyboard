using KeyboardMouse.Controllers;
using KeyboardMouse.Core;
using KeyboardMouse.Input;
using KeyboardMouse.Output;

namespace KeyboardMouse.Tests;

/// <summary>Editing extras: Y / P / Z send Ctrl+C / Ctrl+V / Ctrl+Z.</summary>
public class EditingExtrasTests
{
    [Fact]
    public void YThenPThenZ_SendTheCopyPasteUndoChords()
    {
        var h = new RouterHarness(editingExtras: true);
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.Y));
        Assert.True(h.Down(PhysicalKey.P));
        Assert.True(h.Down(PhysicalKey.Z));
        Assert.True(h.Up(PhysicalKey.Y));

        var expected = new[]
        {
            (true, PhysicalKey.C),
            (true, PhysicalKey.V),
            (true, PhysicalKey.Z)
        };
        Assert.Equal(expected, h.Keys.Chords);
    }

    [Fact]
    public void Disabled_YPassesThroughToTheApp()
    {
        var h = new RouterHarness(editingExtras: false);
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.Y));
        Assert.Empty(h.Keys.Chords);
    }

    [Fact]
    public void Repeat_DoesNotSendAnotherChord()
    {
        var h = new RouterHarness(editingExtras: true);
        h.TapCaps();

        h.Down(PhysicalKey.Y);
        h.Down(PhysicalKey.Y, repeat: true);

        Assert.Single(h.Keys.Chords);
    }
}

/// <summary>The toggle key itself is remappable (activation.toggleKey).</summary>
public class ToggleKeyTests
{
    [Fact]
    public void RemappedToggleKey_Taps()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.ScrollLock);

        h.Now = 0;
        Assert.True(h.Down(PhysicalKey.ScrollLock));
        h.Now += 50;
        Assert.True(h.Up(PhysicalKey.ScrollLock));
        Assert.True(h.State.MouseModeOn);

        h.Now += 1000;
        h.Down(PhysicalKey.ScrollLock);
        h.Now += 50;
        h.Up(PhysicalKey.ScrollLock);
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void RemappedToggleKey_HoldsAreMomentary()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.ScrollLock);

        h.Now = 0;
        Assert.True(h.Down(PhysicalKey.ScrollLock));
        h.Now += 250;
        h.Tick(); // hold becomes momentary mode
        Assert.True(h.State.MouseModeOn);

        Assert.True(h.Down(PhysicalKey.J));
        Assert.Contains(Direction.Left, h.Movement.Active);
        Assert.True(h.Up(PhysicalKey.J));

        Assert.True(h.Up(PhysicalKey.ScrollLock));
        Assert.False(h.State.MouseModeOn);
        Assert.Empty(h.Movement.Active);
    }

    [Fact]
    public void RemappedToggleKey_HoldDoesNotTurnOffAnAlreadyToggledMode()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.ScrollLock);

        h.Now = 0;
        h.Down(PhysicalKey.ScrollLock);
        h.Now += 50;
        h.Up(PhysicalKey.ScrollLock);
        Assert.True(h.State.MouseModeOn);

        h.Now += 1000;
        h.Down(PhysicalKey.ScrollLock);
        h.Now += 250;
        h.Tick();

        Assert.True(h.Up(PhysicalKey.ScrollLock));
        Assert.True(h.State.MouseModeOn);
    }

    [Fact]
    public void RemappedToggleKey_ReleasesCapsLockToTheApp()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.ScrollLock);

        // Caps Lock is no longer special: the app gets it untouched.
        Assert.False(h.Down(PhysicalKey.CapsLock));
        h.Up(PhysicalKey.CapsLock);
        h.Tick();
        Assert.False(h.State.MouseModeOn);
    }

    [Fact]
    public void ModifierKeys_CannotBecomeTheToggle()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.RightAlt);

        // Ignored: still Caps Lock.
        h.TapCaps();
        Assert.True(h.State.MouseModeOn);
        h.Now += 1000;
        h.TapCaps();
        Assert.False(h.State.MouseModeOn);

        // And Alt still works as a plain modifier.
        Assert.False(h.Down(PhysicalKey.RightAlt));
        Assert.False(h.Up(PhysicalKey.RightAlt));
    }

    [Fact]
    public void NoKeyIsPromotedWhileCapsIsBeingHeldForTheToggleKeyBound()
    {
        var h = new RouterHarness();
        h.Router.SetToggleKey(PhysicalKey.ScrollLock);

        h.Now = 0;
        h.Down(PhysicalKey.ScrollLock);

        // A second key while the toggle is held enters momentary mode.
        h.Now = 100;
        Assert.True(h.Down(PhysicalKey.J));
        Assert.True(h.State.MouseModeOn);
        Assert.Contains(Direction.Left, h.Movement.Active);

        h.Up(PhysicalKey.J);
        h.Up(PhysicalKey.ScrollLock);
        Assert.False(h.State.MouseModeOn);
    }
}

/// <summary>Every binding is remappable; duplicates are resolved deterministically.</summary>
public class RemapTests
{
    [Fact]
    public void AllBindings_FollowTheSettings()
    {
        var h = new RouterHarness(editingExtras: true);
        h.KeySettings.Up = "Y";
        h.KeySettings.LeftClick = "Semicolon";
        h.KeySettings.TurboLayer = "B";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        h.Now += 100;
        Assert.True(h.Down(PhysicalKey.Y));
        Assert.Contains(Direction.Up, h.Movement.Active);
        h.Up(PhysicalKey.Y);

        h.Down(PhysicalKey.Semicolon);
        h.Up(PhysicalKey.Semicolon);
        Assert.Equal(1, h.Mouse.Count("left-down"));

        h.Down(PhysicalKey.B);
        Assert.Same(h.Profiles.Turbo, h.Movement.MoveProfile);
        h.Up(PhysicalKey.B);
    }

    [Fact]
    public void OldDefaultKeys_PassThroughOnceRemapped()
    {
        var h = new RouterHarness(editingExtras: true);
        h.KeySettings.Up = "Y";
        h.KeySettings.LeftClick = "Semicolon";
        h.KeySettings.Copy = "Backtick";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        // I and Space are no longer bound: the app gets to use them.
        Assert.False(h.Down(PhysicalKey.I));
        Assert.False(h.Down(PhysicalKey.Space));
        h.Up(PhysicalKey.I);
        h.Up(PhysicalKey.Space);

        // The editing chord did not move with its old launcher key.
        Assert.Empty(h.Keys.Chords);
        Assert.True(h.Down(PhysicalKey.Backtick));
        Assert.Equal([(true, PhysicalKey.C)], h.Keys.Chords);
    }

    [Fact]
    public void DuplicateBindings_FirstBindingWins()
    {
        var h = new RouterHarness(editingExtras: true);
        // Hint would be bound after Left, so the movement key keeps the key
        // and Hint ends up unbound.
        h.KeySettings.Left = "Q";
        h.KeySettings.Hint = "Q";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        Assert.True(h.Down(PhysicalKey.Q));
        Assert.Contains(Direction.Left, h.Movement.Active);
        Assert.Equal(0, h.HintScanner.ScanCount);
        Assert.True(h.Up(PhysicalKey.Q));
    }

    [Fact]
    public void InvalidKeyName_FallsBackToTheDefault()
    {
        var h = new RouterHarness(editingExtras: true);
        h.KeySettings.DoubleClick = "NotARealKey";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        h.Down(PhysicalKey.R);
        Assert.Equal(1, h.Mouse.Count("double-click"));
    }

    [Fact]
    public void ModifierKeys_NeverBecomeBindings()
    {
        var h = new RouterHarness(editingExtras: true);
        // A settings mistake must not turn Ctrl into a swallow-everything
        // mouse button: modifiers always pass through untouched.
        h.KeySettings.RightClick = "LeftCtrl";
        h.Router.UpdateKeys(h.KeySettings);
        h.TapCaps();

        Assert.False(h.Down(PhysicalKey.LeftCtrl));
        Assert.False(h.Up(PhysicalKey.LeftCtrl));
        Assert.Empty(h.Mouse.Calls);
    }
}

/// <summary>Auto-off: leaving the mode after a configured idle time (0 = off).</summary>
public class AutoOffTests
{
    [Fact]
    public void Inactivity_TurnsTheModeOff()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(10);
        h.TapCaps();

        h.Now = 100;
        Assert.True(h.Down(PhysicalKey.J));
        Assert.True(h.Up(PhysicalKey.J)); // activity at t=100
        Assert.True(h.State.MouseModeOn);

        h.Now = 100 + 10_000; // exactly at the boundary: still on
        h.Tick();
        Assert.True(h.State.MouseModeOn);

        h.Now += 1;
        h.Tick();
        Assert.False(h.State.MouseModeOn);
        Assert.Contains("release-all", h.Mouse.Calls);
    }

    [Fact]
    public void KeyEvents_KeepTheModeAlive()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(10);
        h.TapCaps();

        for (int i = 0; i < 3; i++)
        {
            h.Now += 9_000;
            h.Down(PhysicalKey.J);
            h.Up(PhysicalKey.J);
            h.Tick();
            Assert.True(h.State.MouseModeOn);
        }
    }

    [Fact]
    public void Zero_DisablesAutoOff()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(10);
        h.TapCaps();
        h.Router.UpdateAutoOff(0);

        h.Now += 1_000_000;
        h.Tick();
        Assert.True(h.State.MouseModeOn);
    }

    [Fact]
    public void Waiting_LongEnough_TimesOutAgainAfterActivity()
    {
        var h = new RouterHarness();
        h.Router.UpdateAutoOff(10);
        h.TapCaps();

        h.Now = 100;
        h.Down(PhysicalKey.J);
        h.Up(PhysicalKey.J); // activity at t=100

        h.Now = 100 + 10_001;
        h.Tick();
        Assert.False(h.State.MouseModeOn);

        // Turn it back on and idle again: the timer runs from the new start.
        h.Now += 1000;
        h.TapCaps();
        Assert.True(h.State.MouseModeOn);

        h.Now += 10_001;
        h.Tick();
        Assert.False(h.State.MouseModeOn);
    }
}
