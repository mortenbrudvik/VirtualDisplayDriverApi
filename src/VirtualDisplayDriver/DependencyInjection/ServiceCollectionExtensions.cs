using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualDisplayDriver(
        this IServiceCollection services,
        Action<VirtualDisplayOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<VirtualDisplayOptions>(_ => { });

        services.TryAddSingleton<IVddPipeClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<VirtualDisplayOptions>>().Value;
            var logger = sp.GetService<ILogger<VddPipeClient>>();
            return new VddPipeClient(options, logger);
        });

        services.TryAddSingleton<IVirtualDisplayManager>(sp =>
        {
            var client = sp.GetRequiredService<IVddPipeClient>();
            var options = sp.GetRequiredService<IOptions<VirtualDisplayOptions>>().Value;
            var logger = sp.GetService<ILogger<VirtualDisplayManager>>();
            return new VirtualDisplayManager(client, options, logger);
        });

        return services;
    }
}
