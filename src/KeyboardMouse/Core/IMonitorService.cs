using System.Drawing;

namespace KeyboardMouse.Core;

/// <summary>Screen geometry used by controllers; fakeable in tests.</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> All { get; }

    MonitorInfo GetAtPoint(Point point);

    MonitorInfo Next(Point pointer);

    Rectangle? GetForegroundWindowBounds();

    Point? GetForegroundWindowCenter();

    double GetScaleAtPoint(Point point);

    (double X, double Y) ClampToDesktop(double x, double y);
}
