using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class DisplayManagementViewModel : ObservableObject
{
    private readonly IVirtualDisplayManager _manager;
    private readonly IActivityLogger _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDisplayCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllCommand))]
    private int _currentDisplayCount;

    [ObservableProperty] private int _targetDisplayCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public DisplayManagementViewModel(IVirtualDisplayManager manager, IActivityLogger logger)
    {
        _manager = manager;
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
            CurrentDisplayCount = _manager.DisplayCount;
            TargetDisplayCount = CurrentDisplayCount;
            _logger.LogSuccess("Display", $"{action} complete — display count is now {CurrentDisplayCount}");
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
}
