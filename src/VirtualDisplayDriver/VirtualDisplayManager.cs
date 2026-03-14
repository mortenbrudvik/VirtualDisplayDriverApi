using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver;

public class VirtualDisplayManager : IVirtualDisplayManager
{
    private readonly IVddPipeClient _client;
    private readonly VirtualDisplayOptions _options;
    private readonly ILogger<VirtualDisplayManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private DateTime _lastReloadComplete = DateTime.MinValue;
    private bool _disposed;

    public int DisplayCount { get; private set; }
    public bool IsConnected { get; private set; }
    public IVddPipeClient PipeClient => _client;

    public VirtualDisplayManager(
        IVddPipeClient client,
        VirtualDisplayOptions options,
        ILogger<VirtualDisplayManager>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<VirtualDisplayManager>.Instance;
        DisplayCount = _options.InitialDisplayCount;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            var response = await _client.PingAsync(ct);
            IsConnected = true;
            return response.Contains("PONG");
        }
        catch (PipeConnectionException)
        {
            IsConnected = false;
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SetDisplayCountAsync(int count, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        await ExecuteDisplayCountCommandAsync(count, ct);
    }

    public async Task AddDisplaysAsync(int count = 1, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        await ExecuteDisplayCountCommandAsync(currentCount => currentCount + count, ct);
    }

    public async Task RemoveDisplaysAsync(int count = 1, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        await ExecuteDisplayCountCommandAsync(currentCount => Math.Max(0, currentCount - count), ct);
    }

    public async Task RemoveAllDisplaysAsync(CancellationToken ct = default)
    {
        await ExecuteDisplayCountCommandAsync(0, ct);
    }

    public async Task<DriverSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            async token => await _client.GetSettingsAsync(token), ct);
        return ParseSettings(response);
    }

    public Task SetHdrPlusAsync(bool enabled, CancellationToken ct = default)
        => ExecuteReloadToggleAsync("HDRPLUS", enabled, ct);

    public Task SetSdr10BitAsync(bool enabled, CancellationToken ct = default)
        => ExecuteReloadToggleAsync("SDR10", enabled, ct);

    public Task SetCustomEdidAsync(bool enabled, CancellationToken ct = default)
        => ExecuteReloadToggleAsync("CUSTOMEDID", enabled, ct);

    public Task SetPreventSpoofAsync(bool enabled, CancellationToken ct = default)
        => ExecuteReloadToggleAsync("PREVENTSPOOF", enabled, ct);

    public Task SetCeaOverrideAsync(bool enabled, CancellationToken ct = default)
        => ExecuteReloadToggleAsync("CEAOVERRIDE", enabled, ct);

    public Task SetHardwareCursorAsync(bool enabled, CancellationToken ct = default)
        => ExecuteReloadToggleAsync("HARDWARECURSOR", enabled, ct);

    public async Task SetDebugLoggingAsync(bool enabled, CancellationToken ct = default)
        => await ExecuteCommandAsync(
            async token => await _client.SendToggleAsync("LOG_DEBUG", enabled, token), ct);

    public async Task SetLoggingAsync(bool enabled, CancellationToken ct = default)
        => await ExecuteCommandAsync(
            async token => await _client.SendToggleAsync("LOGGING", enabled, token), ct);

    public async Task<string> GetD3DDeviceGpuAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            async token => await _client.SendQueryAsync("D3DDEVICEGPU", token), ct);
        if (string.IsNullOrWhiteSpace(response))
            throw new CommandException("D3DDEVICEGPU returned an empty response.");
        return response;
    }

    public async Task<string> GetIddCxVersionAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            async token => await _client.SendQueryAsync("IDDCXVERSION", token), ct);
        if (string.IsNullOrWhiteSpace(response))
            throw new CommandException("IDDCXVERSION returned an empty response.");
        return response;
    }

    public async Task<string> GetAssignedGpuAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            async token => await _client.SendQueryAsync("GETASSIGNEDGPU", token), ct);
        return ParseGpuLine(response)
            ?? throw new CommandException("GETASSIGNEDGPU returned no GPU line.", response);
    }

    public async Task<IReadOnlyList<string>> GetAllGpusAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            async token => await _client.SendQueryAsync("GETALLGPUS", token), ct);
        return ParseGpuLines(response);
    }

    public async Task SetGpuAsync(string gpuName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gpuName);
        await ExecuteReloadCommandAsync(
            async token => await _client.SetGpuAsync(gpuName, token), ct);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    // --- Private helpers ---

    private Task ExecuteDisplayCountCommandAsync(int count, CancellationToken ct)
        => ExecuteDisplayCountCommandAsync(_ => count, ct);

    private async Task ExecuteDisplayCountCommandAsync(
        Func<int, int> computeCount, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            await WaitForReloadSpacingAsync(ct);
            var newCount = computeCount(DisplayCount);
            await _client.SetDisplayCountAsync(newCount, ct);
            _lastReloadComplete = DateTime.UtcNow;
            DisplayCount = newCount;
            IsConnected = true;
        }
        catch (PipeConnectionException)
        {
            IsConnected = false;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string> ExecuteCommandAsync(
        Func<CancellationToken, Task<string>> command, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            var response = await command(ct);
            IsConnected = true;
            return response;
        }
        catch (PipeConnectionException)
        {
            IsConnected = false;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ExecuteReloadCommandAsync(
        Func<CancellationToken, Task<string>> command, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            await WaitForReloadSpacingAsync(ct);
            await command(ct);
            _lastReloadComplete = DateTime.UtcNow;
            IsConnected = true;
        }
        catch (PipeConnectionException)
        {
            IsConnected = false;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WaitForReloadSpacingAsync(CancellationToken ct)
    {
        var elapsed = DateTime.UtcNow - _lastReloadComplete;
        var remaining = _options.ReloadSpacing - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            _logger.LogDebug("Reload spacing: waiting {RemainingMs}ms", remaining.TotalMilliseconds);
            await Task.Delay(remaining, ct);
        }
    }

    private async Task ExecuteReloadToggleAsync(string command, bool value, CancellationToken ct)
    {
        await ExecuteReloadCommandAsync(
            async token => await _client.SendToggleAsync(command, value, token), ct);
    }

    private static DriverSettings ParseSettings(string response)
    {
        var match = Regex.Match(response, @"DEBUG=(\w+)\s+LOG=(\w+)");
        if (!match.Success)
            throw new CommandException(
                $"Failed to parse GETSETTINGS response: {response}", response);

        return new DriverSettings(
            DebugLogging: match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase),
            Logging: match.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ParseGpuLine(string response)
    {
        return response.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("GPU:"))
            .Select(line => line[(line.LastIndexOf("GPU:", StringComparison.Ordinal) + 4)..].Trim())
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> ParseGpuLines(string response)
    {
        return response.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("GPU:"))
            .Select(line => line[(line.LastIndexOf("GPU:", StringComparison.Ordinal) + 4)..].Trim())
            .ToList()
            .AsReadOnly();
    }
}
