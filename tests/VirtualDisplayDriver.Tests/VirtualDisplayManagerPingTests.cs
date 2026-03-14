using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerPingTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new();

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenResponseContainsPong()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>()).Returns("PONG");
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeTrue();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenResponseContainsPongWithLogData()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .Returns("PONG\n[2024-01-01] [PIPE] Heartbeat Ping");
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeTrue();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenResponseDoesNotContainPong()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>()).Returns("UNEXPECTED");
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeFalse();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenPipeConnectionFails()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new PipeConnectionException("driver not running"));
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeFalse();
        manager.IsConnected.Should().BeFalse();
    }
}
