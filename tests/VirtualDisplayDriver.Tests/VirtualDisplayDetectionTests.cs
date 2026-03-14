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

    [Fact]
    public void GetConfiguredDisplayCount_ReturnsNonNegative()
    {
        var count = VirtualDisplayDetection.GetConfiguredDisplayCount();
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetConfiguredDisplayCount_WhenDriverInstalled_ReturnsPositive()
    {
        // If the driver is installed, vdd_settings.xml should exist and have a count >= 1
        // (the driver defaults to 1 if count is 0 or missing)
        if (!VirtualDisplayDetection.IsDriverInstalled())
            return; // skip on machines without the driver

        var count = VirtualDisplayDetection.GetConfiguredDisplayCount();
        count.Should().BeGreaterThanOrEqualTo(1,
            "when the driver is installed, vdd_settings.xml should have a count >= 1 " +
            "(the driver defaults to 1 if count is 0). " +
            "If this fails, GetConfiguredDisplayCount is not finding the settings file.");
    }

    [Fact]
    public void GetSettingsFilePath_WhenDriverInstalled_FileExists()
    {
        if (!VirtualDisplayDetection.IsDriverInstalled())
            return;

        var path = VirtualDisplayDetection.GetSettingsFilePath();
        path.Should().NotBeNull("the driver is installed so the settings file should be locatable");
        File.Exists(path).Should().BeTrue(
            $"vdd_settings.xml should exist at '{path}'. " +
            "If this fails, the file search is not looking in subdirectories.");
    }
}
