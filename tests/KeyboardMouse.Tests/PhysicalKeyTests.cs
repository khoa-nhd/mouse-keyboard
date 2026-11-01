using KeyboardMouse.Input;

namespace KeyboardMouse.Tests;

public class PhysicalKeyTests
{
    [Fact]
    public void EveryMappedKey_RoundTripsThroughItsScanCode()
    {
        foreach (var key in Enum.GetValues<PhysicalKey>())
        {
            if (key == PhysicalKey.None)
            {
                continue;
            }

            var (scanCode, extended) = key.ToScanCode();
            Assert.True(scanCode != 0, $"{key} has no scan code.");
            Assert.Equal(key, PhysicalKeyExtensions.FromScanCode(scanCode, extended));
        }
    }

    [Fact]
    public void ScrollPageKeys_UseExtendedNavigationCodes()
    {
        Assert.Equal((0x49, true), PhysicalKey.PageUp.ToScanCode());
        Assert.Equal((0x51, true), PhysicalKey.PageDown.ToScanCode());
    }
}
