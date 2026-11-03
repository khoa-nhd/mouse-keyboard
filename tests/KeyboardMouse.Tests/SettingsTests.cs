using KeyboardMouse.Config;
using KeyboardMouse.Overlays;

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
        Assert.Equal("", bad.Activation.ToggleKey);

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
            Feedback = new FeedbackSettings { CursorStyle = "Brackets" }
        };
        mixedCase.Normalize();
        Assert.Equal("brackets", mixedCase.Feedback.CursorStyle);

        var defaults = new Settings();
        defaults.Normalize();
        Assert.Equal("dot", defaults.Feedback.CursorStyle);
    }

    [Fact]
    public void Normalize_RepairsCursorColor()
    {
        var mixedCase = new Settings
        {
            Feedback = new FeedbackSettings { CursorColor = "#3A7BD5" }
        };
        mixedCase.Normalize();
        Assert.Equal("3a7bd5", mixedCase.Feedback.CursorColor);

        foreach (string bad in new[] { "", "#12", "blue", "12345g", "1,2,3" })
        {
            var settings = new Settings
            {
                Feedback = new FeedbackSettings { CursorColor = bad }
            };
            settings.Normalize();
            Assert.Equal(CursorColors.DefaultHex, settings.Feedback.CursorColor);
        }

        var defaults = new Settings();
        defaults.Normalize();
        Assert.Equal("2ecc71", defaults.Feedback.CursorColor);
    }

    [Fact]
    public void Normalize_RepairsStateColours()
    {
        var settings = new Settings
        {
            Feedback = new FeedbackSettings
            {
                ScrollColor = "#123ABC",
                PrecisionColor = "nope",
                TurboColor = "",
                DragLockColor = null!,
                AutoClickColor = "ffd700"
            }
        };

        settings.Normalize();

        Assert.Equal("123abc", settings.Feedback.ScrollColor);
        Assert.Equal("9b59b6", settings.Feedback.PrecisionColor);
        Assert.Equal("e74c3c", settings.Feedback.TurboColor);
        Assert.Equal("e67e22", settings.Feedback.DragLockColor);
        Assert.Equal("ffd700", settings.Feedback.AutoClickColor);
    }

    [Fact]
    public void ToPalette_MapsEveryColour()
    {
        var settings = new Settings
        {
            Feedback = new FeedbackSettings
            {
                CursorColor = "2ec9cc",
                ScrollColor = "112233",
                PrecisionColor = "445566",
                TurboColor = "778899",
                DragLockColor = "aabbcc",
                AutoClickColor = "ddeeff"
            }
        };

        var palette = settings.Feedback.ToPalette();

        Assert.Equal(CursorColors.ToColor("2ec9cc"), palette.Normal);
        Assert.Equal(CursorColors.ToColor("112233"), palette.Scroll);
        Assert.Equal(CursorColors.ToColor("445566"), palette.Precision);
        Assert.Equal(CursorColors.ToColor("778899"), palette.Turbo);
        Assert.Equal(CursorColors.ToColor("aabbcc"), palette.DragLock);
        Assert.Equal(CursorColors.ToColor("ddeeff"), palette.AutoClick);
    }

    [Fact]
    public void Normalize_RepairsBorderSettings()
    {
        var settings = new Settings
        {
            Feedback = new FeedbackSettings
            {
                BorderStyle = "Glow",
                BorderColor = "#AABBCC",
                BorderThickness = 40
            }
        };

        settings.Normalize();

        Assert.Equal("glow", settings.Feedback.BorderStyle);
        Assert.Equal("aabbcc", settings.Feedback.BorderColor);
        Assert.Equal(12, settings.Feedback.BorderThickness);

        var unknown = new Settings
        {
            Feedback = new FeedbackSettings { BorderStyle = "neon", BorderThickness = 0 }
        };
        unknown.Normalize();
        Assert.Equal("frame", unknown.Feedback.BorderStyle);
        Assert.Equal(2, unknown.Feedback.BorderThickness);
    }
}
