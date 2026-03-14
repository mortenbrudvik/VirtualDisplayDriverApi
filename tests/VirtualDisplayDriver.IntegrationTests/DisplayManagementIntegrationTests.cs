using FluentAssertions;
using VirtualDisplayDriver;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.IntegrationTests;

[Trait("Category", "Integration")]
public class DisplayManagementIntegrationTests : IAsyncDisposable
{
    private readonly VirtualDisplayManager _manager;

    public DisplayManagementIntegrationTests()
    {
        var options = new VirtualDisplayOptions
        {
            ReloadSpacing = TimeSpan.Zero
        };
        var client = new VddPipeClient(options);
        _manager = new VirtualDisplayManager(client, options);

        // Sync local count from driver's persisted config
        var configuredCount = VirtualDisplayDetection.GetConfiguredDisplayCount();
        _manager.SyncDisplayCount(configuredCount);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _manager.RemoveAllDisplaysAsync(); }
        catch { /* best-effort cleanup */ }
        await _manager.DisposeAsync();
    }

    [Fact]
    public async Task AddDisplay_IncreasesConfiguredCount()
    {
        // Arrange — record current configured count
        var countBefore = VirtualDisplayDetection.GetConfiguredDisplayCount();

        // Act — add 1 display
        await _manager.AddDisplaysAsync(1);
        await Task.Delay(3000); // Wait for driver to reload and write XML

        // Assert — XML count should have increased by 1
        var countAfter = VirtualDisplayDetection.GetConfiguredDisplayCount();
        countAfter.Should().Be(countBefore + 1,
            $"adding 1 display should increase configured count from {countBefore} to {countBefore + 1}");
        _manager.DisplayCount.Should().Be(countAfter);
    }

    [Fact]
    public async Task SetDisplayCount_MatchesConfiguredCount()
    {
        // Act — set to 2
        await _manager.SetDisplayCountAsync(2);
        await Task.Delay(3000);

        // Assert — XML should say 2
        var configuredCount = VirtualDisplayDetection.GetConfiguredDisplayCount();
        configuredCount.Should().Be(2);
        _manager.DisplayCount.Should().Be(2);
    }

    [Fact]
    public async Task RemoveAllDisplays_SetsConfiguredCountToZero()
    {
        // Arrange — ensure at least 1 display
        if (_manager.DisplayCount == 0)
        {
            await _manager.AddDisplaysAsync(1);
            await Task.Delay(3000);
        }

        // Act
        await _manager.RemoveAllDisplaysAsync();
        await Task.Delay(3000);

        // Assert — XML should say 0 (even though driver internally defaults to 1)
        var configuredCount = VirtualDisplayDetection.GetConfiguredDisplayCount();
        configuredCount.Should().Be(0);
        _manager.DisplayCount.Should().Be(0);
    }
}
