using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class StatusViewModel : ObservableObject
{
    private readonly IVirtualDisplayManager _manager;
    private readonly IVirtualDisplaySetup _setup;
    private readonly IActivityLogger _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPipeRunning;
    [ObservableProperty] private bool _isDriverInstalled;
    [ObservableProperty] private string? _installPath;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _pingResult;
    [ObservableProperty] private int _displayCount;
    [ObservableProperty] private string? _iddCxVersion;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _showInstallBanner;
    [ObservableProperty] private bool _showEnableBanner;
    [ObservableProperty] private bool _isSetupInProgress;
    [ObservableProperty] private double _setupProgressPercent;
    [ObservableProperty] private string? _setupProgressMessage;

    public StatusViewModel(IVirtualDisplayManager manager, IVirtualDisplaySetup setup, IActivityLogger logger)
    {
        _manager = manager;
        _setup = setup;
        _logger = logger;
        _ = RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        _logger.LogInfo("Status", "Refreshing status...");

        try
        {
            IsPipeRunning = VirtualDisplayDetection.IsPipeRunning();
            IsDriverInstalled = VirtualDisplayDetection.IsDriverInstalled();
            InstallPath = VirtualDisplayDetection.GetInstallPath();

            PingResult = await _manager.PingAsync();
            IsConnected = _manager.IsConnected;
            DisplayCount = _manager.DisplayCount;

            if (IsConnected)
            {
                try { IddCxVersion = await _manager.GetIddCxVersionAsync(); }
                catch { IddCxVersion = "N/A"; }
            }

            _logger.LogSuccess("Status", $"Status refreshed — Pipe: {IsPipeRunning}, Connected: {IsConnected}, Displays: {DisplayCount}");
        }
        catch (VddException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Status", $"Status refresh failed: {ex.Message}");
        }
        finally
        {
            ShowInstallBanner = !IsDriverInstalled;
            ShowEnableBanner = IsDriverInstalled && !IsPipeRunning;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        _logger.LogInfo("Status", "Pinging driver...");

        try
        {
            PingResult = await _manager.PingAsync();
            IsConnected = _manager.IsConnected;

            if (PingResult)
                _logger.LogSuccess("Status", "Ping successful — PONG received");
            else
                _logger.LogWarning("Status", "Ping failed — driver not responding");
        }
        catch (Exception ex)
        {
            PingResult = false;
            IsConnected = false;
            _logger.LogError("Status", $"Ping error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task InstallDriverAsync()
    {
        IsSetupInProgress = true;
        ErrorMessage = null;
        _logger.LogInfo("Setup", "Starting driver installation...");

        try
        {
            var progress = new Progress<SetupProgress>(p =>
            {
                SetupProgressPercent = p.PercentComplete;
                SetupProgressMessage = p.Message;
            });

            await _setup.InstallDriverAsync(progress: progress);
            _logger.LogSuccess("Setup", "Driver installed successfully.");
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Installation failed: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
        }
    }

    [RelayCommand]
    private async Task EnableDriverAsync()
    {
        IsSetupInProgress = true;
        ErrorMessage = null;

        try
        {
            var state = await _setup.GetDeviceStateAsync();

            if (state == DeviceState.Disabled)
            {
                _logger.LogInfo("Setup", "Enabling virtual display device...");
                await _setup.EnableDeviceAsync();
                _logger.LogSuccess("Setup", "Device enabled.");
            }
            else
            {
                // Device is enabled or has an error — restart to recover
                var reason = state == DeviceState.Error ? "has an error" : "is enabled but pipe is not running";
                _logger.LogInfo("Setup", $"Device {reason}. Restarting device...");
                await _setup.RestartDeviceAsync();
                _logger.LogSuccess("Setup", "Device restarted.");
            }

            // Wait for the pipe server to start (driver needs time to initialize)
            _logger.LogInfo("Setup", "Waiting for driver pipe to start...");
            SetupProgressMessage = "Waiting for driver to initialize...";

            var pipeReady = false;
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (VirtualDisplayDetection.IsPipeRunning())
                {
                    pipeReady = true;
                    break;
                }
            }

            if (pipeReady)
                _logger.LogSuccess("Setup", "Driver pipe is running.");
            else
                _logger.LogWarning("Setup", "Driver pipe did not start within 10 seconds. The driver may need more time or a system restart.");
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Enable/restart failed: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
        }
    }
}
