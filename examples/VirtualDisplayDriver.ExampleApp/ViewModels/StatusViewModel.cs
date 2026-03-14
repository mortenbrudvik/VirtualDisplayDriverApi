using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class StatusViewModel : ObservableObject
{
    private readonly IVirtualDisplayManager _manager;
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

    public StatusViewModel(IVirtualDisplayManager manager, IActivityLogger logger)
    {
        _manager = manager;
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
}
