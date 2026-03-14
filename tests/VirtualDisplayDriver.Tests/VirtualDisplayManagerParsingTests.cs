using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerParsingTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new();

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    [Theory]
    [InlineData("SETTINGS DEBUG=true LOG=false", true, false)]
    [InlineData("SETTINGS DEBUG=false LOG=true", false, true)]
    [InlineData("SETTINGS DEBUG=true LOG=true", true, true)]
    [InlineData("SETTINGS DEBUG=false LOG=false", false, false)]
    [InlineData("SETTINGS DEBUG=True LOG=False", true, false)]
    public async Task GetSettingsAsync_ParsesAllCombinations(
        string response, bool expectedDebug, bool expectedLog)
    {
        _mockClient.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(response);
        await using var manager = CreateManager();

        var settings = await manager.GetSettingsAsync();

        settings.DebugLogging.Should().Be(expectedDebug);
        settings.Logging.Should().Be(expectedLog);
    }

    [Fact]
    public async Task GetSettingsAsync_ThrowsOnInvalidFormat()
    {
        _mockClient.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns("GARBAGE DATA");
        await using var manager = CreateManager();

        var act = () => manager.GetSettingsAsync();

        var ex = await act.Should().ThrowAsync<CommandException>();
        ex.Which.RawResponse.Should().Be("GARBAGE DATA");
    }

    [Fact]
    public async Task GetAllGpusAsync_ParsesGpuLines()
    {
        _mockClient.SendQueryAsync("GETALLGPUS", Arg.Any<CancellationToken>())
            .Returns("[LOG] GPU: NVIDIA GeForce RTX 4090\n[LOG] GPU: Intel UHD 770");
        await using var manager = CreateManager();

        var gpus = await manager.GetAllGpusAsync();

        gpus.Should().Equal("NVIDIA GeForce RTX 4090", "Intel UHD 770");
    }

    [Fact]
    public async Task GetAllGpusAsync_HandlesTimestampPrefixedLogLines()
    {
        _mockClient.SendQueryAsync("GETALLGPUS", Arg.Any<CancellationToken>())
            .Returns("[2024-01-15 10:30:45] [GPU] GPU: NVIDIA GeForce RTX 4090\n[2024-01-15 10:30:45] [GPU] GPU: Intel UHD 770");
        await using var manager = CreateManager();

        var gpus = await manager.GetAllGpusAsync();

        gpus.Should().Equal("NVIDIA GeForce RTX 4090", "Intel UHD 770");
    }

    [Fact]
    public async Task GetAllGpusAsync_ReturnsEmptyListWhenNoMatch()
    {
        _mockClient.SendQueryAsync("GETALLGPUS", Arg.Any<CancellationToken>())
            .Returns("no gpus here");
        await using var manager = CreateManager();

        var gpus = await manager.GetAllGpusAsync();

        gpus.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssignedGpuAsync_ReturnsFirstGpuMatch()
    {
        _mockClient.SendQueryAsync("GETASSIGNEDGPU", Arg.Any<CancellationToken>())
            .Returns("[LOG] Assigned GPU: NVIDIA GeForce RTX 4090");
        await using var manager = CreateManager();

        var gpu = await manager.GetAssignedGpuAsync();

        gpu.Should().Be("NVIDIA GeForce RTX 4090");
    }

    [Fact]
    public async Task GetAssignedGpuAsync_ThrowsWhenNoGpuLine()
    {
        _mockClient.SendQueryAsync("GETASSIGNEDGPU", Arg.Any<CancellationToken>())
            .Returns("no gpu info");
        await using var manager = CreateManager();

        var act = () => manager.GetAssignedGpuAsync();

        await act.Should().ThrowAsync<CommandException>();
    }

    [Fact]
    public async Task GetIddCxVersionAsync_ReturnsFullResponse()
    {
        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns("IddCx version: 1.10.0");
        await using var manager = CreateManager();

        var version = await manager.GetIddCxVersionAsync();

        version.Should().Be("IddCx version: 1.10.0");
    }

    [Fact]
    public async Task GetIddCxVersionAsync_ThrowsOnEmptyResponse()
    {
        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns("");
        await using var manager = CreateManager();

        var act = () => manager.GetIddCxVersionAsync();

        await act.Should().ThrowAsync<CommandException>();
    }

    [Fact]
    public async Task GetD3DDeviceGpuAsync_ReturnsFullResponse()
    {
        _mockClient.SendQueryAsync("D3DDEVICEGPU", Arg.Any<CancellationToken>())
            .Returns("GPU Device: NVIDIA GeForce RTX 4090");
        await using var manager = CreateManager();

        var result = await manager.GetD3DDeviceGpuAsync();

        result.Should().Be("GPU Device: NVIDIA GeForce RTX 4090");
    }

    [Fact]
    public async Task GetD3DDeviceGpuAsync_ThrowsOnEmptyResponse()
    {
        _mockClient.SendQueryAsync("D3DDEVICEGPU", Arg.Any<CancellationToken>())
            .Returns("  ");
        await using var manager = CreateManager();

        var act = () => manager.GetD3DDeviceGpuAsync();

        await act.Should().ThrowAsync<CommandException>();
    }
}
