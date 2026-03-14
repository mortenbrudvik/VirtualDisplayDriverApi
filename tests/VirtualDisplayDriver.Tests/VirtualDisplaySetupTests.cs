using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VirtualDisplayDriver;
using VirtualDisplayDriver.Setup;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplaySetupTests
{
    private readonly IProcessRunner _processRunner;
    private readonly VirtualDisplaySetup _setup;

    private const string ValidReleaseJson = """
        {
            "tag_name": "25.7.23",
            "assets": [
                {
                    "name": "VirtualDisplayDriver-x86.Driver.Only.zip",
                    "size": 4096,
                    "browser_download_url": "https://github.com/test/download/vdd.zip"
                },
                {
                    "name": "VirtualDisplayDriver-ARM64.Driver.Only.zip",
                    "size": 4096,
                    "browser_download_url": "https://github.com/test/download/vdd-arm.zip"
                }
            ]
        }
        """;

    private const string EnabledDeviceOutput = """
        Instance ID:        ROOT\DISPLAY\0000
        Device Description: Virtual Display
        Class Name:         Display
        Manufacturer Name:  MikeTheTech
        Status:             Started
        Driver Name:        oem42.inf
        """;

    private const string DisabledDeviceOutput = """
        Instance ID:        ROOT\DISPLAY\0000
        Device Description: Virtual Display
        Class Name:         Display
        Manufacturer Name:  MikeTheTech
        Status:             Stopped
        Driver Name:        oem42.inf
        """;

    private const string NoVddDeviceOutput = """
        Instance ID:        PCI\VEN_10DE&DEV_2504
        Device Description: NVIDIA GeForce RTX 3060
        Class Name:         Display
        Status:             Started
        """;

    public VirtualDisplaySetupTests()
    {
        _processRunner = Substitute.For<IProcessRunner>();
        var httpClient = new HttpClient(new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidReleaseJson)
            }));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Test/1.0");
        _setup = new VirtualDisplaySetup(httpClient, _processRunner);
    }

    // --- GetDeviceInfoAsync ---

    [Fact]
    public async Task GetDeviceInfoAsync_EnabledDevice_ReturnsEnabledInfo()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(EnabledDeviceOutput);

        var result = await _setup.GetDeviceInfoAsync();

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be(@"ROOT\DISPLAY\0000");
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetDeviceInfoAsync_DisabledDevice_ReturnsDisabledInfo()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(DisabledDeviceOutput);

        var result = await _setup.GetDeviceInfoAsync();

        result.Should().NotBeNull();
        result!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetDeviceInfoAsync_NoDevice_ReturnsNull()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(NoVddDeviceOutput);

        var result = await _setup.GetDeviceInfoAsync();
        result.Should().BeNull();
    }

    // --- GetDeviceStateAsync ---

    [Fact]
    public async Task GetDeviceStateAsync_EnabledDevice_ReturnsEnabled()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(EnabledDeviceOutput);

        var result = await _setup.GetDeviceStateAsync();
        result.Should().Be(DeviceState.Enabled);
    }

    [Fact]
    public async Task GetDeviceStateAsync_DisabledDevice_ReturnsDisabled()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(DisabledDeviceOutput);

        var result = await _setup.GetDeviceStateAsync();
        result.Should().Be(DeviceState.Disabled);
    }

    [Fact]
    public async Task GetDeviceStateAsync_NoDevice_ReturnsNotFound()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(NoVddDeviceOutput);

        var result = await _setup.GetDeviceStateAsync();
        result.Should().Be(DeviceState.NotFound);
    }

    // --- GetDeviceStateAsync with Problem device ---

    [Fact]
    public async Task GetDeviceStateAsync_ProblemDevice_ReturnsError()
    {
        var problemDeviceOutput = """
            Instance ID:                ROOT\DISPLAY\0000
            Device Description:         Virtual Display Driver
            Class Name:                 Display
            Manufacturer Name:          MikeTheTech
            Status:                     Problem
            Problem Code:               43 (0x2B) [CM_PROB_FAILED_POST_START]
            Driver Name:                oem117.inf
            """;

        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(problemDeviceOutput);

        var result = await _setup.GetDeviceStateAsync();
        result.Should().Be(DeviceState.Error);
    }

    // --- EnableDeviceAsync ---

    [Fact]
    public async Task EnableDeviceAsync_CallsPnpUtilWithInstanceId()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(DisabledDeviceOutput);
        _processRunner.RunElevatedAsync("pnputil.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _setup.EnableDeviceAsync();

        await _processRunner.Received(1).RunElevatedAsync(
            "pnputil.exe",
            Arg.Is<string>(s => s.Contains(@"ROOT\DISPLAY\0000") && s.Contains("/enable-device")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableDeviceAsync_AlreadyEnabled_SkipsPnpUtil()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(EnabledDeviceOutput);

        await _setup.EnableDeviceAsync();

        await _processRunner.DidNotReceive().RunElevatedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableDeviceAsync_DeviceNotFound_ThrowsSetupException()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(NoVddDeviceOutput);

        var act = () => _setup.EnableDeviceAsync();
        await act.Should().ThrowAsync<SetupException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task EnableDeviceAsync_PnpUtilFails_ThrowsSetupException()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(DisabledDeviceOutput);
        _processRunner.RunElevatedAsync("pnputil.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var act = () => _setup.EnableDeviceAsync();
        await act.Should().ThrowAsync<SetupException>()
            .WithMessage("*exit code*");
    }

    // --- DisableDeviceAsync ---

    [Fact]
    public async Task DisableDeviceAsync_CallsPnpUtilWithInstanceId()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(EnabledDeviceOutput);
        _processRunner.RunElevatedAsync("pnputil.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _setup.DisableDeviceAsync();

        await _processRunner.Received(1).RunElevatedAsync(
            "pnputil.exe",
            Arg.Is<string>(s => s.Contains(@"ROOT\DISPLAY\0000") && s.Contains("/disable-device")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisableDeviceAsync_AlreadyDisabled_SkipsPnpUtil()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(DisabledDeviceOutput);

        await _setup.DisableDeviceAsync();

        await _processRunner.DidNotReceive().RunElevatedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- RestartDeviceAsync ---

    [Fact]
    public async Task RestartDeviceAsync_CallsPnpUtilRestartDevice()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(EnabledDeviceOutput);
        _processRunner.RunElevatedAsync("pnputil.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _setup.RestartDeviceAsync();

        await _processRunner.Received(1).RunElevatedAsync(
            "pnputil.exe",
            Arg.Is<string>(s => s.Contains("/restart-device") && s.Contains(@"ROOT\DISPLAY\0000")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestartDeviceAsync_DeviceNotFound_ThrowsSetupException()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-devices /class Display", Arg.Any<CancellationToken>())
            .Returns(NoVddDeviceOutput);

        var act = () => _setup.RestartDeviceAsync();
        await act.Should().ThrowAsync<SetupException>()
            .WithMessage("*not found*");
    }

    // --- UninstallDriverAsync ---

    [Fact]
    public async Task UninstallDriverAsync_FindsOemInfAndRemoves()
    {
        var enumDriversOutput = """
            Published Name: oem42.inf
            Original Name:  MttVDD.inf
            Provider Name:  MikeTheTech
            Class Name:     Display
            """;

        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-drivers", Arg.Any<CancellationToken>())
            .Returns(enumDriversOutput);
        _processRunner.RunElevatedAsync("pnputil.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _setup.UninstallDriverAsync();

        await _processRunner.Received(1).RunElevatedAsync(
            "pnputil.exe",
            Arg.Is<string>(s => s.Contains("\"oem42.inf\"") && s.Contains("/delete-driver")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UninstallDriverAsync_DriverNotFound_ThrowsSetupException()
    {
        _processRunner.RunAndCaptureOutputAsync("pnputil.exe", "/enum-drivers", Arg.Any<CancellationToken>())
            .Returns("Published Name: oem15.inf\nOriginal Name: nvdm.inf\n");

        var act = () => _setup.UninstallDriverAsync();
        await act.Should().ThrowAsync<SetupException>()
            .WithMessage("*not found*");
    }

    // --- GetAssetName ---

    [Fact]
    public void GetAssetName_ReturnsExpectedFormat()
    {
        var name = VirtualDisplaySetup.GetAssetName();
        name.Should().StartWith("VirtualDisplayDriver-");
        name.Should().EndWith(".Driver.Only.zip");
    }

    // --- Helper ---

    private class FakeHttpHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_handler(request));
    }
}
