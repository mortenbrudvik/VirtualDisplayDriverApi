using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class DisplayManagementViewModel : ObservableObject
{
    private readonly IVirtualDisplayManager _manager;
    private readonly IVirtualDisplaySetup _setup;
    private readonly IActivityLogger _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDisplayCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllCommand))]
    private int _currentDisplayCount;

    [ObservableProperty] private int _targetDisplayCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public DisplayManagementViewModel(IVirtualDisplayManager manager, IVirtualDisplaySetup setup, IActivityLogger logger)
    {
        _manager = manager;
        _setup = setup;
        _logger = logger;
        CurrentDisplayCount = manager.DisplayCount;
        TargetDisplayCount = manager.DisplayCount;
    }

    [RelayCommand]
    private async Task SetDisplayCountAsync()
    {
        await ExecuteAsync("Set Display Count",
            $"Setting display count to {TargetDisplayCount}...",
            () => _manager.SetDisplayCountAsync(TargetDisplayCount));
    }

    [RelayCommand]
    private async Task AddDisplayAsync()
    {
        await ExecuteAsync("Add Display",
            "Adding 1 display...",
            () => _manager.AddDisplaysAsync(1));
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveDisplayAsync()
    {
        await ExecuteAsync("Remove Display",
            "Removing 1 display...",
            () => _manager.RemoveDisplaysAsync(1));
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAllAsync()
    {
        await ExecuteAsync("Remove All",
            "Removing all displays...",
            () => _manager.RemoveAllDisplaysAsync());
    }

    private bool CanRemove() => CurrentDisplayCount > 0;

    private async Task ExecuteAsync(string action, string logMessage, Func<Task> operation)
    {
        IsLoading = true;
        ErrorMessage = null;
        _logger.LogInfo("Display", logMessage);

        try
        {
            await operation();

            // Write count to XML (matches official VDD Control app approach).
            // The pipe command triggers a driver reload, but the driver may not
            // update the XML itself. Writing XML ensures the count persists.
            VirtualDisplayDetection.SetConfiguredDisplayCount(_manager.DisplayCount);

            CurrentDisplayCount = _manager.DisplayCount;
            TargetDisplayCount = CurrentDisplayCount;
            _logger.LogSuccess("Display", $"{action} complete — display count is now {CurrentDisplayCount}");

            // The driver restarts after display count changes and may crash (Code 43).
            // Wait briefly, then check device state and auto-recover if needed.
            await Task.Delay(2000);
            await AutoRecoverIfNeededAsync();
        }
        catch (PipeConnectionException ex)
        {
            ErrorMessage = "Not connected to Virtual Display Driver. Is the driver running?";
            _logger.LogError("Display", ErrorMessage, ex.ToString());
        }
        catch (CommandException ex)
        {
            ErrorMessage = $"Command failed: {ex.Message}";
            _logger.LogError("Display", ErrorMessage, ex.RawResponse ?? ex.ToString());
        }
        catch (VddException ex)
        {
            ErrorMessage = $"Driver error: {ex.Message}";
            _logger.LogError("Display", ErrorMessage, ex.ToString());
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AutoRecoverIfNeededAsync()
    {
        if (VirtualDisplayDetection.IsPipeRunning())
            return;

        var state = await _setup.GetDeviceStateAsync();
        if (state != DeviceState.Error)
            return;

        _logger.LogWarning("Display", "Driver crashed after display count change (Code 43). Restarting device...");

        try
        {
            await _setup.RestartDeviceAsync();

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
                // Re-sync local count from XML to match the recovered driver's state.
                var xmlCount = VirtualDisplayDetection.GetConfiguredDisplayCount();
                await _manager.SyncDisplayCountAsync(xmlCount);
                CurrentDisplayCount = _manager.DisplayCount;
                _logger.LogSuccess("Display", "Driver recovered automatically.");
            }
            else
            {
                _logger.LogWarning("Display", "Driver did not recover within 10 seconds. Try restarting manually.");
            }
        }
        catch (SetupException ex)
        {
            _logger.LogError("Display", $"Auto-recovery failed: {ex.Message}");
        }
    }
}
