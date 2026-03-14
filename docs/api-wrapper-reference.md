# VDD API Wrapper Reference

API reference for the C#/.NET wrapper library that provides safe, typed access to the [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) named-pipe protocol.

---

## IVirtualDisplayManager

The primary API for controlling virtual displays. Provides command serialization, automatic reload spacing, display count tracking, and response parsing.

Implements `IAsyncDisposable`. Always use `await using` or call `DisposeAsync()` when done.

### Properties

| Property | Type | Description |
|---|---|---|
| `DisplayCount` | `int` | Current local display count. Starts at `InitialDisplayCount` (default 0). Updated on Set/Add/Remove calls. May become stale if another client changes the count externally. |
| `IsConnected` | `bool` | Reflects the outcome of the most recently completed command. `true` after any successful command, `false` after a pipe failure. Not a live connection — use `PingAsync()` for an active health check. |
| `PipeClient` | `IVddPipeClient` | Escape hatch to the underlying low-level pipe client for custom commands or raw response parsing. |

### Display Management

#### PingAsync

```csharp
Task<bool> PingAsync(CancellationToken ct = default)
```

Safe health check. Returns `true` if the driver responds with `PONG`, `false` if the driver is unreachable or returns an unexpected response. Never throws for connection failures — catches `PipeConnectionException` internally.

```csharp
if (await manager.PingAsync())
    Console.WriteLine("Driver is running");
else
    Console.WriteLine("Driver not available");
```

#### SetDisplayCountAsync

```csharp
Task SetDisplayCountAsync(int count, CancellationToken ct = default)
```

Set the total number of active virtual monitors. Triggers a driver reload.

- `count` must be >= 0 (`ArgumentOutOfRangeException` for negative values)
- No upper bound enforced — the driver repeats the last XML profile for excess monitors

```csharp
await manager.SetDisplayCountAsync(3); // Activate 3 monitors
await manager.SetDisplayCountAsync(0); // Remove all monitors
```

#### AddDisplaysAsync

```csharp
Task AddDisplaysAsync(int count = 1, CancellationToken ct = default)
```

Add monitors by incrementing the current `DisplayCount`. Triggers a driver reload.

- `count` must be > 0 (`ArgumentOutOfRangeException` for zero or negative)

```csharp
await manager.AddDisplaysAsync();    // Add 1
await manager.AddDisplaysAsync(3);   // Add 3
```

#### RemoveDisplaysAsync

```csharp
Task RemoveDisplaysAsync(int count = 1, CancellationToken ct = default)
```

Remove monitors by decrementing the current `DisplayCount`. Clamps to 0 — never goes negative. Triggers a driver reload.

- `count` must be > 0 (`ArgumentOutOfRangeException` for zero or negative)

```csharp
await manager.RemoveDisplaysAsync();    // Remove 1
await manager.RemoveDisplaysAsync(10);  // Removes all (clamps to 0)
```

#### RemoveAllDisplaysAsync

```csharp
Task RemoveAllDisplaysAsync(CancellationToken ct = default)
```

Remove all virtual monitors (sends `SETDISPLAYCOUNT 0`). Triggers a driver reload.

### Settings & Configuration

#### GetSettingsAsync

```csharp
Task<DriverSettings> GetSettingsAsync(CancellationToken ct = default)
```

Query the driver's current debug/logging settings. Returns a parsed `DriverSettings` record.

Throws `CommandException` if the response format is unexpected.

```csharp
var settings = await manager.GetSettingsAsync();
Console.WriteLine($"Debug: {settings.DebugLogging}, Logging: {settings.Logging}");
```

#### Toggle Commands

These methods update a driver setting and persist it to `vdd_settings.xml`.

**Commands that trigger a driver reload** (subject to reload spacing):

| Method | Wire command | Effect |
|---|---|---|
| `SetHdrPlusAsync(bool)` | `HDRPLUS true\|false` | Toggle HDR Plus mode |
| `SetSdr10BitAsync(bool)` | `SDR10 true\|false` | Toggle 10-bit SDR color |
| `SetCustomEdidAsync(bool)` | `CUSTOMEDID true\|false` | Toggle custom EDID profiles |
| `SetPreventSpoofAsync(bool)` | `PREVENTSPOOF true\|false` | Toggle spoof prevention |
| `SetCeaOverrideAsync(bool)` | `CEAOVERRIDE true\|false` | Toggle EDID CEA block override |
| `SetHardwareCursorAsync(bool)` | `HARDWARECURSOR true\|false` | Toggle hardware cursor support |

**Commands that do NOT trigger a reload** (no spacing delay):

| Method | Wire command | Effect |
|---|---|---|
| `SetDebugLoggingAsync(bool)` | `LOG_DEBUG true\|false` | Toggle debug logging |
| `SetLoggingAsync(bool)` | `LOGGING true\|false` | Toggle general logging |

```csharp
await manager.SetHdrPlusAsync(true);        // Enable HDR+ (triggers reload)
await manager.SetDebugLoggingAsync(false);   // Disable debug logging (no reload)
```

### GPU Management

#### GetD3DDeviceGpuAsync

```csharp
Task<string> GetD3DDeviceGpuAsync(CancellationToken ct = default)
```

Initialize a D3D device and return GPU information. Throws `CommandException` if the response is empty.

#### GetIddCxVersionAsync

```csharp
Task<string> GetIddCxVersionAsync(CancellationToken ct = default)
```

Query the IddCx framework version. Throws `CommandException` if the response is empty.

#### GetAssignedGpuAsync

```csharp
Task<string> GetAssignedGpuAsync(CancellationToken ct = default)
```

Query the currently assigned GPU name. Throws `CommandException` if no GPU line is found in the response.

#### GetAllGpusAsync

```csharp
Task<IReadOnlyList<string>> GetAllGpusAsync(CancellationToken ct = default)
```

List all available GPU names. Returns an empty list if no GPUs are found.

```csharp
var gpus = await manager.GetAllGpusAsync();
foreach (var gpu in gpus)
    Console.WriteLine(gpu);
```

#### SetGpuAsync

```csharp
Task SetGpuAsync(string gpuName, CancellationToken ct = default)
```

Select which GPU hosts the virtual displays. Triggers a driver reload. The GPU name is automatically quoted in the wire command.

- Throws `ArgumentException` for null, empty, or whitespace names

```csharp
var gpus = await manager.GetAllGpusAsync();
await manager.SetGpuAsync(gpus[0]);
```

---

## IVddPipeClient

Low-level pipe client for direct named-pipe communication. Each method opens a fresh pipe connection, sends one command, reads until the driver disconnects, and returns the raw response string.

No serialization, no reload spacing, no response parsing. Use this when you need raw access or the high-level API doesn't cover your use case. Access it via `manager.PipeClient`.

| Method | Wire command | Response encoding |
|---|---|---|
| `PingAsync()` | `PING` | UTF-8 |
| `SetDisplayCountAsync(int)` | `SETDISPLAYCOUNT N` | UTF-8 |
| `GetSettingsAsync()` | `GETSETTINGS` | UTF-16LE |
| `SendToggleAsync(string, bool)` | `{command} true\|false` | UTF-8 |
| `SendQueryAsync(string)` | `{command}` | UTF-8 |
| `SetGpuAsync(string)` | `SETGPU "{name}"` | UTF-8 |

```csharp
// Using the low-level client directly
var raw = await manager.PipeClient.SendQueryAsync("GETALLGPUS");
Console.WriteLine($"Raw response: {raw}");
```

---

## VirtualDisplayOptions

Configuration for both the pipe client and the manager.

```csharp
public class VirtualDisplayOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReloadSpacing { get; set; } = TimeSpan.FromSeconds(30);
    public int InitialDisplayCount { get; set; } = 0;
}
```

| Property | Default | Description |
|---|---|---|
| `ConnectTimeout` | 10 seconds | Maximum time to wait when connecting to the driver pipe. |
| `ReloadSpacing` | 30 seconds | Minimum delay between commands that trigger a driver reload. The manager auto-waits if called too soon. |
| `InitialDisplayCount` | 0 | Starting value for `DisplayCount`. Set this if you know how many monitors are already active. |

---

## DriverSettings

```csharp
public record DriverSettings(bool DebugLogging, bool Logging);
```

Parsed from the `GETSETTINGS` response. Contains the two logging flags the driver reports:

- `DebugLogging` — Whether debug logging is enabled (maps to `DEBUG=true|false`)
- `Logging` — Whether general logging is enabled (maps to `LOG=true|false`)

---

## Exceptions

All library exceptions inherit from `VddException`.

```
Exception
  └── VddException                    (base for all VDD errors)
        ├── PipeConnectionException   (driver unavailable or timeout)
        └── CommandException          (unexpected response or parse failure)
```

### VddException

Base exception. Thrown directly only if neither subclass applies.

### PipeConnectionException

The driver pipe is not available, the connection timed out, or the pipe broke mid-communication.

| Cause | Original exception |
|---|---|
| Driver not installed or not running | `IOException` (wrapped) |
| Connect timeout expired | `OperationCanceledException` (wrapped) |
| Pipe disconnected unexpectedly | `IOException` (wrapped) |

### CommandException

The command succeeded at the pipe level but the response was unexpected or unparseable.

Has a `RawResponse` property containing the raw response string (when available).

| Cause | Example |
|---|---|
| `GETSETTINGS` response doesn't match expected format | `"Failed to parse GETSETTINGS response: ..."` |
| Query command returned empty response | `"IDDCXVERSION returned an empty response."` |
| GPU query returned no GPU line | `"GETASSIGNEDGPU returned no GPU line."` |

### Cancellation

The caller's own `CancellationToken` is never wrapped. If you cancel via your token, you get a standard `OperationCanceledException` — not a `PipeConnectionException`.

---

## Dependency Injection

```csharp
using VirtualDisplayDriver.DependencyInjection;

// Basic registration
services.AddVirtualDisplayDriver();

// With configuration
services.AddVirtualDisplayDriver(opts =>
{
    opts.ConnectTimeout = TimeSpan.FromSeconds(5);
    opts.ReloadSpacing = TimeSpan.FromSeconds(60);
    opts.InitialDisplayCount = 2;
});
```

Registers:
- `IVddPipeClient` as **singleton** (stateless, reusable)
- `IVirtualDisplayManager` as **singleton** (owns semaphore and display count state)

Both receive `ILogger<T>` from the container automatically when available.

---

## Error Handling

```csharp
try
{
    await manager.SetDisplayCountAsync(2);
}
catch (PipeConnectionException ex)
{
    // Driver not running or pipe broken
    Console.WriteLine($"Connection failed: {ex.Message}");
}
catch (CommandException ex)
{
    // Unexpected response
    Console.WriteLine($"Command failed: {ex.Message}");
    if (ex.RawResponse is not null)
        Console.WriteLine($"Raw: {ex.RawResponse}");
}
```

`PingAsync` is special — it catches `PipeConnectionException` internally and returns `false` instead of throwing. All other methods let exceptions propagate.

---

## Thread Safety & Reload Spacing

### Command serialization

All methods on `IVirtualDisplayManager` are serialized via an internal semaphore. Only one command is in-flight at a time, even from multiple threads.

### Reload spacing

Commands that trigger a driver reload (`SetDisplayCount`, `AddDisplays`, `RemoveDisplays`, `RemoveAllDisplays`, `SetHdrPlus`, `SetSdr10Bit`, `SetCustomEdid`, `SetPreventSpoof`, `SetCeaOverride`, `SetHardwareCursor`, `SetGpu`) enforce a minimum delay since the **completion** of the last reload-triggering command.

When called within the spacing window, the manager automatically awaits the remaining time before proceeding. This is transparent — no exception, no retry needed. The delay honors the caller's `CancellationToken`.

Commands that do NOT trigger a reload (`Ping`, `GetSettings`, `SetDebugLogging`, `SetLogging`, `GetD3DDeviceGpu`, `GetIddCxVersion`, `GetAssignedGpu`, `GetAllGpus`) execute immediately with no spacing delay.

### Low-level client

`IVddPipeClient` has **no serialization and no reload spacing**. If you use the low-level client directly, you are responsible for preventing concurrent commands and spacing reload-triggering operations.

---

## Known Limitations

- **No display count query**: The pipe protocol has no command to query how many monitors are active. `DisplayCount` is tracked locally and can become stale if another client or a driver restart changes the count.
- **RELOAD_DRIVER is broken**: The upstream driver's `RELOAD_DRIVER` command has a [known crash bug](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/351). This library does not expose it. Use `SetDisplayCountAsync` instead.
- **Responses are log messages**: Most driver responses are interleaved log output, not structured data. Parsing is best-effort based on observed patterns.
- **Per-monitor configuration**: The pipe controls how many monitors are active. Per-monitor resolution, refresh rate, and display properties are configured through `C:\VirtualDisplayDriver\vdd_settings.xml`.
- **Windows only**: The VDD driver is a Windows IDD — this library targets `net8.0-windows`.

---

## Protocol Documentation

For details on the underlying named-pipe protocol:

- [Named Pipe API Reference](named-pipe-api-reference.md) — All 17 commands, encoding, connection model
- [Named Pipe Technical Deep Dive](named-pipe-technical-deep-dive.md) — Wire protocol, thread safety, crash analysis
- [VDD Overview](virtual-display-driver-overview.md) — Driver architecture, capabilities, configuration
