# VDD API Wrapper — Design Spec

A C#/.NET API wrapper library for the Virtual Display Driver named-pipe protocol, targeting the broader .NET community as a public-quality library.

---

## Decisions

| Decision | Choice |
|---|---|
| Target framework | `net8.0-windows` (LTS) |
| Abstraction level | Two layers: low-level pipe client + high-level manager |
| Async model | Async-only (`Task`-based) |
| Thread safety | Built-in serialization at the high-level layer; consumer responsibility at low-level |
| Error handling | Custom exceptions (idiomatic C#) |
| DI support | Standalone + optional `IServiceCollection` extensions |
| Project structure | Single project, namespace-separated (Approach C) |

---

## Project Structure

```
VirtualDisplayDriver/
├── VirtualDisplayDriver.csproj          # net8.0-windows, single assembly
├── Pipe/                                # Low-level layer
│   ├── IVddPipeClient.cs               # Interface: raw pipe operations
│   ├── VddPipeClient.cs                # Implementation: one-shot pipe connections
│   └── PipeConstants.cs                 # Pipe name, read buffer size (512 bytes)
├── IVirtualDisplayManager.cs            # Interface: high-level API
├── VirtualDisplayManager.cs             # Implementation: state tracking, safety
├── Models/
│   └── DriverSettings.cs                # Parsed GETSETTINGS response
├── Exceptions/
│   ├── VddException.cs                  # Base exception
│   ├── PipeConnectionException.cs       # Driver not available / connect timeout
│   └── CommandException.cs              # Command failed / unexpected response
└── DependencyInjection/
    └── ServiceCollectionExtensions.cs   # AddVirtualDisplayDriver() extensions
```

**Namespace mapping:**
- `VirtualDisplayDriver` — high-level manager, models, exceptions
- `VirtualDisplayDriver.Pipe` — low-level pipe client
- `VirtualDisplayDriver.DependencyInjection` — DI extensions

---

## Low-Level Pipe Client

```csharp
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
```

### Behaviors

- Each method opens a fresh `NamedPipeClientStream` to `\\.\pipe\MTTVirtualDisplayPipe`, sends one command encoded as UTF-16LE, reads until disconnect, and closes.
- Response decoding is per-command: UTF-16LE for `GETSETTINGS`, UTF-8 for everything else. For `GETSETTINGS`, the implementation accumulates raw bytes (e.g., into a `MemoryStream`) and decodes the complete byte array as UTF-16LE after the read loop completes, avoiding split-character corruption. All other commands decode UTF-8 chunks incrementally.
- Connection timeout received via `VirtualDisplayOptions` (constructor-injected). Default: 10 seconds.
- Optional `ILogger<VddPipeClient>` for diagnostic logging (connection attempts, failures, command sent/received). Defaults to `NullLogger` if not provided.
- No `IAsyncDisposable` — the client is stateless; each call opens and closes its own pipe connection. No resources to dispose.
- Throws `PipeConnectionException` on `IOException` or connect timeout, `CommandException` on unexpected failures.
- The pipe client handles command string construction — formatting parameters into wire strings (e.g., `SetDisplayCountAsync(3)` sends `"SETDISPLAYCOUNT 3"`, `SendToggleAsync("HDRPLUS", true)` sends `"HDRPLUS true"`) — but does not validate the semantic correctness of values.

### What it does NOT do

- No command serialization (no semaphore).
- No reload spacing enforcement.
- No response parsing — returns raw strings.
- No display count tracking.
- No command validation — the consumer is responsible for passing correct command names to `SendToggleAsync` and `SendQueryAsync`. The driver uses prefix matching (`wcsncmp`), so invalid command strings like `"HDRPLUS_EXTRA"` could match the `HDRPLUS` handler. Use the high-level manager for a safe API.
- Does not expose `RELOAD_DRIVER` (known crash bug).

### GPU name quoting

`SetGpuAsync(string gpuName)` automatically wraps the GPU name in quotes before sending. The caller passes the plain name (e.g., `"NVIDIA GeForce RTX 4090"`), and the client sends `SETGPU "NVIDIA GeForce RTX 4090"`.

---

## High-Level Virtual Display Manager

```csharp
namespace VirtualDisplayDriver;

public interface IVirtualDisplayManager : IAsyncDisposable
{
    int DisplayCount { get; }
    bool IsConnected { get; }
    IVddPipeClient PipeClient { get; }

    Task<bool> PingAsync(CancellationToken ct = default);

    Task SetDisplayCountAsync(int count, CancellationToken ct = default);
    Task AddDisplaysAsync(int count = 1, CancellationToken ct = default);
    Task RemoveDisplaysAsync(int count = 1, CancellationToken ct = default);
    Task RemoveAllDisplaysAsync(CancellationToken ct = default);

    Task<DriverSettings> GetSettingsAsync(CancellationToken ct = default);

    // Toggle commands that trigger ReloadDriver
    Task SetHdrPlusAsync(bool enabled, CancellationToken ct = default);
    Task SetSdr10BitAsync(bool enabled, CancellationToken ct = default);
    Task SetCustomEdidAsync(bool enabled, CancellationToken ct = default);
    Task SetPreventSpoofAsync(bool enabled, CancellationToken ct = default);
    Task SetCeaOverrideAsync(bool enabled, CancellationToken ct = default);
    Task SetHardwareCursorAsync(bool enabled, CancellationToken ct = default);

    // Toggle commands that do NOT trigger ReloadDriver (no reload spacing)
    Task SetDebugLoggingAsync(bool enabled, CancellationToken ct = default);
    Task SetLoggingAsync(bool enabled, CancellationToken ct = default);

    Task<string> GetD3DDeviceGpuAsync(CancellationToken ct = default);
    Task<string> GetIddCxVersionAsync(CancellationToken ct = default);
    Task<string> GetAssignedGpuAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllGpusAsync(CancellationToken ct = default);
    Task SetGpuAsync(string gpuName, CancellationToken ct = default);
}
```

### Command-to-wire mapping

Each high-level method maps to an exact wire command string sent through the pipe client:

| Manager method | Wire command | Triggers ReloadDriver |
|---|---|---|
| `SetDisplayCountAsync(3)` | `SETDISPLAYCOUNT 3` | Yes |
| `AddDisplaysAsync(1)` | `SETDISPLAYCOUNT {DisplayCount + 1}` | Yes |
| `RemoveDisplaysAsync(1)` | `SETDISPLAYCOUNT {max(0, DisplayCount - 1)}` | Yes |
| `RemoveAllDisplaysAsync()` | `SETDISPLAYCOUNT 0` | Yes |
| `SetHdrPlusAsync(true)` | `HDRPLUS true` | Yes |
| `SetSdr10BitAsync(true)` | `SDR10 true` | Yes |
| `SetCustomEdidAsync(true)` | `CUSTOMEDID true` | Yes |
| `SetPreventSpoofAsync(true)` | `PREVENTSPOOF true` | Yes |
| `SetCeaOverrideAsync(true)` | `CEAOVERRIDE true` | Yes |
| `SetHardwareCursorAsync(true)` | `HARDWARECURSOR true` | Yes |
| `SetGpuAsync("name")` | `SETGPU "name"` | Yes |
| `SetDebugLoggingAsync(true)` | `LOG_DEBUG true` | No |
| `SetLoggingAsync(true)` | `LOGGING true` | No |
| `PingAsync()` | `PING` | No |
| `GetSettingsAsync()` | `GETSETTINGS` | No |
| `GetD3DDeviceGpuAsync()` | `D3DDEVICEGPU` | No |
| `GetIddCxVersionAsync()` | `IDDCXVERSION` | No |
| `GetAssignedGpuAsync()` | `GETASSIGNEDGPU` | No |
| `GetAllGpusAsync()` | `GETALLGPUS` | No |

Since the manager always passes valid boolean values to toggle commands, all toggle commands marked "Yes" will always trigger reload.

### Behaviors

- **Command serialization**: Internal `SemaphoreSlim(1,1)` ensures one command at a time across all methods.
- **Reload spacing**: Commands marked "Triggers ReloadDriver: Yes" in the table above enforce a configurable minimum delay since the *completion* of the last reload-triggering command (i.e., after the pipe read-until-disconnect finishes and the driver is in a stable state). Default: 30 seconds. When a reload-triggering command is called within the spacing window, the manager **awaits the remaining time** (`Task.Delay`) before proceeding, honoring the caller's `CancellationToken`. This is transparent to the caller — no exception, no retry logic needed. `SetDebugLoggingAsync` and `SetLoggingAsync` do NOT trigger reload and are excluded from reload spacing.
- **Local display count tracking**: `DisplayCount` maintained in memory, updated on Set/Add/Remove calls. Can become stale if another client or driver restart changes the count externally.
- **Response parsing**: Raw pipe strings parsed into typed models (`DriverSettings`, GPU name lists). See "Response Parsing" section below.
- **Connection state**: `IsConnected` reflects the outcome of the most recently completed command, not a live connection (the protocol is one-shot with no persistent connection). Defaults to `false`. Set to `true` after any successful command, `false` after any caught pipe exception. Use `PingAsync()` for an active health check.
- **Raw client access**: `PipeClient` exposes the underlying `IVddPipeClient` for advanced users who need escape-hatch access to raw pipe operations (e.g., custom response parsing, unsupported commands).
- **RELOAD_DRIVER excluded**: Not exposed since it causes driver crashes.

### Initial state

- `DisplayCount` starts at `InitialDisplayCount` from `VirtualDisplayOptions` (default `0`). Consumers who know their current monitor count can seed it at construction time. This may still not reflect reality if another client changes the count externally. There is no protocol command to query the actual count.
- `IsConnected` starts at `false` until the first successful command.

### Display count validation

- `SetDisplayCountAsync(count)`: `count` must be >= 0. Throws `ArgumentOutOfRangeException` for negative values. No enforced upper bound (the driver handles excess by repeating the last XML profile).
- `AddDisplaysAsync(count)`: `count` must be > 0. Throws `ArgumentOutOfRangeException` for zero or negative values.
- `RemoveDisplaysAsync(count)`: `count` must be > 0. Throws `ArgumentOutOfRangeException` for zero or negative values. Clamps the result to 0 (never goes negative) — e.g., `RemoveDisplaysAsync(5)` when `DisplayCount` is 2 sends `SETDISPLAYCOUNT 0`.

### Ping behavior

`PingAsync` is a safe health check. If the pipe connection fails (`PipeConnectionException`), it catches the exception, sets `IsConnected = false`, and returns `false`. If the pipe communication succeeds, it sets `IsConnected = true` and returns `true` if the response contains the substring `"PONG"`, `false` otherwise. A non-PONG response with a successful pipe connection still sets `IsConnected = true` because the pipe communication succeeded — `PingAsync` returning `false` indicates an unexpected response, not a connection failure. The protocol reference notes that PING may return additional log data beyond "PONG" when `SendLogsThroughPipe` is enabled — the substring check handles this.

### GPU name quoting

`SetGpuAsync(string gpuName)` passes the plain GPU name to the pipe client, which handles quoting. Throws `ArgumentException` if `gpuName` is null, empty, or whitespace.

### Disposal

`DisposeAsync` does not wait for in-flight commands to complete. It disposes the semaphore immediately, which causes any callers currently waiting on the semaphore to receive `ObjectDisposedException`. A command that has already acquired the semaphore will complete but may fail if it attempts pipe operations after disposal. After disposal, all new method calls throw `ObjectDisposedException`.

---

## Response Parsing

The driver responds to most commands with log messages, not structured data. Parsing is best-effort based on observed log line patterns.

### GETSETTINGS

Response format: `SETTINGS DEBUG=true|false LOG=true|false` (UTF-16LE, may include null terminator).

Parsing: Strip any trailing null characters (`\0`) from the decoded UTF-16LE string, then regex match `DEBUG=(\w+)\s+LOG=(\w+)`. If the response does not match, throws `CommandException` with the raw response included in the exception message.

### PING

Response: `"PONG"` (UTF-8). May include additional log lines if `SendLogsThroughPipe` is enabled. Check for substring `"PONG"`.

### GPU queries

Query commands (`D3DDEVICEGPU`, `IDDCXVERSION`, `GETASSIGNEDGPU`, `GETALLGPUS`) return UTF-8 log lines through the log-through-pipe mechanism. Parsing heuristics:

| Command | Parsing approach | Empty result behavior |
|---|---|---|
| `GETALLGPUS` | Split by newlines, extract lines containing `"GPU:"`, take the substring after the **last** occurrence of `"GPU:"` (using `LastIndexOf` to handle log prefixes that may contain "GPU:" earlier in the line). Returns `IReadOnlyList<string>`. | Returns empty list if no lines match. |
| `GETASSIGNEDGPU` | Same line-matching approach, return the first match as a string. | Throws `CommandException` if no line matches (an assigned GPU should always exist if the driver is running). |
| `IDDCXVERSION` | Return the full trimmed response string (version info is in the log output). | Throws `CommandException` if response is empty. |
| `D3DDEVICEGPU` | Return the full trimmed response string (GPU info is in the log output). | Throws `CommandException` if response is empty. |

These heuristics are based on observed driver behavior and the parsing hints in the protocol reference. If the driver's log format changes, parsing may need to be updated. The raw pipe client is always available as a fallback for consumers who need to parse responses themselves.

---

## Models

```csharp
namespace VirtualDisplayDriver;

public record DriverSettings(bool DebugLogging, bool Logging);
```

`DriverSettings` maps to `SETTINGS DEBUG=true|false LOG=true|false`. Parsed from the GETSETTINGS response.

---

## Exceptions

```csharp
namespace VirtualDisplayDriver;

public class VddException : Exception                    // Base
public class PipeConnectionException : VddException      // Driver not available / timeout
public class CommandException : VddException              // Unexpected response / pipe broke / parse failure
```

### Exception mapping

| Failure | Exception thrown |
|---|---|
| `IOException` (pipe broken / not found) | `PipeConnectionException` |
| `OperationCanceledException` (connect timeout) | `PipeConnectionException` |
| Unexpected / unparseable response | `CommandException` (includes raw response in message) |
| Empty response from query command | `CommandException` |
| `GETSETTINGS` response does not match expected format | `CommandException` |
| Caller's own `CancellationToken` cancelled | `OperationCanceledException` (passed through, not wrapped) |

`DriverReloadException` has been removed. The driver communicates reload results through unstructured log messages, and there is no reliable way to detect reload failure at the protocol level. Pipe-level failures during reload-triggering commands surface as `PipeConnectionException` (if the pipe breaks) or succeed silently (if the driver responds normally). The reload spacing mechanism prevents rapid successive reloads proactively rather than reactively.

---

## Configuration & DI

### Options

```csharp
namespace VirtualDisplayDriver;

public class VirtualDisplayOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReloadSpacing { get; set; } = TimeSpan.FromSeconds(30);
    public int InitialDisplayCount { get; set; }
}
```

### DI extensions

```csharp
namespace VirtualDisplayDriver.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualDisplayDriver(
        this IServiceCollection services,
        Action<VirtualDisplayOptions>? configure = null);
}
```

- `IVddPipeClient` registered as singleton (stateless, reusable).
- `IVirtualDisplayManager` registered as singleton (owns semaphore and display count state).
- Options bound via `IOptions<VirtualDisplayOptions>`.
- Dependencies: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Logging.Abstractions`.

### Constructor signatures

Both `VddPipeClient` and `VirtualDisplayManager` accept `VirtualDisplayOptions` directly in their constructors (for standalone usage). The DI extension unwraps `IOptions<VirtualDisplayOptions>.Value` when constructing them.

```csharp
public VddPipeClient(VirtualDisplayOptions options, ILogger<VddPipeClient>? logger = null)
public VirtualDisplayManager(IVddPipeClient client, VirtualDisplayOptions options, ILogger<VirtualDisplayManager>? logger = null)
```

Both accept optional `ILogger<T>` parameters. When null, logging is silently disabled (`NullLogger`). The DI extension wires up loggers automatically from the container.

### Standalone usage

```csharp
var options = new VirtualDisplayOptions();
var client = new VddPipeClient(options);
var manager = new VirtualDisplayManager(client, options);

// With logging:
var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var client = new VddPipeClient(options, loggerFactory.CreateLogger<VddPipeClient>());
var manager = new VirtualDisplayManager(client, options, loggerFactory.CreateLogger<VirtualDisplayManager>());
```

---

## Testing Strategy

### Unit tests

- Response parsing (DriverSettings, GPU names, edge cases, malformed responses, empty responses).
- Reload spacing logic (auto-delay behavior, cancellation token honored during delay, timer measured from command completion).
- Display count tracking (Add/Remove/Set, boundary clamping, validation).
- Encoding (UTF-16LE commands, per-command response decoding).
- Manager tested with mocked `IVddPipeClient`.

### Integration tests

- Require VDD driver installed and running (opt-in, not CI).
- PING, GETSETTINGS, query command round-trips.
- Avoid `SETDISPLAYCOUNT` in tests (heavyweight reload).

### Framework

- xUnit + FluentAssertions.
- NSubstitute for mocking.

---

## Protocol Reference

This library wraps the MttVDD named-pipe protocol (17 commands). Key protocol constraints:

- **One-shot connections**: Each command requires a fresh pipe connection.
- **Encoding asymmetry**: Commands sent as UTF-16LE; responses UTF-8 except GETSETTINGS (UTF-16LE).
- **No display count query**: Clients must track count locally.
- **RELOAD_DRIVER is broken**: Causes crashes due to type mismatch (pipe handle vs WDF object). Use SETDISPLAYCOUNT instead.
- **Reload crash risk**: Rapid successive reload-triggering commands can destabilize the driver. Minimum 30-second spacing recommended.
- **Prefix matching**: Driver uses `wcsncmp`, so trailing text in commands can match unintended handlers.
- **Buffer limit**: 127 wide characters (254 bytes) max per command.

See `docs/named-pipe-api-reference.md` and `docs/named-pipe-technical-deep-dive.md` for full protocol documentation.
