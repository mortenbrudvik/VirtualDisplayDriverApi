namespace VirtualDisplayDriver;

public interface IVirtualDisplaySetup
{
    Task InstallDriverAsync(
        string installPath = @"C:\VirtualDisplayDriver",
        IProgress<SetupProgress>? progress = null,
        CancellationToken ct = default);

    Task UninstallDriverAsync(CancellationToken ct = default);

    Task<DeviceState> GetDeviceStateAsync(CancellationToken ct = default);
    Task<DeviceInfo?> GetDeviceInfoAsync(CancellationToken ct = default);

    Task EnableDeviceAsync(CancellationToken ct = default);
    Task DisableDeviceAsync(CancellationToken ct = default);
    Task RestartDeviceAsync(CancellationToken ct = default);
}
