using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VirtualDisplayDriver.DependencyInjection;
using VirtualDisplayDriver.Pipe;
using Xunit;

namespace VirtualDisplayDriver.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVirtualDisplayDriver_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver();
        var provider = services.BuildServiceProvider();

        provider.GetService<IVddPipeClient>().Should().NotBeNull();
        provider.GetService<IVirtualDisplayManager>().Should().NotBeNull();
    }

    [Fact]
    public void AddVirtualDisplayDriver_RegistersAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver();
        var provider = services.BuildServiceProvider();

        var client1 = provider.GetService<IVddPipeClient>();
        var client2 = provider.GetService<IVddPipeClient>();
        client1.Should().BeSameAs(client2);

        var manager1 = provider.GetService<IVirtualDisplayManager>();
        var manager2 = provider.GetService<IVirtualDisplayManager>();
        manager1.Should().BeSameAs(manager2);
    }

    [Fact]
    public void AddVirtualDisplayDriver_AcceptsConfiguration()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver(opts =>
        {
            opts.ConnectTimeout = TimeSpan.FromSeconds(5);
            opts.ReloadSpacing = TimeSpan.FromSeconds(60);
        });
        var provider = services.BuildServiceProvider();

        var manager = provider.GetService<IVirtualDisplayManager>();
        manager.Should().NotBeNull();
    }

    [Fact]
    public void AddVirtualDisplayDriver_ManagerExposesPipeClient()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver();
        var provider = services.BuildServiceProvider();

        var manager = provider.GetRequiredService<IVirtualDisplayManager>();
        var client = provider.GetRequiredService<IVddPipeClient>();
        manager.PipeClient.Should().BeSameAs(client);
    }
}
