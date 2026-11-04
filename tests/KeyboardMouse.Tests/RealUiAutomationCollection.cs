using Xunit;

namespace KeyboardMouse.Tests;

/// <summary>
/// Real UI Automation runs share a COM apartment and the desktop. Running them
/// concurrently makes FlaUI intermittently fail to open a window with E_FAIL,
/// so they are serialized against the rest of the suite.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealUiAutomationCollection
{
    public const string Name = "RealUiAutomation";
}
