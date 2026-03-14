namespace VirtualDisplayDriver.Pipe;

public interface IVddPipeClient
{
    Task<string> PingAsync(CancellationToken ct = default);
    Task<string> SetDisplayCountAsync(int count, CancellationToken ct = default);
    Task<string> GetSettingsAsync(CancellationToken ct = default);
    Task<string> SendToggleAsync(string command, bool value, CancellationToken ct = default);
    Task<string> SendQueryAsync(string command, CancellationToken ct = default);
    Task<string> SetGpuAsync(string gpuName, CancellationToken ct = default);
}
