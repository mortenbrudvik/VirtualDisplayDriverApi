using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VirtualDisplayDriver.Pipe;

public class VddPipeClient : IVddPipeClient
{
    private readonly VirtualDisplayOptions _options;
    private readonly ILogger<VddPipeClient> _logger;

    public VddPipeClient(VirtualDisplayOptions options, ILogger<VddPipeClient>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<VddPipeClient>.Instance;
    }

    public Task<string> PingAsync(CancellationToken ct = default)
        => SendUtf8CommandAsync("PING", ct);

    public Task<string> SetDisplayCountAsync(int count, CancellationToken ct = default)
        => SendUtf8CommandAsync($"SETDISPLAYCOUNT {count}", ct);

    public async Task<string> GetSettingsAsync(CancellationToken ct = default)
    {
        var responseBytes = await SendCommandRawAsync("GETSETTINGS", ct);
        return Encoding.Unicode.GetString(responseBytes).TrimEnd('\0').Trim();
    }

    public Task<string> SendToggleAsync(string command, bool value, CancellationToken ct = default)
        => SendUtf8CommandAsync($"{command} {(value ? "true" : "false")}", ct);

    public Task<string> SendQueryAsync(string command, CancellationToken ct = default)
        => SendUtf8CommandAsync(command, ct);

    public Task<string> SetGpuAsync(string gpuName, CancellationToken ct = default)
    {
        if (gpuName.AsSpan().IndexOfAny(['"', '\n', '\r', '\0']) >= 0)
            throw new ArgumentException(
                "GPU name contains invalid characters (quotes, newlines, or null).", nameof(gpuName));
        return SendUtf8CommandAsync($"SETGPU \"{gpuName}\"", ct);
    }

    private async Task<string> SendUtf8CommandAsync(string command, CancellationToken ct)
    {
        var responseBytes = await SendCommandRawAsync(command, ct);
        return Encoding.UTF8.GetString(responseBytes).TrimEnd('\0').Trim();
    }

    private async Task<byte[]> SendCommandRawAsync(string command, CancellationToken ct)
    {
        _logger.LogDebug("Sending command: {Command}", command);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_options.ConnectTimeout);

            await pipe.ConnectAsync(connectCts.Token);

            if (command.Length > PipeConstants.MaxCommandLengthChars)
                throw new ArgumentException(
                    $"Command exceeds maximum length of {PipeConstants.MaxCommandLengthChars} characters (was {command.Length}). The VDD driver silently truncates longer commands.",
                    nameof(command));

            var payload = Encoding.Unicode.GetBytes(command);
            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            writeCts.CancelAfter(_options.WriteTimeout);
            await pipe.WriteAsync(payload, writeCts.Token);
            await pipe.FlushAsync(writeCts.Token);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(_options.ReadTimeout);

            using var ms = new MemoryStream();
            var buffer = new byte[PipeConstants.ReadBufferSize];
            int bytesRead;
            while ((bytesRead = await pipe.ReadAsync(buffer, readCts.Token)) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
                if (ms.Length > PipeConstants.MaxResponseSize)
                    throw new PipeConnectionException(
                        $"Response exceeded maximum size of {PipeConstants.MaxResponseSize} bytes for command: {command}");
            }

            _logger.LogDebug("Command {Command} completed, received {ByteCount} bytes", command, ms.Length);
            return ms.ToArray();
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout for command: {Command}", command);
            throw new PipeConnectionException(
                $"VDD pipe operation timed out for command: {command}", ex);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Pipe I/O error for command: {Command}", command);
            throw new PipeConnectionException(
                $"Pipe communication failed for command: {command}", ex);
        }
    }
}
