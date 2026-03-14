# MTTVirtualDisplayPipe -- Technical Deep Dive

This document is a comprehensive technical reference for the named pipe protocol exposed by the upstream [Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MttVDD) project. It targets developers building any tooling that sends commands to the MttVDD driver.

---

## 1. Overview

The upstream MttVDD driver ([VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)) is a Windows Indirect Display Driver (IDD) built on Microsoft's IddCx framework (INF minimum: IddCx 1.2 via `IddCx0102`; compile target: IddCx 1.10 for x64/ARM64 Release builds). The driver uses `IDD_IS_FIELD_AVAILABLE` for runtime feature detection and calls `IddCxGetVersion()` for diagnostics. It runs as a User-Mode Driver Framework (UMDF) DLL (`MttVDD.dll`) and creates virtual monitors without requiring physical display hardware.

At runtime, the driver exposes a named pipe at:

```
\\.\pipe\MTTVirtualDisplayPipe
```

This pipe accepts plain-text commands for controlling virtual displays -- adding monitors, removing monitors, and querying driver health. The protocol follows a **one-shot connection model**: each command requires opening a fresh pipe connection, sending the command, reading the response, and then closing the connection. The driver processes exactly one command per connection, then disconnects the client.

Monitor configuration -- resolutions, refresh rates, color formats, HDR parameters, EDID profiles -- is defined in a separate XML file at `C:\VirtualDisplayDriver\vdd_settings.xml`. The pipe protocol controls *how many* virtual monitors are active; the XML file controls *what each monitor looks like*. The `SETDISPLAYCOUNT` command sets the total number of active monitors, and each monitor inherits its profile from the corresponding entry in `vdd_settings.xml`.

The driver device appears as `Root\MttVDD` in Device Manager. It is officially code-signed via SignPath.io, so no test signing is required on x64 systems.

---

## 2. Wire Protocol

The upstream pipe protocol uses **raw UTF-16LE (wchar_t) string commands** with **no framing, no length prefixes, and no delimiters**. This is fundamentally different from typical structured IPC protocols.

### Pipe Mode

The driver creates the pipe with `PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT` (synchronous, message-mode). Each `WriteFile`/`ReadFile` call operates on a complete message. Note that `PipeOptions.Asynchronous` used in C# client code controls the client-side I/O model only -- the server pipe itself is synchronous.

### Buffer Size Limit

The driver reads commands into a `wchar_t buffer[128]` (Driver.cpp), limiting commands to **127 wide characters** (254 bytes) plus a null terminator. Commands exceeding this length are silently truncated. In practice, all documented commands fit well within this limit.

### Sending Commands

Commands are encoded as UTF-16LE byte sequences and written directly to the pipe. In C#, this is `Encoding.Unicode.GetBytes(command)`. There is no header, no length prefix, no message boundary marker -- the driver reads the raw bytes into a `wchar_t` buffer via `ReadFile`.

### Receiving Responses

Response encoding varies by command:

| Command | Response Encoding | Response Content |
|---|---|---|
| `PING` | UTF-8 | `PONG` |
| `SETDISPLAYCOUNT N` | UTF-8 | Log/status messages (read until disconnect) |
| `GETSETTINGS` | UTF-16LE | `SETTINGS DEBUG=true\|false LOG=true\|false` |
| Toggle commands | UTF-8 | Log messages via log-through-pipe |
| Query commands | UTF-8 | Log messages via log-through-pipe |
| `SETGPU "name"` | UTF-8 | Log messages via log-through-pipe |

The asymmetry between write encoding (always UTF-16LE) and read encoding (usually UTF-8) reflects the driver's internal implementation: commands are received into `wchar_t` buffers for string comparison, but responses are written using different code paths depending on the command handler. Most commands respond through the **log-through-pipe** mechanism (see Section 4.5), which writes UTF-8 log messages to the connected pipe.

> **Important:** Clients that hardcode UTF-8 decoding will correctly handle `PING` and `SETDISPLAYCOUNT` responses but will corrupt `GETSETTINGS` responses. Handle encoding per-command or use `GETSETTINGS`-aware decoding.

### No Framing

There are no length prefixes, no message delimiters, no packet boundaries. The client writes command bytes, the driver reads them, processes the command, writes a response, and then disconnects. The client detects the end of the response by reading until `ReadFile` returns 0 bytes (pipe closed by server). This read-until-disconnect pattern is the only way to know when the response is complete.

---

## 3. Connection Lifecycle

Each command follows this exact sequence:

### Client Side

1. **Open connection**: Create a `NamedPipeClientStream` (C#) or call `CreateFileW` (Win32) targeting `\\.\pipe\MTTVirtualDisplayPipe`. The pipe is bidirectional (`PipeDirection.InOut`).

2. **Connect with timeout**: Call `ConnectAsync` with a cancellation token. A 10-second timeout is recommended:

   ```csharp
   using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
   connectCts.CancelAfter(TimeSpan.FromSeconds(10));
   await pipe.ConnectAsync(connectCts.Token);
   ```

3. **Write command**: Encode the command string as UTF-16LE and write the bytes to the pipe.

4. **Flush**: Call `FlushAsync` to ensure all bytes are delivered to the driver before reading.

5. **Read until disconnect**: Read in a loop until the driver closes its end of the pipe (read returns 0 bytes). This is critical -- the client MUST drain all response data and wait for the disconnect signal. For `SETDISPLAYCOUNT`, the driver triggers an internal `RELOAD_DRIVER` operation that reinitializes the entire IddCx adapter, and the disconnect only occurs after this reload completes.

6. **Close**: Dispose the pipe stream. The connection cannot be reused.

### Driver Side

1. **Accept connection**: The driver's pipe listener accepts one client connection via `ConnectNamedPipe`.

2. **Read command**: `ReadFile` into a `wchar_t` buffer to receive the UTF-16LE command string.

3. **Dispatch**: Compare the command string against known commands (17 total: `PING`, `SETDISPLAYCOUNT`, `GETSETTINGS`, `RELOAD_DRIVER`, 8 toggle commands, 4 query commands, and `SETGPU`) and execute the appropriate handler.

4. **Write response**: Send response bytes back to the client.

5. **Disconnect**: Call `DisconnectNamedPipe` followed by `CloseHandle` to close the pipe instance. This is what causes the client's read loop to receive 0 bytes and exit.

6. **Next client**: The listener loop creates a new pipe instance and waits for the next connection.

### Sequence Diagram

```
Client                              Driver (MTTVirtualDisplayPipe)
  |                                        |
  |--- CreateFileW / ConnectAsync -------->|  (new pipe instance)
  |                                        |
  |--- WriteAsync(UTF-16LE command) ------>|  ReadFile into wchar_t buffer
  |--- FlushAsync ------------------------>|
  |                                        |  dispatch command
  |                                        |  (SETDISPLAYCOUNT triggers RELOAD_DRIVER)
  |<-- response bytes (UTF-8 or UTF-16LE) -|  WriteFile response
  |<-- 0 bytes (disconnect) ---------------|  DisconnectNamedPipe + CloseHandle
  |                                        |
  |  (dispose pipe stream)                 |  (loop: create new instance, wait)
  |                                        |
```

### One Command Per Connection

This is a strict constraint of the upstream driver. The driver reads one command, processes it, responds, and disconnects. There is no way to send multiple commands on a single connection. Every command requires opening a brand new pipe connection.

---

## 4. Commands

### PING

**Purpose**: Health check and connectivity probe. Verifies that the driver is installed, running, and responsive.

**Request**: UTF-16LE encoded string `PING` (8 bytes: `50 00 49 00 4E 00 47 00`)

**Response**: UTF-8 encoded string `PONG` (4 bytes: `50 4F 4E 47`)

Clients typically use `PING` to verify the driver is available before sending operational commands. The upstream pipe protocol does not provide a dedicated status query that returns per-monitor details, so `PING` is also the closest thing to a status check.

### SETDISPLAYCOUNT N

**Purpose**: Set the total number of active virtual monitors. `N` is a non-negative integer.

**Request**: UTF-16LE encoded string `SETDISPLAYCOUNT N` where N is the desired count. Examples:
- `SETDISPLAYCOUNT 0` -- remove all virtual monitors
- `SETDISPLAYCOUNT 1` -- one virtual monitor
- `SETDISPLAYCOUNT 3` -- three virtual monitors

**Response**: UTF-8 encoded status/log messages via the log-through-pipe mechanism. The client must read until the driver disconnects. The response stream includes log messages from the XML update, the adapter reinitialization, and any monitor creation/destruction.

**Behavior**: This command first calls `UpdateXmlDisplayCountSetting(newDisplayCount)` to **persist the new count to `vdd_settings.xml`**. The count is persistent across driver restarts. The XML write can fail independently -- in that case the driver logs an error but proceeds with reload anyway. If parsing the count from the command string fails, `newDisplayCount` defaults to `1`.

After the XML update, the command triggers an internal `RELOAD_DRIVER` operation in the upstream driver, which **reinitializes the entire IddCx adapter**. This is a heavyweight operation:

- All existing virtual monitors are torn down
- The IddCx adapter is reinitialized
- New monitors are created according to the new count and `vdd_settings.xml` profiles
- Windows desktop composition is recalculated

> **WARNING**: Rapid successive `SETDISPLAYCOUNT` calls can crash the driver. Each call triggers `RELOAD_DRIVER` which reinitializes the IddCx adapter. Too many reloads in quick succession can destabilize the driver. Space out calls and avoid using `SETDISPLAYCOUNT` in cleanup/teardown code where multiple calls may stack up.

The client MUST read until the driver disconnects before sending the next command. This ensures the reload operation has completed. Sending a second `SETDISPLAYCOUNT` while a reload is in progress can cause driver instability or crashes.

**Design implication**: Because the pipe only supports setting the total count (not adding/removing individual monitors), clients that want add/remove semantics must track the display count locally and increment/decrement it before each `SETDISPLAYCOUNT` call.

### GETSETTINGS

**Purpose**: Query the driver's current configuration.

**Request**: UTF-16LE encoded string `GETSETTINGS`

**Response**: UTF-16LE encoded `wstring` in the format:

```
SETTINGS DEBUG=true|false LOG=true|false
```

The response contains only two settings: the current debug logging status and the general logging status. Unlike `PING` and `SETDISPLAYCOUNT` which respond in UTF-8, `GETSETTINGS` returns its payload in UTF-16LE (including a null terminator in the byte count). Clients must use `Encoding.Unicode` (C#) or equivalent UTF-16LE decoding to read this response correctly.

> **Note:** The `EnabledQuery()` function used by `GETSETTINGS` checks the Windows registry at `HKEY_LOCAL_MACHINE\SOFTWARE\MikeTheTech\VirtualDisplayDriver` first, falling back to the XML configuration if the registry key is not found.

### RELOAD_DRIVER

**Purpose**: Force a full driver reload without changing the monitor count.

**Request**: UTF-16LE encoded string `RELOAD_DRIVER`

**Response**: Driver reinitializes the adapter, then disconnects.

> **WARNING: Do not use this command.** Sending `RELOAD_DRIVER` directly through the pipe causes **undefined behavior** ([upstream issue #351](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/351)). The root cause is a type mismatch in the `ReloadDriver` function:
>
> ```cpp
> void ReloadDriver(HANDLE hPipe) {
>     auto* pContext = WdfObjectGet_IndirectDeviceContextWrapper(hPipe);
> ```
>
> `ReloadDriver` receives `hPipe` (a named pipe handle) and passes it to `WdfObjectGet_IndirectDeviceContextWrapper`, which expects a WDF device object handle. A pipe handle is not a WDF object, causing **undefined behavior**. The function has a null-check guard (`if (pContext && pContext->pContext)`), so depending on memory state it may crash (segfault) or silently fail. When called via `SETDISPLAYCOUNT`, the same bug exists but preceding XML update operations and log messages may affect timing, which can mask or alter the failure behavior.

This command exists in the driver's command dispatch table and can be sent, but it is not intended for direct client use. Use `SETDISPLAYCOUNT` instead.

### Toggle Commands

Eight commands follow a common pattern: `COMMAND true|false` (UTF-16LE, space-separated). Each updates the corresponding field in `vdd_settings.xml`. Some trigger `ReloadDriver`; others take effect immediately via in-memory flags.

| Command | XML Field | Triggers ReloadDriver |
|---|---|---|
| `LOG_DEBUG true\|false` | `debuglogging` | No -- updates in-memory debug flag directly |
| `LOGGING true\|false` | `logging` | No -- updates in-memory logging flag directly |
| `HDRPLUS true\|false` | `HDRPlus` | Yes |
| `SDR10 true\|false` | `SDR10bit` | Yes |
| `CUSTOMEDID true\|false` | `CustomEdid` | Yes |
| `PREVENTSPOOF true\|false` | `PreventSpoof` | Yes |
| `CEAOVERRIDE true\|false` | `EdidCeaOverride` | Yes |
| `HARDWARECURSOR true\|false` | `HardwareCursor` | Yes |

**Request format**: UTF-16LE encoded string, e.g. `HDRPLUS true` or `LOGGING false`.

**Response**: UTF-8 log messages via the log-through-pipe mechanism. Commands that trigger `ReloadDriver` produce additional log output from the adapter reinitialization.

> **Note:** The same `ReloadDriver` crash risk (see RELOAD_DRIVER section above) applies to all toggle commands that trigger it. The `LOG_DEBUG` and `LOGGING` commands are safe -- they update in-memory flags without reloading.

### Query Commands

Four commands query driver/system information. They take no parameters and respond by logging information through the pipe as UTF-8.

| Command | Purpose |
|---|---|
| `D3DDEVICEGPU` | Initializes a D3D device and logs GPU information |
| `IDDCXVERSION` | Logs the IddCx framework version |
| `GETASSIGNEDGPU` | Logs the currently assigned GPU |
| `GETALLGPUS` | Logs all available GPUs on the system |

**Request**: UTF-16LE encoded command string with no parameters.

**Response**: UTF-8 log messages via the log-through-pipe mechanism. The response content varies by command -- clients should read until disconnect and parse the log output for the relevant information.

### SETGPU

**Purpose**: Select which GPU hosts the virtual displays. Useful in multi-GPU systems.

**Request**: UTF-16LE encoded string `SETGPU "name"` where `name` is the quoted GPU name.

**Response**: UTF-8 log messages via the log-through-pipe mechanism.

**Behavior**: Updates the GPU selection in `vdd_settings.xml` and triggers `ReloadDriver`. The same crash risk as `RELOAD_DRIVER` applies.

### Log-Through-Pipe Mechanism

Most commands respond through a **log-through-pipe** mechanism rather than sending dedicated response data. When `SendLogsThroughPipe` is `true` (the default, configurable in `vdd_settings.xml`) and a pipe client is connected (`g_pipeHandle` is valid), the driver's `vddlog()` function writes **all log messages** to the connected pipe as UTF-8 via an inline `WriteFile` call. Note that `SendToPipe()` is a separate function used by specific command handlers (e.g., PING); `vddlog()` does not call it.

This means:
- **Any command's response includes interleaved driver log output**, not just dedicated response data
- The `PING` "PONG" response uses `SendToPipe()` to write directly to the pipe
- `vddlog()` writes log messages to the pipe via its own inline `WriteFile` call (it does not call `SendToPipe()` -- both functions write to `g_pipeHandle` but are independent code paths)
- For `SETDISPLAYCOUNT`, the response stream includes log messages from the XML update, the adapter reinitialization, and any monitor creation/destruction
- Query commands (`D3DDEVICEGPU`, `IDDCXVERSION`, etc.) return their results as log lines through this same channel
- **Client code should handle receiving multiple log lines** before the pipe disconnects
- Log-through-pipe sends **all severity levels** to the connected client -- the debug log filter (`debugLogs` flag) only controls file logging, not pipe output

The only exception is `GETSETTINGS`, which sends a structured UTF-16LE response string rather than log output.

---

## 5. Client Implementation Patterns

This section describes recommended patterns for building a robust pipe client. These patterns address the specific challenges of the MttVDD protocol: one-shot connections, heavyweight reload operations, and encoding asymmetry.

### One-Shot Connection Lifecycle

Every command requires a complete connection lifecycle. A typical implementation:

```csharp
async Task<string> SendCommandAsync(string command, CancellationToken ct)
{
    await using var pipe = new NamedPipeClientStream(
        ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    connectCts.CancelAfter(TimeSpan.FromSeconds(10));
    await pipe.ConnectAsync(connectCts.Token);

    // Send command as UTF-16LE (wchar_t) -- matches driver's ReadFile buffer.
    var payload = Encoding.Unicode.GetBytes(command);
    await pipe.WriteAsync(payload, ct);
    await pipe.FlushAsync(ct);

    // Read until the driver disconnects (returns 0 bytes).
    var response = new StringBuilder();
    var buffer = new byte[512];
    int bytesRead;
    while ((bytesRead = await pipe.ReadAsync(buffer, ct)) > 0)
    {
        response.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    return response.ToString().Trim('\0').Trim();
}
```

> **Note:** This example uses UTF-8 decoding, which is correct for `PING` and `SETDISPLAYCOUNT` responses. For `GETSETTINGS`, accumulate raw bytes into a `MemoryStream` and decode with `Encoding.Unicode` after the read loop completes.

### Command Serialization

Because `SETDISPLAYCOUNT` triggers a destructive adapter reload, clients must ensure that only one command is in flight at a time. Use a binary semaphore to serialize all pipe operations:

```csharp
private readonly SemaphoreSlim _pipeLock = new(1, 1);

async Task<string> SendCommandAsync(string command, CancellationToken ct)
{
    await _pipeLock.WaitAsync(ct);
    try
    {
        // ... one-shot connection lifecycle ...
    }
    finally
    {
        _pipeLock.Release();
    }
}
```

The serialization guarantee has two components:

1. **Semaphore**: Prevents a second call from starting while the first is still running.

2. **Read until disconnect**: Within each call, the client reads until the driver disconnects. The driver calls `DisconnectNamedPipe` only after it has fully processed the command, including any `RELOAD_DRIVER` operation. By waiting for the disconnect, the client ensures the driver is in a stable state before the semaphore is released and the next command can begin.

Together, these two mechanisms ensure that commands are fully serialized end-to-end: no new connection is opened until the previous command's processing and driver reload (if any) are complete.

### Local Display Count Tracking

The upstream pipe protocol has no command that returns the current monitor count or per-monitor details. Clients that need to track how many monitors are active must maintain a local counter and update it with each `SETDISPLAYCOUNT` call.

This local count will become stale if the driver restarts or another client changes the display count externally.

### Encoding Handling

The response encoding varies by command. Clients have two options:

1. **Per-command decoding**: Use UTF-8 for most commands (`PING`, `SETDISPLAYCOUNT`, toggle commands, query commands, `SETGPU`), UTF-16LE for `GETSETTINGS`.
2. **UTF-8 only**: If the client does not use `GETSETTINGS`, hardcoding UTF-8 decoding is acceptable and simpler -- all other commands respond in UTF-8 via the log-through-pipe mechanism.

---

## 6. vdd_settings.xml Configuration

The upstream driver reads monitor profiles from `C:\VirtualDisplayDriver\vdd_settings.xml` at initialization time and whenever `SETDISPLAYCOUNT` triggers a `RELOAD_DRIVER`. This XML file controls all per-monitor display characteristics.

### What vdd_settings.xml Controls

| Setting | Description |
|---|---|
| **Resolutions** | Per-monitor lists of supported width/height pairs |
| **Refresh rates** | Including fractional rates like 59.94 Hz (the driver's `float_to_vsync()` converts floats to numerator/denominator pairs using `den=10000`, e.g. 59.94 becomes 599400/10000) |
| **Color format** | RGB 8/10/12-bit, YCbCr 4:4:4, YCbCr 4:2:2, YCbCr 4:2:0 (various bit depths) |
| **HDR parameters** | Enable/disable, 10-bit or 12-bit color depth (requires Windows 11 23H2+) |
| **EDID profiles** | Custom Extended Display Identification Data for monitor emulation |
| **GPU selection** | GPU friendly name string for multi-adapter systems (the driver resolves the friendly name to a PCI-bus LUID internally) |
| **Logging** | Debug logging configuration |
| **Cursor settings** | Hardware cursor parameters (128x128 pixel support with alpha blending) |

### Relationship to SETDISPLAYCOUNT

`SETDISPLAYCOUNT N` sets the number of active virtual monitors but does not configure them individually. Each of the N monitors inherits its profile from the corresponding entry in `vdd_settings.xml`. To change a monitor's resolution or refresh rate, you edit the XML file and then send a `SETDISPLAYCOUNT` command (or restart the driver) to reload the configuration.

The `option.txt` file in the upstream repository provides 72 resolution presets ranging from 640x480 to 10240x4320, which can be used as reference values for the XML configuration.

### File Location

The driver expects the settings file at `C:\VirtualDisplayDriver\vdd_settings.xml`. The `Download-VddDriver.ps1` script in this repository downloads a default copy of this file alongside the driver binaries.

---

## 7. Thread Safety

### Why Serialization Matters

The `SETDISPLAYCOUNT` command triggers `RELOAD_DRIVER` inside the upstream driver, which reinitializes the entire IddCx adapter. This is a destructive, asynchronous operation within the driver -- all existing virtual monitors are torn down and new ones are created. If a second command arrives while a reload is in progress, the driver may be in an inconsistent state, leading to failures or crashes.

> **WARNING**: Rapid successive reloads can destabilize the driver ([upstream issue #351](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/351)). Space out `SETDISPLAYCOUNT` calls and avoid calling them in rapid loops or cleanup/teardown code.

### Recommended Approach

Use a binary semaphore (`SemaphoreSlim(1, 1)`) to ensure at most one command is in flight at any time. Acquire it before opening the pipe and release it in a `finally` block after the read-until-disconnect loop completes.

The semaphore alone is not sufficient -- the **read-until-disconnect pattern** is equally critical. The driver calls `DisconnectNamedPipe` only after it has fully processed the command. By waiting for the disconnect, the client ensures the driver is in a stable state before releasing the semaphore. Without this, the semaphore would be released while the driver is still mid-reload.

---

## 8. Error Handling

### Exception Types

Clients communicating with the MttVDD pipe should expect these exception types:

| Exception | Cause |
|---|---|
| `IOException` | Pipe broken mid-communication, driver process exited, or the pipe does not exist. This is the most common failure mode when the driver is not installed or has been unloaded. |
| `OperationCanceledException` | The connect timeout expired, or the caller's `CancellationToken` was triggered. When using a linked `CancellationTokenSource`, either condition produces this exception. |
| `ObjectDisposedException` | The pipe stream was disposed while an async read or write was in progress. This typically occurs during application shutdown. |

### Recommended Practices

1. **Catch and surface**: Catch `IOException`, `OperationCanceledException`, and `ObjectDisposedException` to update connection state and log the failure. Re-throw to let the caller handle error presentation or retry logic.

2. **Semaphore safety**: Always release the serialization semaphore in a `finally` block to prevent deadlocks after pipe failures.

3. **Connection state tracking**: Maintain an `IsConnected` flag. Set it to `true` after any successful command, `false` after any caught exception. This gives callers a quick check for driver availability.

4. **Disposal**: When disposing the client, dispose the semaphore. Any in-flight or subsequent command will throw `ObjectDisposedException`, which the exception handler should catch and surface.

---

## 9. Security

### Explicit DACL

The upstream MttVDD driver creates the pipe with **explicit `SECURITY_ATTRIBUTES`** using the SDDL string `D:(A;;GA;;;WD)`:

```cpp
SECURITY_ATTRIBUTES sa;
sa.nLength = sizeof(SECURITY_ATTRIBUTES);
sa.bInheritHandle = FALSE;
const wchar_t* sddl = L"D:(A;;GA;;;WD)";
```

This SDDL breaks down as:
- `GA` = Generic All (full control -- read, write, execute)
- `WD` = World (the "Everyone" well-known SID)

This grants **full control to ALL local processes**, regardless of user identity or privilege level. There is no authentication, no authorization, and no per-command access control. Any process on the machine can connect and issue any command.

### No Encryption

Commands and responses are transmitted as plaintext over the local named pipe. This is standard for local IPC on Windows.

### Acceptability

For a local developer tool running on a single-user workstation, these security characteristics are acceptable. The attack surface is limited to processes already running on the local machine, and the worst-case impact is unauthorized creation or removal of virtual monitors -- an annoyance, not a privilege escalation. The virtual display driver itself runs in user mode (UMDF), so even in a worst-case scenario, driver misbehavior cannot cause a BSOD.

---

## Appendix: Quick Reference

### Pipe Endpoint

| Property | Value |
|---|---|
| **Pipe name** | `\\.\pipe\MTTVirtualDisplayPipe` |
| **Direction** | Bidirectional |
| **Server pipe mode** | `PIPE_TYPE_MESSAGE \| PIPE_READMODE_MESSAGE \| PIPE_WAIT` (synchronous) |
| **Connection model** | One-shot (one command per connection) |
| **Command encoding** | UTF-16LE |
| **Response encoding** | UTF-8 (most commands, via log-through-pipe) or UTF-16LE (`GETSETTINGS`) |
| **Buffer limit** | `wchar_t[128]` -- max 127 wide characters per command |
| **Security** | SDDL `D:(A;;GA;;;WD)` -- full control to Everyone |

### Command Summary

| Command | Purpose | Parameters | Response Encoding | ReloadDriver |
|---|---|---|---|---|
| `PING` | Connectivity probe | None | UTF-8: `PONG` | No |
| `SETDISPLAYCOUNT N` | Set total virtual display count | `N` (integer) | UTF-8: log messages | Yes |
| `GETSETTINGS` | Query debug/logging status | None | UTF-16LE: `SETTINGS DEBUG=... LOG=...` | No |
| `RELOAD_DRIVER` | Force adapter reload (do not use) | None | Disconnects after reload | Yes (undefined behavior) |
| `LOG_DEBUG true\|false` | Toggle debug logging | `true` or `false` | UTF-8: log messages | No |
| `LOGGING true\|false` | Toggle general logging | `true` or `false` | UTF-8: log messages | No |
| `HDRPLUS true\|false` | Toggle HDR+ mode | `true` or `false` | UTF-8: log messages | Yes |
| `SDR10 true\|false` | Toggle 10-bit SDR | `true` or `false` | UTF-8: log messages | Yes |
| `CUSTOMEDID true\|false` | Toggle custom EDID | `true` or `false` | UTF-8: log messages | Yes |
| `PREVENTSPOOF true\|false` | Toggle spoof prevention | `true` or `false` | UTF-8: log messages | Yes |
| `CEAOVERRIDE true\|false` | Toggle EDID CEA override | `true` or `false` | UTF-8: log messages | Yes |
| `HARDWARECURSOR true\|false` | Toggle hardware cursor | `true` or `false` | UTF-8: log messages | Yes |
| `D3DDEVICEGPU` | Query GPU via D3D device | None | UTF-8: log messages | No |
| `IDDCXVERSION` | Query IddCx version | None | UTF-8: log messages | No |
| `GETASSIGNEDGPU` | Query assigned GPU | None | UTF-8: log messages | No |
| `GETALLGPUS` | Query all available GPUs | None | UTF-8: log messages | No |
| `SETGPU "name"` | Select GPU for virtual displays | Quoted GPU name | UTF-8: log messages | Yes |
