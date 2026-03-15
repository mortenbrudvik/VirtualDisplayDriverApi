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
    [ObservableProperty] private bool _showReinstallBanner;
    [ObservableProperty] private bool _isSetupInProgress;
    [ObservableProperty] private double _setupProgressPercent;
    [ObservableProperty] private string? _setupProgressMessage;

    public StatusViewModel(IVirtualDisplayManager manager, IVirtualDisplaySetup setup, IActivityLogger logger)
    {
        _manager = manager;
        _setup = setup;
        _logger = logger;
        _ = SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync()
    {
        try
        {
            await RefreshStatusAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError("Status", $"Initial status refresh failed: {ex.Message}");
        }
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
            InstallPath = VirtualDisplayDetection.GetInstallPath();

            PingResult = await _manager.PingAsync();
            IsConnected = _manager.IsConnected;

            // Sync display count from XML only when driver is actually running.
            // When disabled/stopped, don't override — the count should be 0.
            if (IsPipeRunning)
            {
                var configuredCount = VirtualDisplayDetection.GetConfiguredDisplayCount();
                if (configuredCount != _manager.DisplayCount)
                    await _manager.SyncDisplayCountAsync(configuredCount);
            }
            else if (!IsPipeRunning && IsDriverInstalled)
            {
                await _manager.SyncDisplayCountAsync(0);
            }

            DisplayCount = _manager.DisplayCount;

            if (IsConnected)
            {
                try { IddCxVersion = await _manager.GetIddCxVersionAsync(); }
                catch { IddCxVersion = "N/A"; }
            }

            _logger.LogSuccess("Status", $"Status refreshed — Pipe: {IsPipeRunning}, Connected: {IsConnected}, Displays: {DisplayCount}");
        }
        catch (Exception ex) when (ex is VddException or ObjectDisposedException or OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Status", $"Status refresh failed: {ex.Message}");
        }
        finally
        {
            // Check device state to determine which banner to show
            var filesExist = VirtualDisplayDetection.IsDriverInstalled();
            var deviceState = DeviceState.NotFound;
            if (filesExist && !IsPipeRunning)
            {
                try
                {
                    deviceState = await _setup.GetDeviceStateAsync();
                }
                catch { /* ignore */ }
            }

            // NotFound = driver files may exist but not registered with Windows (needs install)
            var needsInstall = !filesExist || (!IsPipeRunning && deviceState == DeviceState.NotFound);
            ShowInstallBanner = needsInstall;
            IsDriverInstalled = !needsInstall;
            ShowReinstallBanner = !needsInstall && deviceState == DeviceState.Error;
            ShowEnableBanner = !needsInstall && !IsPipeRunning && deviceState != DeviceState.Error;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestartDeviceAsync()
    {
        IsSetupInProgress = true;
        ErrorMessage = null;
        _logger.LogInfo("Setup", "Restarting device...");

        try
        {
            SetupProgressMessage = "Restarting device...";
            await _setup.RestartDeviceAsync();
            _logger.LogSuccess("Setup", "Device restarted.");

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
                _logger.LogWarning("Setup", "Driver pipe did not start within 10 seconds.");
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Restart failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError("Setup", $"Restart failed unexpectedly: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
        }
    }

    [RelayCommand]
    private async Task DisableDriverAsync()
    {
        IsSetupInProgress = true;
        ErrorMessage = null;
        _logger.LogInfo("Setup", "Disabling all virtual display devices...");

        try
        {
            SetupProgressMessage = "Disabling devices...";
            await _setup.DisableDeviceAsync();
            await _manager.SyncDisplayCountAsync(0);
            _logger.LogSuccess("Setup", "All virtual display devices disabled. 0 virtual displays active.");
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Disable failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError("Setup", $"Disable failed unexpectedly: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
        }
    }

    [RelayCommand]
    private async Task UninstallDriverAsync()
    {
        IsSetupInProgress = true;
        ErrorMessage = null;
        _logger.LogInfo("Setup", "Uninstalling driver...");

        try
        {
            SetupProgressMessage = "Uninstalling driver...";
            await _setup.UninstallDriverAsync();
            await _manager.SyncDisplayCountAsync(0);
            _logger.LogSuccess("Setup", "Driver uninstalled. A system restart may be needed to fully remove all devices.");
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Uninstall failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError("Setup", $"Uninstall failed unexpectedly: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
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
            _logger.LogError("Setup", $"Installation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError("Setup", $"Installation failed unexpectedly: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
        }
    }

    [RelayCommand]
    private async Task ReinstallDriverAsync()
    {
        IsSetupInProgress = true;
        ErrorMessage = null;
        _logger.LogInfo("Setup", "Reinstalling driver (uninstall → install)...");

        try
        {
            // Uninstall
            SetupProgressMessage = "Uninstalling driver...";
            SetupProgressPercent = 0.1;
            await _setup.UninstallDriverAsync();
            _logger.LogSuccess("Setup", "Driver uninstalled.");

            // Wait for device to fully remove
            await Task.Delay(2000);

            // Install
            var progress = new Progress<SetupProgress>(p =>
            {
                SetupProgressPercent = 0.2 + p.PercentComplete * 0.8;
                SetupProgressMessage = p.Message;
            });

            await _setup.InstallDriverAsync(progress: progress);
            _logger.LogSuccess("Setup", "Driver reinstalled successfully.");

            // Wait for pipe to start
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
            {
                _logger.LogSuccess("Setup", "Driver pipe is running.");
            }
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Reinstall failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError("Setup", $"Reinstall failed unexpectedly: {ex.Message}");
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
            {
                _logger.LogSuccess("Setup", "Driver pipe is running.");
            }
            else
            {
                _logger.LogWarning("Setup", "Driver pipe did not start within 10 seconds. The driver may need more time or a system restart.");
            }
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("Setup", $"Enable/restart failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError("Setup", $"Enable/restart failed unexpectedly: {ex.Message}");
        }
        finally
        {
            IsSetupInProgress = false;
            SetupProgressMessage = null;
            await RefreshStatusAsync();
        }
    }
}
