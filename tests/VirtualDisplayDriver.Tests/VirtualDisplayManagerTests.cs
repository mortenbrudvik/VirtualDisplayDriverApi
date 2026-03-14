using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new();

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    [Fact]
    public async Task Constructor_SetsInitialState()
    {
        await using var manager = CreateManager();
        manager.DisplayCount.Should().Be(0);
        manager.IsConnected.Should().BeFalse();
        manager.PipeClient.Should().BeSameAs(_mockClient);
    }

    [Fact]
    public void Constructor_ThrowsOnNullClient()
    {
        var act = () => new VirtualDisplayManager(null!, _options);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullOptions()
    {
        var act = () => new VirtualDisplayManager(_mockClient, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DisposeAsync_SubsequentCallsThrowObjectDisposed()
    {
        var manager = CreateManager();
        await manager.DisposeAsync();

        var act = () => manager.SetDisplayCountAsync(1);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Commands_AreSerialized()
    {
        await using var manager = CreateManager();
        var callOrder = new List<int>();
        var tcs1 = new TaskCompletionSource<string>();

        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callOrder.Add(1);
                var result = await tcs1.Task;
                callOrder.Add(2);
                return result;
            });

        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add(3);
                return Task.FromResult("1.10");
            });

        var pingTask = manager.PingAsync();
        await Task.Delay(50);
        var queryTask = manager.GetIddCxVersionAsync();
        await Task.Delay(50);

        callOrder.Should().Equal(1);

        tcs1.SetResult("PONG");
        await pingTask;
        await queryTask;

        callOrder.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task CallerCancellation_PassesThroughAsOperationCanceledException()
    {
        await using var manager = CreateManager();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => manager.SetDisplayCountAsync(1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IsConnected_SetTrueAfterSuccessfulNonPingCommand()
    {
        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns("1.10");
        await using var manager = CreateManager();

        manager.IsConnected.Should().BeFalse();
        await manager.GetIddCxVersionAsync();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task IsConnected_SetFalseAfterNonPingCommandFailure()
    {
        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new PipeConnectionException("driver not running"));
        await using var manager = CreateManager();

        var act = () => manager.SetDisplayCountAsync(1);
        await act.Should().ThrowAsync<PipeConnectionException>();
        manager.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SyncDisplayCountAsync_SetsCountWithoutPipeCommand()
    {
        await using var manager = CreateManager();
        manager.DisplayCount.Should().Be(0);

        await manager.SyncDisplayCountAsync(3);

        manager.DisplayCount.Should().Be(3);
        await _mockClient.DidNotReceive().SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncDisplayCountAsync_ThrowsOnNegative()
    {
        await using var manager = CreateManager();

        var act = () => manager.SyncDisplayCountAsync(-1);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SyncDisplayCountAsync_WaitsForSemaphore()
    {
        await using var manager = CreateManager();
        var tcs = new TaskCompletionSource<string>();

        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .Returns(async _ => await tcs.Task);

        // Start a ping that holds the semaphore
        var pingTask = manager.PingAsync();
        await Task.Delay(50);

        // SyncDisplayCountAsync should block until ping completes
        var syncTask = manager.SyncDisplayCountAsync(5);
        await Task.Delay(50);
        syncTask.IsCompleted.Should().BeFalse("sync should wait for semaphore");

        tcs.SetResult("PONG");
        await pingTask;
        await syncTask;

        manager.DisplayCount.Should().Be(5);
    }
}
