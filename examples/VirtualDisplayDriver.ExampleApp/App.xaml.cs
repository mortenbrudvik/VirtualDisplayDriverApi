using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VirtualDisplayDriver.DependencyInjection;
using VirtualDisplayDriver.ExampleApp.Services;
using VirtualDisplayDriver.ExampleApp.ViewModels;
using VirtualDisplayDriver.ExampleApp.Views;

namespace VirtualDisplayDriver.ExampleApp;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // VDD library
        services.AddVirtualDisplayDriver(opts =>
        {
            opts.ConnectTimeout = TimeSpan.FromSeconds(5);
            opts.ReloadSpacing = TimeSpan.FromSeconds(30);
        });

        // Logging
        services.AddLogging(builder => builder.AddDebug());

        // App services
        services.AddSingleton<IActivityLogger, ActivityLogger>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<StatusViewModel>();
        services.AddTransient<DisplayManagementViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<GpuViewModel>();
        services.AddSingleton<ActivityLogViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            var manager = _serviceProvider.GetService<IVirtualDisplayManager>();
            manager?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            _serviceProvider.Dispose();
        }

        base.OnExit(e);
    }
}
