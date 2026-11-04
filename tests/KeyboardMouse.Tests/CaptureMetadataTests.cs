using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class CaptureMetadataTests
{
    [Fact]
    public void FromMonitor_ReportsNativePixels_NotTheDpiScale()
    {
        var monitor = new MonitorInfo(
            new IntPtr(7),
            new Rectangle(-2560, -200, 2560, 1440),
            new Rectangle(-2560, -200, 2560, 1400),
            2.0);

        CaptureMetadata metadata = CaptureMetadata.FromMonitor(monitor);

        // GDI captures physical pixels. Reporting the monitor's DPI scale here
        // used to shrink or shift every CV target on mixed-DPI desktops.
        Assert.Equal(1.0, metadata.DpiScale);
        Assert.Equal(new TargetBounds(-2560, -200, 2560, 1440), metadata.Region);
        Assert.Equal(new IntPtr(7), metadata.MonitorHandle);
        Assert.False(metadata.IsFullScreen);
    }

    [Fact]
    public void FromRegion_KeepsNegativeCoordinates()
    {
        var region = new TargetBounds(-1920, -120, 800, 600);

        CaptureMetadata metadata = CaptureMetadata.FromRegion(region);

        Assert.Equal(region, metadata.Region);
        Assert.Equal(1.0, metadata.DpiScale);
    }
}
