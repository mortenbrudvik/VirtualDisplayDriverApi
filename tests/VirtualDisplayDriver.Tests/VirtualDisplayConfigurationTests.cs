using FluentAssertions;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayConfigurationTests
{
    [Fact]
    public void GetVirtualMonitors_ReturnsListWithSequentialIndices()
    {
        var monitors = VirtualDisplayConfiguration.GetVirtualMonitors();

        monitors.Should().NotBeNull();
        for (var i = 0; i < monitors.Count; i++)
            monitors[i].Index.Should().Be(i);
    }

    [Fact]
    public void GetVirtualMonitors_AllHaveDeviceName()
    {
        var monitors = VirtualDisplayConfiguration.GetVirtualMonitors();

        foreach (var monitor in monitors)
            monitor.DeviceName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetVirtualMonitors_AllHavePositiveResolution()
    {
        var monitors = VirtualDisplayConfiguration.GetVirtualMonitors();

        foreach (var monitor in monitors)
        {
            monitor.Width.Should().BeGreaterThan(0);
            monitor.Height.Should().BeGreaterThan(0);
            monitor.RefreshRate.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void SetResolution_ThrowsOnNegativeIndex()
    {
        var act = () => VirtualDisplayConfiguration.SetResolution(-1, 1920, 1080);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetResolution_ThrowsOnOutOfRangeIndex()
    {
        var monitors = VirtualDisplayConfiguration.GetVirtualMonitors();
        var act = () => VirtualDisplayConfiguration.SetResolution(monitors.Count + 10, 1920, 1080);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetResolution_ThrowsOnInvalidWidth()
    {
        var act = () => VirtualDisplayConfiguration.SetResolution(0, 0, 1080);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetResolutionByDeviceName_ThrowsOnNullDeviceName()
    {
        var act = () => VirtualDisplayConfiguration.SetResolutionByDeviceName(null!, 1920, 1080);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VirtualMonitor_RecordEquality()
    {
        var a = new VirtualMonitor(0, @"\\.\DISPLAY1", 1920, 1080, 60);
        var b = new VirtualMonitor(0, @"\\.\DISPLAY1", 1920, 1080, 60);
        a.Should().Be(b);
    }

    [Fact]
    public void VirtualMonitor_RecordInequality()
    {
        var a = new VirtualMonitor(0, @"\\.\DISPLAY1", 1920, 1080, 60);
        var b = new VirtualMonitor(0, @"\\.\DISPLAY1", 2560, 1440, 60);
        a.Should().NotBe(b);
    }
}
