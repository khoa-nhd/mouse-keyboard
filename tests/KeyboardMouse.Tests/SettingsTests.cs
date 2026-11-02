using KeyboardMouse.Config;

namespace KeyboardMouse.Tests;

public class SettingsTests
{
    [Fact]
    public void Normalize_ReplacesNullSections()
    {
        var settings = new Settings
        {
            Activation = null!,
            Keys = null!,
            Grid = null!,
            Hint = null!,
            Speed = null!,
            Scroll = null!,
            Feedback = null!
        };

        settings.Normalize();

        Assert.NotNull(settings.Activation);
        Assert.NotNull(settings.Keys);
        Assert.NotNull(settings.Grid);
        Assert.NotNull(settings.Hint);
        Assert.NotNull(settings.Speed);
        Assert.NotNull(settings.Scroll);
        Assert.NotNull(settings.Feedback);
        Assert.Equal("I", settings.Keys.Up);
        Assert.Equal("asdfjkl;", settings.Hint.Alphabet);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var settings = new Settings
        {
            Activation = new ActivationSettings { TapTimeoutMs = -10, AutoOffSeconds = -5 },
            Speed = new SpeedSettings
            {
                Normal = new SpeedConfig { Base = 100, Max = 50, Accel = -1, TapStep = -2 }
            },
            Grid = new GridSettings { MaxDepth = 99, Keys = [] },
            Hint = new HintSettings { Alphabet = "a", MaxElements = 0 },
            Scroll = new ScrollSettings { Base = 100, Max = 50, Accel = -3 },
            Feedback = new FeedbackSettings { DotSize = 0 }
        };

        settings.Normalize();

        Assert.Equal(50, settings.Activation.TapTimeoutMs);
        Assert.Equal(0, settings.Activation.AutoOffSeconds);
        Assert.Equal(100, settings.Speed.Normal.Max);
        Assert.Equal(0, settings.Speed.Normal.Accel);
        Assert.Equal(0, settings.Speed.Normal.TapStep);
        Assert.Equal(3, settings.Grid.MaxDepth);
        Assert.Equal(9, settings.Grid.Keys.Length);
        Assert.Equal("asdfjkl;", settings.Hint.Alphabet);
        Assert.Equal(1, settings.Hint.MaxElements);
        Assert.Equal(100, settings.Scroll.Max);
        Assert.Equal(0, settings.Scroll.Accel);
        Assert.Equal(2, settings.Feedback.DotSize);
    }

    [Fact]
    public void Normalize_KeepsValidValues()
    {
        var speed = new SpeedConfig { Base = 250, Max = 1800, Accel = 2500, TapStep = 4 };
        var settings = new Settings
        {
            Activation = new ActivationSettings { TapTimeoutMs = 250, AutoOffSeconds = 30 },
            Speed = new SpeedSettings { Normal = speed },
            Hint = new HintSettings { Alphabet = "jkl;", MaxElements = 200 },
            Feedback = new FeedbackSettings { DotSize = 10 }
        };

        settings.Normalize();

        Assert.Equal(250, settings.Activation.TapTimeoutMs);
        Assert.Equal(30, settings.Activation.AutoOffSeconds);
        Assert.Same(speed, settings.Speed.Normal);
        Assert.Equal(250, settings.Speed.Normal.Base);
        Assert.Equal(1800, settings.Speed.Normal.Max);
        Assert.Equal("jkl;", settings.Hint.Alphabet);
        Assert.Equal(200, settings.Hint.MaxElements);
        Assert.Equal(10, settings.Feedback.DotSize);
    }

    [Fact]
    public void NormalizedDefaultSpeedProfiles_AreValid()
    {
        var settings = new Settings();
        settings.Normalize();

        // Must not throw: the engine builds SpeedProfile from these.
        _ = settings.Speed.Normal.ToProfile();
        _ = settings.Speed.Precision.ToProfile();
        _ = settings.Speed.Turbo.ToProfile();
    }

    [Fact]
    public void Normalize_RepairsAnUnusableToggleKey()
    {
        var bad = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "" }
        };
        bad.Normalize();
        Assert.Equal("CapsLock", bad.Activation.ToggleKey);

        var modifier = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "LeftCtrl" }
        };
        modifier.Normalize();
        Assert.Equal("CapsLock", modifier.Activation.ToggleKey);

        var invalid = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "NoSuchKey" }
        };
        invalid.Normalize();
        Assert.Equal("CapsLock", invalid.Activation.ToggleKey);

        var valid = new Settings
        {
            Activation = new ActivationSettings { ToggleKey = "ScrollLock" }
        };
        valid.Normalize();
        Assert.Equal("ScrollLock", valid.Activation.ToggleKey);
    }

    [Fact]
    public void Normalize_RepairsCursorStyle()
    {
        var unknown = new Settings
        {
            Feedback = new FeedbackSettings { CursorStyle = "sparkle-dragon" }
        };
        unknown.Normalize();
        Assert.Equal("dot", unknown.Feedback.CursorStyle);

        var mixedCase = new Settings
        {
            Feedback = new FeedbackSettings { CursorStyle = "Buddy" }
        };
        mixedCase.Normalize();
        Assert.Equal("buddy", mixedCase.Feedback.CursorStyle);

        var defaults = new Settings();
        defaults.Normalize();
        Assert.Equal("dot", defaults.Feedback.CursorStyle);
    }
}
