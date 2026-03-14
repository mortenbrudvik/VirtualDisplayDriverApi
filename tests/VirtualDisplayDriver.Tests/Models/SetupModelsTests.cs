using FluentAssertions;
using VirtualDisplayDriver;
using Xunit;

namespace VirtualDisplayDriver.Tests.Models;

public class SetupModelsTests
{
    [Fact]
    public void SetupProgress_RecordEquality()
    {
        var a = new SetupProgress(SetupStage.Downloading, 0.5, "50%");
        var b = new SetupProgress(SetupStage.Downloading, 0.5, "50%");
        a.Should().Be(b);
    }

    [Fact]
    public void SetupProgress_RecordInequality()
    {
        var a = new SetupProgress(SetupStage.Downloading, 0.5, "50%");
        var b = new SetupProgress(SetupStage.Extracting, 0.5, "50%");
        a.Should().NotBe(b);
    }

    [Fact]
    public void DeviceInfo_RecordEquality()
    {
        var a = new DeviceInfo("ROOT\\DISPLAY\\0000", "Virtual Display", true);
        var b = new DeviceInfo("ROOT\\DISPLAY\\0000", "Virtual Display", true);
        a.Should().Be(b);
    }

    [Fact]
    public void DeviceInfo_RecordInequality_DifferentEnabled()
    {
        var a = new DeviceInfo("ROOT\\DISPLAY\\0000", "Virtual Display", true);
        var b = new DeviceInfo("ROOT\\DISPLAY\\0000", "Virtual Display", false);
        a.Should().NotBe(b);
    }

    [Fact]
    public void SetupStage_HasExpectedValues()
    {
        Enum.GetNames<SetupStage>().Should().BeEquivalentTo(
            "FetchingRelease", "Downloading", "Extracting", "InstallingDriver", "Complete");
    }

    [Fact]
    public void DeviceState_HasExpectedValues()
    {
        Enum.GetNames<DeviceState>().Should().BeEquivalentTo(
            "NotFound", "Enabled", "Disabled");
    }
}
