using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IVirtualDisplayManager _manager;
    private readonly IActivityLogger _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Queryable settings (populated from GetSettingsAsync)
    [ObservableProperty] private bool _debugLoggingEnabled;
    [ObservableProperty] private bool _loggingEnabled;

    // Reload-triggering settings (state unknown until toggled)
    [ObservableProperty] private bool _hdrPlusEnabled;
    [ObservableProperty] private bool _sdr10BitEnabled;
    [ObservableProperty] private bool _customEdidEnabled;
    [ObservableProperty] private bool _preventSpoofEnabled;
    [ObservableProperty] private bool _ceaOverrideEnabled;
    [ObservableProperty] private bool _hardwareCursorEnabled;

    public SettingsViewModel(IVirtualDisplayManager manager, IActivityLogger logger)
    {
        _manager = manager;
        _logger = logger;
        _ = RefreshSettingsAsync();
    }

    [RelayCommand]
    private Task RefreshSettingsAsync()
    {
        ErrorMessage = null;
        _logger.LogInfo("Settings", "Reading settings from vdd_settings.xml...");

        var settings = VirtualDisplayDetection.GetSettingsFromXml();
        DebugLoggingEnabled = settings.DebugLogging;
        LoggingEnabled = settings.Logging;
        HdrPlusEnabled = settings.HdrPlus;
        Sdr10BitEnabled = settings.Sdr10Bit;
        CustomEdidEnabled = settings.CustomEdid;
        PreventSpoofEnabled = settings.PreventSpoof;
        CeaOverrideEnabled = settings.CeaOverride;
        HardwareCursorEnabled = settings.HardwareCursor;

        _logger.LogSuccess("Settings", "Settings loaded from XML");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ToggleHdrPlus() => ToggleAsync("HDR+", HdrPlusEnabled, v => _manager.SetHdrPlusAsync(v), v => HdrPlusEnabled = v);

    [RelayCommand]
    private Task ToggleSdr10Bit() => ToggleAsync("SDR 10-bit", Sdr10BitEnabled, v => _manager.SetSdr10BitAsync(v), v => Sdr10BitEnabled = v);

    [RelayCommand]
    private Task ToggleCustomEdid() => ToggleAsync("Custom EDID", CustomEdidEnabled, v => _manager.SetCustomEdidAsync(v), v => CustomEdidEnabled = v);

    [RelayCommand]
    private Task TogglePreventSpoof() => ToggleAsync("Prevent Spoof", PreventSpoofEnabled, v => _manager.SetPreventSpoofAsync(v), v => PreventSpoofEnabled = v);

    [RelayCommand]
    private Task ToggleCeaOverride() => ToggleAsync("CEA Override", CeaOverrideEnabled, v => _manager.SetCeaOverrideAsync(v), v => CeaOverrideEnabled = v);

    [RelayCommand]
    private Task ToggleHardwareCursor() => ToggleAsync("Hardware Cursor", HardwareCursorEnabled, v => _manager.SetHardwareCursorAsync(v), v => HardwareCursorEnabled = v);

    [RelayCommand]
    private Task ToggleDebugLogging() => ToggleAsync("Debug Logging", DebugLoggingEnabled, v => _manager.SetDebugLoggingAsync(v), v => DebugLoggingEnabled = v, triggersReload: false);

    [RelayCommand]
    private Task ToggleLogging() => ToggleAsync("Logging", LoggingEnabled, v => _manager.SetLoggingAsync(v), v => LoggingEnabled = v, triggersReload: false);

    private async Task ToggleAsync(string name, bool value, Func<bool, Task> operation, Action<bool> revert, bool triggersReload = true)
    {
        IsLoading = true;
        ErrorMessage = null;
        var state = value ? "ON" : "OFF";
        _logger.LogInfo("Settings", $"Setting {name} to {state}...{(triggersReload ? " (triggers reload)" : "")}");

        try
        {
            await operation(value);
            _logger.LogSuccess("Settings", $"{name} set to {state}");
        }
        catch (PipeConnectionException ex)
        {
            revert(!value);
            ErrorMessage = "Not connected to Virtual Display Driver.";
            _logger.LogError("Settings", ErrorMessage, ex.ToString());
        }
        catch (CommandException ex)
        {
            revert(!value);
            ErrorMessage = $"Command failed: {ex.Message}";
            _logger.LogError("Settings", ErrorMessage, ex.RawResponse ?? ex.ToString());
        }
        catch (VddException ex)
        {
            revert(!value);
            ErrorMessage = $"Driver error: {ex.Message}";
            _logger.LogError("Settings", ErrorMessage, ex.ToString());
        }
        finally
        {
            IsLoading = false;
        }
    }
}
