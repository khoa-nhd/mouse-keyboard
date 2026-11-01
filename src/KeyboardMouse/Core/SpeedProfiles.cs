namespace KeyboardMouse.Core;

/// <summary>
/// The four speed curves the engine can use: pointer normal/precision/turbo
/// and scrolling. A single settings object feeds all of them.
/// </summary>
public sealed record SpeedProfiles(
    SpeedProfile Normal,
    SpeedProfile Precision,
    SpeedProfile Turbo,
    SpeedProfile Scroll,
    bool SmoothScroll)
{
    public static SpeedProfiles Default { get; } = new(
        SpeedProfile.Normal,
        SpeedProfile.Precision,
        SpeedProfile.Turbo,
        new SpeedProfile(400, 3000, 4000, 0),
        SmoothScroll: true);
}
