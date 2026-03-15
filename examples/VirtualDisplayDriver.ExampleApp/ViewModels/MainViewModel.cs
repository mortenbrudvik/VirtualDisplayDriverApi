using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualDisplayDriver.ExampleApp.Models;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IActivityLogger _logger;
    private readonly DispatcherTimer _statusTimer;

    [ObservableProperty] private bool _isPipeRunning;
    [ObservableProperty] private bool _isDriverInstalled;
    [ObservableProperty] private string? _installPath;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    public ObservableCollection<NavigationItem> NavigationItems { get; } =
    [
        new("Status", "\uE774", typeof(StatusViewModel)),
        new("Displays", "\uE7F4", typeof(DisplayManagementViewModel)),
        new("Settings", "\uE713", typeof(SettingsViewModel)),
        new("Activity Log", "\uE7BA", typeof(ActivityLogViewModel))
    ];

    public INavigationService Navigation => _navigation;

    public MainViewModel(INavigationService navigation, IActivityLogger logger)
    {
        _navigation = navigation;
        _logger = logger;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => RefreshStatusBar();
        _statusTimer.Start();

        RefreshStatusBar();

        _logger.LogInfo("System", "VDD Dashboard started");

        SelectedNavigationItem = NavigationItems[0];
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value is not null)
            _navigation.NavigateTo(value.ViewModelType);
    }

    private void RefreshStatusBar()
    {
        IsPipeRunning = VirtualDisplayDetection.IsPipeRunning();
        IsDriverInstalled = VirtualDisplayDetection.IsDriverInstalled();
        InstallPath = VirtualDisplayDetection.GetInstallPath();
    }
}
