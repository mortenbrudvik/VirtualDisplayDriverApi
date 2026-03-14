using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerToggleTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new() { ReloadSpacing = TimeSpan.Zero };

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    public VirtualDisplayManagerToggleTests()
    {
        _mockClient.SendToggleAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns("");
        _mockClient.SetGpuAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetHdrPlusAsync_DelegatesToPipeClient(bool enabled)
    {
        await using var manager = CreateManager();
        await manager.SetHdrPlusAsync(enabled);
        await _mockClient.Received().SendToggleAsync("HDRPLUS", enabled, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSdr10BitAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetSdr10BitAsync(true);
        await _mockClient.Received().SendToggleAsync("SDR10", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCustomEdidAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetCustomEdidAsync(true);
        await _mockClient.Received().SendToggleAsync("CUSTOMEDID", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPreventSpoofAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetPreventSpoofAsync(false);
        await _mockClient.Received().SendToggleAsync("PREVENTSPOOF", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCeaOverrideAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetCeaOverrideAsync(true);
        await _mockClient.Received().SendToggleAsync("CEAOVERRIDE", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetHardwareCursorAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetHardwareCursorAsync(true);
        await _mockClient.Received().SendToggleAsync("HARDWARECURSOR", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDebugLoggingAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetDebugLoggingAsync(true);
        await _mockClient.Received().SendToggleAsync("LOG_DEBUG", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLoggingAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetLoggingAsync(false);
        await _mockClient.Received().SendToggleAsync("LOGGING", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetGpuAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetGpuAsync("NVIDIA GeForce RTX 4090");
        await _mockClient.Received().SetGpuAsync("NVIDIA GeForce RTX 4090", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetGpuAsync_ThrowsForInvalidName(string? gpuName)
    {
        await using var manager = CreateManager();
        var act = () => manager.SetGpuAsync(gpuName!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReloadSpacing_DelaysBetweenReloadCommands()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromMilliseconds(200) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.SetDisplayCountAsync(1);
        await manager.SetDisplayCountAsync(2);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(150);
    }

    [Fact]
    public async Task ReloadSpacing_HonorsCancellationToken()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromSeconds(30) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        await manager.SetDisplayCountAsync(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => manager.SetDisplayCountAsync(2, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NonReloadCommands_SkipSpacing()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromSeconds(10) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");
        _mockClient.PingAsync(Arg.Any<CancellationToken>()).Returns("PONG");

        await manager.SetDisplayCountAsync(1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.PingAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task SetDebugLoggingAsync_SkipsReloadSpacing()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromSeconds(10) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        await manager.SetDisplayCountAsync(1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.SetDebugLoggingAsync(true);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task SetGpuAsync_EnforcesReloadSpacing()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromMilliseconds(200) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        await manager.SetDisplayCountAsync(1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.SetGpuAsync("NVIDIA GeForce RTX 4090");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(150);
    }
}
