using FluentAssertions;
using VirtualDisplayDriver;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.IntegrationTests;

[Trait("Category", "Integration")]
public class PipeClientIntegrationTests
{
    private readonly VddPipeClient _client = new(new VirtualDisplayOptions());

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        var response = await _client.PingAsync();
        response.Should().Contain("PONG");
    }

    [Fact]
    public async Task GetSettings_ReturnsSettingsString()
    {
        var response = await _client.GetSettingsAsync();
        response.Should().Contain("SETTINGS");
        response.Should().Contain("DEBUG=");
        response.Should().Contain("LOG=");
    }

    [Fact]
    public async Task GetIddCxVersion_ReturnsNonEmpty()
    {
        var response = await _client.SendQueryAsync("IDDCXVERSION");
        response.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAllGpus_ReturnsNonEmpty()
    {
        var response = await _client.SendQueryAsync("GETALLGPUS");
        response.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Manager_Ping_ReturnsTrue()
    {
        await using var manager = new VirtualDisplayManager(_client, new VirtualDisplayOptions());
        var result = await manager.PingAsync();
        result.Should().BeTrue();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Manager_GetSettings_ReturnsParsed()
    {
        await using var manager = new VirtualDisplayManager(_client, new VirtualDisplayOptions());
        var settings = await manager.GetSettingsAsync();
        settings.Should().NotBeNull();
    }
}
