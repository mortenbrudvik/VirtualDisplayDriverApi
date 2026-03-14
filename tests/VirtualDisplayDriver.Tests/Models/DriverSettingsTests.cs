using FluentAssertions;
using VirtualDisplayDriver;
using Xunit;

namespace VirtualDisplayDriver.Tests.Models;

public class DriverSettingsTests
{
    [Fact]
    public void DriverSettings_RecordEquality()
    {
        var a = new DriverSettings(true, false);
        var b = new DriverSettings(true, false);
        a.Should().Be(b);
    }

    [Fact]
    public void DriverSettings_RecordInequality()
    {
        var a = new DriverSettings(true, false);
        var b = new DriverSettings(false, false);
        a.Should().NotBe(b);
    }

    [Fact]
    public void DriverSettings_Properties()
    {
        var settings = new DriverSettings(DebugLogging: true, Logging: false);
        settings.DebugLogging.Should().BeTrue();
        settings.Logging.Should().BeFalse();
    }

    [Fact]
    public void VirtualDisplayOptions_Defaults()
    {
        var opts = new VirtualDisplayOptions();
        opts.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(10));
        opts.ReloadSpacing.Should().Be(TimeSpan.FromSeconds(30));
        opts.InitialDisplayCount.Should().Be(0);
        opts.MaxDisplayCount.Should().Be(16);
    }

    [Fact]
    public void VirtualDisplayOptions_RespectsInitialCount()
    {
        var opts = new VirtualDisplayOptions { InitialDisplayCount = 5 };
        opts.InitialDisplayCount.Should().Be(5);
    }
}
