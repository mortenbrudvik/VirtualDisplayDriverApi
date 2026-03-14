using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver;

public partial class VirtualDisplayManager : IVirtualDisplayManager
{
    private readonly IVddPipeClient _client;
    private readonly VirtualDisplayOptions _options;
    private readonly ILogger<VirtualDisplayManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private long _lastReloadCompleteTicks;
    private int _disposed;

    public int DisplayCount { get; private set; }
    public bool IsConnected { get; private set; }
    public IVddPipeClient PipeClient => _client;

    public async Task SyncDisplayCountAsync(int count, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            DisplayCount = count;
        }
        finally
        {
            _semaphore.Release();
        }
    }

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _options.MaxDisplayCount);
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
            token => _client.GetSettingsAsync(token), ct);
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

    public Task SetDebugLoggingAsync(bool enabled, CancellationToken ct = default)
        => ExecuteCommandAsync(
            token => _client.SendToggleAsync("LOG_DEBUG", enabled, token), ct);

    public Task SetLoggingAsync(bool enabled, CancellationToken ct = default)
        => ExecuteCommandAsync(
            token => _client.SendToggleAsync("LOGGING", enabled, token), ct);

    public Task<string> GetD3DDeviceGpuAsync(CancellationToken ct = default)
        => ExecuteNonEmptyQueryAsync("D3DDEVICEGPU", ct);

    public Task<string> GetIddCxVersionAsync(CancellationToken ct = default)
        => ExecuteNonEmptyQueryAsync("IDDCXVERSION", ct);

    public async Task<string> GetAssignedGpuAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            token => _client.SendQueryAsync("GETASSIGNEDGPU", token), ct);
        return ParseGpuLine(response)
            ?? throw new CommandException("GETASSIGNEDGPU returned no GPU line.", response);
    }

    public async Task<IReadOnlyList<string>> GetAllGpusAsync(CancellationToken ct = default)
    {
        var response = await ExecuteCommandAsync(
            token => _client.SendQueryAsync("GETALLGPUS", token), ct);
        return ParseGpuLines(response);
    }

    public async Task SetGpuAsync(string gpuName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gpuName);
        await ExecuteReloadCommandAsync(
            token => _client.SetGpuAsync(gpuName, token), ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        _semaphore.Dispose();
    }

    // --- Private helpers ---

    private Task ExecuteDisplayCountCommandAsync(int count, CancellationToken ct)
        => ExecuteDisplayCountCommandAsync(_ => count, ct);

    private async Task ExecuteDisplayCountCommandAsync(
        Func<int, int> computeCount, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            await WaitForReloadSpacingAsync(ct);
            var newCount = computeCount(DisplayCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(newCount, _options.MaxDisplayCount, "computed display count");
            await _client.SetDisplayCountAsync(newCount, ct);
            _lastReloadCompleteTicks = Stopwatch.GetTimestamp();
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _semaphore.WaitAsync(ct);
        try
        {
            await WaitForReloadSpacingAsync(ct);
            await command(ct);
            _lastReloadCompleteTicks = Stopwatch.GetTimestamp();
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
        var elapsed = Stopwatch.GetElapsedTime(_lastReloadCompleteTicks);
        var remaining = _options.ReloadSpacing - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            _logger.LogDebug("Reload spacing: waiting {RemainingMs}ms", remaining.TotalMilliseconds);
            await Task.Delay(remaining, ct);
        }
    }

    private Task ExecuteReloadToggleAsync(string command, bool value, CancellationToken ct)
        => ExecuteReloadCommandAsync(
            token => _client.SendToggleAsync(command, value, token), ct);

    private async Task<string> ExecuteNonEmptyQueryAsync(string command, CancellationToken ct)
    {
        var response = await ExecuteCommandAsync(token => _client.SendQueryAsync(command, token), ct);
        if (string.IsNullOrWhiteSpace(response))
            throw new CommandException($"{command} returned an empty response.");
        return response;
    }

    [GeneratedRegex(@"DEBUG=(\w+)\s+LOG=(\w+)")]
    private static partial Regex SettingsPattern();

    private static DriverSettings ParseSettings(string response)
    {
        var match = SettingsPattern().Match(response);
        if (!match.Success)
            throw new CommandException(
                $"Failed to parse GETSETTINGS response: {response}", response);

        return new DriverSettings(
            DebugLogging: match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase),
            Logging: match.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExtractGpuNames(string response)
    {
        return response.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("GPU:", StringComparison.Ordinal))
            .Select(line => line[(line.LastIndexOf("GPU:", StringComparison.Ordinal) + 4)..].Trim());
    }

    private static string? ParseGpuLine(string response)
        => ExtractGpuNames(response).FirstOrDefault();

    private static IReadOnlyList<string> ParseGpuLines(string response)
        => ExtractGpuNames(response).ToArray();
}
