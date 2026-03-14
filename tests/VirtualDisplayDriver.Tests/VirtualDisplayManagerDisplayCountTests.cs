using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerDisplayCountTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new() { ReloadSpacing = TimeSpan.Zero };

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    public VirtualDisplayManagerDisplayCountTests()
    {
        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");
    }

    [Fact]
    public async Task SetDisplayCountAsync_UpdatesDisplayCount()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(3);
        manager.DisplayCount.Should().Be(3);
    }

    [Fact]
    public async Task SetDisplayCountAsync_ThrowsForNegative()
    {
        await using var manager = CreateManager();
        var act = () => manager.SetDisplayCountAsync(-1);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SetDisplayCountAsync_AllowsZero()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(0);
        manager.DisplayCount.Should().Be(0);
    }

    [Fact]
    public async Task AddDisplaysAsync_IncrementsCount()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(2);
        await manager.AddDisplaysAsync(3);
        manager.DisplayCount.Should().Be(5);
        await _mockClient.Received().SetDisplayCountAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDisplaysAsync_ThrowsForZero()
    {
        await using var manager = CreateManager();
        var act = () => manager.AddDisplaysAsync(0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AddDisplaysAsync_ThrowsForNegative()
    {
        await using var manager = CreateManager();
        var act = () => manager.AddDisplaysAsync(-1);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RemoveDisplaysAsync_DecrementsCount()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(5);
        await manager.RemoveDisplaysAsync(2);
        manager.DisplayCount.Should().Be(3);
        await _mockClient.Received().SetDisplayCountAsync(3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveDisplaysAsync_ClampsToZero()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(2);
        await manager.RemoveDisplaysAsync(5);
        manager.DisplayCount.Should().Be(0);
        await _mockClient.Received().SetDisplayCountAsync(0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveDisplaysAsync_ThrowsForZero()
    {
        await using var manager = CreateManager();
        var act = () => manager.RemoveDisplaysAsync(0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RemoveAllDisplaysAsync_SetsCountToZero()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(3);
        await manager.RemoveAllDisplaysAsync();
        manager.DisplayCount.Should().Be(0);
        await _mockClient.Received().SetDisplayCountAsync(0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisplayCount_RespectsInitialValueFromOptions()
    {
        var opts = new VirtualDisplayOptions { InitialDisplayCount = 4, ReloadSpacing = TimeSpan.Zero };
        await using var manager = new VirtualDisplayManager(_mockClient, opts);
        manager.DisplayCount.Should().Be(4);
    }

    [Fact]
    public async Task DisplayCount_UnchangedAfterFailedCommand()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(3);
        manager.DisplayCount.Should().Be(3);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new PipeConnectionException("pipe broke"));

        var act = () => manager.SetDisplayCountAsync(5);
        await act.Should().ThrowAsync<PipeConnectionException>();
        manager.DisplayCount.Should().Be(3);
    }
}
