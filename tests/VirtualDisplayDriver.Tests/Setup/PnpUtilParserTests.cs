using FluentAssertions;
using VirtualDisplayDriver;
using Xunit;

namespace VirtualDisplayDriver.Tests.Setup;

public class PnpUtilParserTests
{
    [Fact]
    public void ParseDeviceInfo_EnabledDevice_ReturnsEnabledInfo()
    {
        var output = """
            Instance ID:        ROOT\DISPLAY\0000
            Device Description: Virtual Display
            Class Name:         Display
            Class GUID:         {4d36e968-e325-11ce-bfc1-08002be10318}
            Manufacturer Name:  MikeTheTech
            Status:             Started
            Driver Name:        oem42.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(@"ROOT\DISPLAY\0000");
        result.Description.Should().Be("Virtual Display");
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ParseDeviceInfo_DisabledDevice_ReturnsDisabledInfo()
    {
        var output = """
            Instance ID:        ROOT\DISPLAY\0000
            Device Description: Virtual Display
            Class Name:         Display
            Class GUID:         {4d36e968-e325-11ce-bfc1-08002be10318}
            Manufacturer Name:  MikeTheTech
            Status:             Stopped
            Driver Name:        oem42.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);

        result.Should().NotBeNull();
        result!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ParseDeviceInfo_NoMatchingDevice_ReturnsNull()
    {
        var output = """
            Instance ID:        PCI\VEN_10DE&DEV_2504
            Device Description: NVIDIA GeForce RTX 3060
            Class Name:         Display
            Status:             Started
            Driver Name:        oem15.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);
        result.Should().BeNull();
    }

    [Fact]
    public void ParseDeviceInfo_MultipleDevices_FindsVdd()
    {
        var output = """
            Instance ID:        PCI\VEN_10DE&DEV_2504
            Device Description: NVIDIA GeForce RTX 3060
            Class Name:         Display
            Status:             Started
            Driver Name:        oem15.inf

            Instance ID:        ROOT\DISPLAY\0000
            Device Description: Virtual Display
            Class Name:         Display
            Manufacturer Name:  MikeTheTech
            Status:             Started
            Driver Name:        oem42.inf

            Instance ID:        PCI\VEN_8086&DEV_4680
            Device Description: Intel UHD Graphics
            Class Name:         Display
            Status:             Started
            Driver Name:        oem3.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(@"ROOT\DISPLAY\0000");
        result.Description.Should().Be("Virtual Display");
    }

    [Fact]
    public void ParseDeviceInfo_EmptyOutput_ReturnsNull()
    {
        VirtualDisplaySetup.ParseDeviceInfo("").Should().BeNull();
        VirtualDisplaySetup.ParseDeviceInfo("   ").Should().BeNull();
    }

    [Fact]
    public void ParseDeviceInfo_MatchesByManufacturer()
    {
        var output = """
            Instance ID:        ROOT\DISPLAY\0001
            Device Description: Some Other Name
            Class Name:         Display
            Manufacturer Name:  MikeTheTech
            Status:             Started
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);
        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(@"ROOT\DISPLAY\0001");
    }

    [Fact]
    public void ParseDeviceInfo_MatchesByDriverName()
    {
        var output = """
            Instance ID:        ROOT\DISPLAY\0002
            Device Description: Some Display
            Class Name:         Display
            Status:             Started
            Driver Name:        MttVDD.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);
        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(@"ROOT\DISPLAY\0002");
    }

    [Fact]
    public void ParseOemInfName_FindsMttVddEntry()
    {
        var output = """
            Published Name: oem15.inf
            Original Name:  nvdm.inf
            Provider Name:  NVIDIA
            Class Name:     Display
            Class GUID:     {4d36e968-e325-11ce-bfc1-08002be10318}
            Driver Version: 01/01/2024 31.0.15.1234

            Published Name: oem42.inf
            Original Name:  MttVDD.inf
            Provider Name:  MikeTheTech
            Class Name:     Display
            Class GUID:     {4d36e968-e325-11ce-bfc1-08002be10318}
            Driver Version: 01/01/2025 1.0.0.0
            """;

        var result = VirtualDisplaySetup.ParseOemInfName(output);
        result.Should().Be("oem42.inf");
    }

    [Fact]
    public void ParseOemInfName_NoMatch_ReturnsNull()
    {
        var output = """
            Published Name: oem15.inf
            Original Name:  nvdm.inf
            Provider Name:  NVIDIA
            Class Name:     Display
            """;

        var result = VirtualDisplaySetup.ParseOemInfName(output);
        result.Should().BeNull();
    }

    [Fact]
    public void ParseOemInfName_EmptyOutput_ReturnsNull()
    {
        VirtualDisplaySetup.ParseOemInfName("").Should().BeNull();
    }

    [Fact]
    public void ParseDeviceInfo_ProblemDevice_ReturnsErrorState()
    {
        // Real output from a machine where VDD has Problem Code 43
        var output = """
            Microsoft PnP Utility

            Instance ID:                ROOT\DISPLAY\0000
            Device Description:         Virtual Display Driver
            Class Name:                 Display
            Class GUID:                 {4d36e968-e325-11ce-bfc1-08002be10318}
            Manufacturer Name:          MikeTheTech
            Status:                     Problem
            Problem Code:               43 (0x2B) [CM_PROB_FAILED_POST_START]
            Driver Name:                oem117.inf

            Instance ID:                PCI\VEN_10DE&DEV_2204&SUBSYS_88D5103C&REV_A1\4&16e2ac9c&0&0008
            Device Description:         NVIDIA GeForce RTX 3090
            Class Name:                 Display
            Class GUID:                 {4d36e968-e325-11ce-bfc1-08002be10318}
            Manufacturer Name:          NVIDIA
            Status:                     Started
            Driver Name:                oem110.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(@"ROOT\DISPLAY\0000");
        result.Description.Should().Be("Virtual Display Driver");
        // A device with Status: Problem is NOT enabled — it has an error
        result.IsEnabled.Should().BeFalse();
        result.HasError.Should().BeTrue();
        result.ProblemCode.Should().Be(43);
    }

    [Fact]
    public void ParseDeviceInfo_ProblemDevice_GetDeviceState_ReturnsError()
    {
        // When a device has a problem code, GetDeviceStateAsync should return Error, not Disabled
        var output = """
            Instance ID:                ROOT\DISPLAY\0000
            Device Description:         Virtual Display Driver
            Class Name:                 Display
            Manufacturer Name:          MikeTheTech
            Status:                     Problem
            Problem Code:               43 (0x2B) [CM_PROB_FAILED_POST_START]
            Driver Name:                oem117.inf
            """;

        var result = VirtualDisplaySetup.ParseDeviceInfo(output);

        result.Should().NotBeNull();
        result!.HasError.Should().BeTrue();
    }
}
