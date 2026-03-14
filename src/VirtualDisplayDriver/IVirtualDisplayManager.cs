using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver;

public interface IVirtualDisplayManager : IAsyncDisposable
{
    int DisplayCount { get; }
    bool IsConnected { get; }
    IVddPipeClient PipeClient { get; }

    void SyncDisplayCount(int count);

    Task<bool> PingAsync(CancellationToken ct = default);

    Task SetDisplayCountAsync(int count, CancellationToken ct = default);
    Task AddDisplaysAsync(int count = 1, CancellationToken ct = default);
    Task RemoveDisplaysAsync(int count = 1, CancellationToken ct = default);
    Task RemoveAllDisplaysAsync(CancellationToken ct = default);

    Task<DriverSettings> GetSettingsAsync(CancellationToken ct = default);

    Task SetHdrPlusAsync(bool enabled, CancellationToken ct = default);
    Task SetSdr10BitAsync(bool enabled, CancellationToken ct = default);
    Task SetCustomEdidAsync(bool enabled, CancellationToken ct = default);
    Task SetPreventSpoofAsync(bool enabled, CancellationToken ct = default);
    Task SetCeaOverrideAsync(bool enabled, CancellationToken ct = default);
    Task SetHardwareCursorAsync(bool enabled, CancellationToken ct = default);

    Task SetDebugLoggingAsync(bool enabled, CancellationToken ct = default);
    Task SetLoggingAsync(bool enabled, CancellationToken ct = default);

    Task<string> GetD3DDeviceGpuAsync(CancellationToken ct = default);
    Task<string> GetIddCxVersionAsync(CancellationToken ct = default);
    Task<string> GetAssignedGpuAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllGpusAsync(CancellationToken ct = default);
    Task SetGpuAsync(string gpuName, CancellationToken ct = default);
}
