using FluentAssertions;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayDetectionTests
{
    [Fact]
    public void IsPipeRunning_ReturnsBool()
    {
        // Should not throw — returns true or false depending on environment
        var result = VirtualDisplayDetection.IsPipeRunning();
        result.Should().Be(result); // validates it returns without exception
    }

    [Fact]
    public void IsDriverInstalled_ReturnsBool()
    {
        var result = VirtualDisplayDetection.IsDriverInstalled();
        result.Should().Be(result);
    }

    [Fact]
    public void GetInstallPath_ReturnsNullOrValidDirectory()
    {
        var path = VirtualDisplayDetection.GetInstallPath();

        if (path is not null)
            Directory.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void IsDriverInstalled_ConsistentWithGetInstallPath()
    {
        var installed = VirtualDisplayDetection.IsDriverInstalled();
        var path = VirtualDisplayDetection.GetInstallPath();

        installed.Should().Be(path is not null);
    }
}
