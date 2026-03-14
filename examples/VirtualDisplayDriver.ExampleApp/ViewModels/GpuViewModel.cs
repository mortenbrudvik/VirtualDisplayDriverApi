using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class GpuViewModel : ObservableObject
{
    private readonly IVirtualDisplayManager _manager;
    private readonly IActivityLogger _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _d3DDeviceGpu;
    [ObservableProperty] private string? _iddCxVersion;
    [ObservableProperty] private string? _assignedGpu;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetGpuCommand))]
    private string? _selectedGpu;

    public ObservableCollection<string> AllGpus { get; } = [];

    public GpuViewModel(IVirtualDisplayManager manager, IActivityLogger logger)
    {
        _manager = manager;
        _logger = logger;
        _ = RefreshGpuInfoAsync();
    }

    [RelayCommand]
    private async Task RefreshGpuInfoAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        _logger.LogInfo("GPU", "Querying GPU information...");

        try
        {
            D3DDeviceGpu = await _manager.GetD3DDeviceGpuAsync();
            IddCxVersion = await _manager.GetIddCxVersionAsync();
            AssignedGpu = await _manager.GetAssignedGpuAsync();

            var gpus = await _manager.GetAllGpusAsync();
            AllGpus.Clear();
            foreach (var gpu in gpus)
                AllGpus.Add(gpu);

            SelectedGpu = AssignedGpu;

            _logger.LogSuccess("GPU", $"GPU info loaded — Assigned: {AssignedGpu}, Available: {gpus.Count} GPUs");
        }
        catch (PipeConnectionException ex)
        {
            ErrorMessage = "Not connected to Virtual Display Driver.";
            _logger.LogError("GPU", ErrorMessage, ex.ToString());
        }
        catch (CommandException ex)
        {
            ErrorMessage = $"Command failed: {ex.Message}";
            _logger.LogError("GPU", ErrorMessage, ex.RawResponse ?? ex.ToString());
        }
        catch (VddException ex)
        {
            ErrorMessage = $"Driver error: {ex.Message}";
            _logger.LogError("GPU", ErrorMessage, ex.ToString());
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetGpu))]
    private async Task SetGpuAsync()
    {
        if (SelectedGpu is null) return;

        IsLoading = true;
        ErrorMessage = null;
        _logger.LogInfo("GPU", $"Setting GPU to '{SelectedGpu}'... (triggers reload)");

        try
        {
            await _manager.SetGpuAsync(SelectedGpu);
            AssignedGpu = SelectedGpu;
            _logger.LogSuccess("GPU", $"GPU set to '{SelectedGpu}'");
        }
        catch (PipeConnectionException ex)
        {
            ErrorMessage = "Not connected to Virtual Display Driver.";
            _logger.LogError("GPU", ErrorMessage, ex.ToString());
        }
        catch (CommandException ex)
        {
            ErrorMessage = $"Command failed: {ex.Message}";
            _logger.LogError("GPU", ErrorMessage, ex.RawResponse ?? ex.ToString());
        }
        catch (VddException ex)
        {
            ErrorMessage = $"Driver error: {ex.Message}";
            _logger.LogError("GPU", ErrorMessage, ex.ToString());
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSetGpu() => SelectedGpu is not null;
}
