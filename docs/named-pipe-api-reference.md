# Virtual Display Driver Pipe API Reference

API reference for the named-pipe control protocol exposed by the upstream [Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MttVDD). This protocol lets client applications manage virtual monitors at runtime by sending plain-text commands over a Windows named pipe.

> **See also:** [Pipe Technical Deep Dive](named-pipe-technical-deep-dive.md) for sequence diagrams, the `ReloadDriver` crash root-cause analysis, thread-safety rationale, and advanced client implementation patterns.

> **Version target:** MttVDD driver releases 24.12+ (protocol stable since late 2024).

---

## 1. Quick Start

Minimal C# example -- connect to the driver and send `PING`:

```csharp
using System.IO.Pipes;
using System.Text;

await using var pipe = new NamedPipeClientStream(
    ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync();

var payload = Encoding.Unicode.GetBytes("PING");
await pipe.WriteAsync(payload);
await pipe.FlushAsync();

// Read until the driver disconnects
var buffer = new byte[512];
var sb = new StringBuilder();
int bytesRead;
while ((bytesRead = await pipe.ReadAsync(buffer)) > 0)
    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

// sb.ToString() == "PONG"
```

> **IMPORTANT: No display count query.** The pipe protocol has no command to query the current number of active virtual monitors. Clients that need add/remove semantics must **track the display count locally** and pass the updated total to `SETDISPLAYCOUNT`. This local count can become stale if another client or driver restart changes the count externally.

---

## 2. Connection Details

| Property | Value |
|---|---|
| **Pipe name** | `\\.\pipe\MTTVirtualDisplayPipe` |
| **Direction** | `InOut` (duplex) |
| **Pipe mode (server)** | `PIPE_WAIT` (synchronous blocking), `PIPE_TYPE_MESSAGE \| PIPE_READMODE_MESSAGE` |
| **Pipe mode (client)** | .NET clients typically use `PipeOptions.Asynchronous` for non-blocking I/O |
| **Connection model** | **One-shot:** one command per connection. The driver disconnects after responding. |
| **Connect timeout (recommended)** | 10 seconds |
| **Max command length** | 127 `wchar_t` characters (254 bytes). The driver's read buffer is `wchar_t buffer[128]` (127 usable + null terminator). Commands exceeding this length are silently truncated. |
| **Server-side pipe buffers** | 512 bytes each for input and output (`CreateNamedPipeW` nInBufferSize/nOutBufferSize) |
| **Max instances** | `PIPE_UNLIMITED_INSTANCES` — the server can create multiple pipe instances simultaneously |
| **Security** | Explicit `SECURITY_ATTRIBUTES` with SDDL string `D:(A;;GA;;;WD)` — grants full control (`GA` = Generic All) to all local users and processes (`WD` = World/Everyone). |

> **Security note:** The SDDL string grants **Generic All** access to the **World (Everyone)** SID, meaning any local user or process on the machine can connect and issue commands -- including `SETDISPLAYCOUNT` and toggle commands that modify driver state. This is by design for local desktop use. In multi-user or shared-machine scenarios, review whether unrestricted pipe access is appropriate.

Each command requires its own pipe connection. After the driver processes the command and sends any response data, it calls `DisconnectNamedPipe` and closes the handle. The client detects this when `ReadAsync` returns 0 bytes.

> **Note on pipe mode:** The server-side pipe uses synchronous blocking mode (`PIPE_WAIT`). The `PipeOptions.Asynchronous` flag in the Quick Start and client examples is a client-side option for non-blocking I/O -- it does not affect the server's behavior.

---

## 3. Wire Format

There is no framing layer. Raw bytes are written directly to the pipe.

### Sending commands

Encode the command string as **UTF-16LE** (`Encoding.Unicode` in .NET), then write the raw bytes. This matches the driver's internal `wchar_t` read buffer.

> **WARNING: Prefix matching.** The driver matches commands using `wcsncmp` (prefix comparison), not exact string equality. For example, sending `PING_EXTRA` would match the `PING` handler. Always send exactly the documented command strings and avoid appending unexpected trailing text. See also Section 10 (Limits and Caveats).

```csharp
var payload = Encoding.Unicode.GetBytes("PING");  // 8 bytes: P-00 I-00 N-00 G-00
await pipe.WriteAsync(payload);
await pipe.FlushAsync();
```

### Reading responses

Response encoding varies by command:

| Command | Response encoding |
|---|---|
| `PING` | UTF-8 (direct `SendToPipe("PONG")`; additional log data may follow if `SendLogsThroughPipe` is enabled) |
| `SETDISPLAYCOUNT` | UTF-8 (log/status messages via log-through-pipe) |
| `GETSETTINGS` | UTF-16LE (direct wstring response) |
| Toggle commands (`LOG_DEBUG`, `LOGGING`, `HDRPLUS`, etc.) | UTF-8 (log/status messages via log-through-pipe) |
| Query commands (`D3DDEVICEGPU`, `IDDCXVERSION`, etc.) | UTF-8 (via log-through-pipe) |
| `SETGPU` | UTF-8 (log/status messages via log-through-pipe) |
| `RELOAD_DRIVER` | UTF-8 (log/status messages via log-through-pipe) |

### End of response

The driver signals completion by disconnecting the pipe. The client reads in a loop until `ReadAsync` returns 0 bytes:

```csharp
var buffer = new byte[512];
var sb = new StringBuilder();
int bytesRead;
while ((bytesRead = await pipe.ReadAsync(buffer)) > 0)
    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
```

> **Note:** This example uses UTF-8 decoding, which is correct for `PING` and `SETDISPLAYCOUNT` responses. For `GETSETTINGS`, use `Encoding.Unicode` (UTF-16LE) instead -- see the GETSETTINGS example in Section 4.

---

## 4. Commands

### PING

Health check and connectivity probe.

| Property | Value |
|---|---|
| **Purpose** | Verify the driver is running and responsive |
| **Send** | `PING` encoded as UTF-16LE (8 bytes) |
| **Response** | `PONG` (UTF-8) |

**Example:**

```csharp
await using var pipe = new NamedPipeClientStream(
    ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync();

await pipe.WriteAsync(Encoding.Unicode.GetBytes("PING"));
await pipe.FlushAsync();

var buffer = new byte[512];
var sb = new StringBuilder();
int bytesRead;
while ((bytesRead = await pipe.ReadAsync(buffer)) > 0)
    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

Console.WriteLine(sb.ToString());  // "PONG"
```

---

### SETDISPLAYCOUNT N

Set the total number of active virtual monitors.

| Property | Value |
|---|---|
| **Purpose** | Add or remove virtual monitors by setting the total count |
| **Send** | `SETDISPLAYCOUNT N` encoded as UTF-16LE, where N is an integer >= 0. If N is missing or unparseable, the driver defaults to 1. |
| **Behavior** | Persists the count to `vdd_settings.xml` via `UpdateXmlDisplayCountSetting()`, then triggers an internal `RELOAD_DRIVER` -- reinitializes the entire IddCx display adapter |
| **Persistence** | The count is written to XML before the reload, so it survives driver restarts |
| **Response** | Log/status output (UTF-8) via the log-through-pipe mechanism before disconnecting |

**Examples:**

| Command | Effect |
|---|---|
| `SETDISPLAYCOUNT 1` | Activate exactly 1 virtual monitor |
| `SETDISPLAYCOUNT 3` | Activate exactly 3 virtual monitors |
| `SETDISPLAYCOUNT 0` | Remove all virtual monitors |

> **IMPORTANT:** This is a heavyweight operation. Each call triggers a full adapter reload. Always wait until the pipe disconnects (read returns 0 bytes) before opening a new connection for the next command.

> **WARNING:** Rapid successive calls can crash the driver. The integration test suite explicitly avoids calling `SETDISPLAYCOUNT` in cleanup code, noting: "each SETDISPLAYCOUNT triggers RELOAD_DRIVER which reinitializes the IddCx adapter. Too many reloads can crash the driver."

**Example:**

```csharp
await using var pipe = new NamedPipeClientStream(
    ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync();

await pipe.WriteAsync(Encoding.Unicode.GetBytes("SETDISPLAYCOUNT 2"));
await pipe.FlushAsync();

// Wait for the driver to finish reloading by reading until disconnect
var buffer = new byte[512];
while (await pipe.ReadAsync(buffer) > 0) { }
// Driver has completed the reload -- safe to send the next command
```

---

### GETSETTINGS

Query the current driver configuration.

| Property | Value |
|---|---|
| **Purpose** | Retrieve the driver's active settings |
| **Send** | `GETSETTINGS` encoded as UTF-16LE |
| **Response** | A single-line `wstring` encoded as **UTF-16LE** |
| **Response format** | `SETTINGS DEBUG=true\|false LOG=true\|false` |

The response is a single-line wstring with the prefix `SETTINGS ` followed by two key=value pairs indicating the current debug logging and logging status. This is **not** the full driver configuration -- only the two logging flags are returned.

> **Note on response keys:** The response uses the short key `LOG` (not `LOGGING`) and `DEBUG` (not `LOG_DEBUG`). These are internal abbreviated names and do not correspond one-to-one with the pipe command names. `LOG=true|false` reflects the state controlled by the `LOGGING` command, and `DEBUG=true|false` reflects the state controlled by the `LOG_DEBUG` command.

> **Note:** The response encoding for `GETSETTINGS` differs from all other commands. Use `Encoding.Unicode` (UTF-16LE) to decode the response, not `Encoding.UTF8`. This is because `GETSETTINGS` writes a `wstring` directly to the pipe, while other commands respond through the UTF-8 log-through-pipe mechanism.

**Example:**

```csharp
await using var pipe = new NamedPipeClientStream(
    ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync();

await pipe.WriteAsync(Encoding.Unicode.GetBytes("GETSETTINGS"));
await pipe.FlushAsync();

var buffer = new byte[4096];
var ms = new MemoryStream();
int bytesRead;
while ((bytesRead = await pipe.ReadAsync(buffer)) > 0)
    ms.Write(buffer, 0, bytesRead);

string settings = Encoding.Unicode.GetString(ms.ToArray());
Console.WriteLine(settings);
```

---

### RELOAD_DRIVER

Internal command that reinitializes the IddCx adapter.

| Property | Value |
|---|---|
| **Purpose** | Force a full driver reload without changing the monitor count |
| **Send** | `RELOAD_DRIVER` encoded as UTF-16LE |
| **Response** | Driver reinitializes the adapter, then disconnects |

> **WARNING: Do not use this command.** Sending `RELOAD_DRIVER` directly through the pipe is known to cause driver crashes ([upstream issue #351](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/351)). The `SETDISPLAYCOUNT` command triggers `RELOAD_DRIVER` internally as part of its normal operation -- use `SETDISPLAYCOUNT` instead.

This command is documented here for completeness. It exists in the driver's command dispatch table and can be sent, but it is not intended for direct client use.

---

### Toggle Commands

These commands update a setting in `vdd_settings.xml` and toggle the corresponding runtime flag. All take a single boolean parameter (`true` or `false`), space-separated from the command name. Send the entire string as UTF-16LE.

Commands that trigger a driver reload are marked below. Commands that do **not** trigger a reload take effect only at the runtime flag level (the XML is still updated for persistence across restarts).

> **Note on invalid parameters:** Toggle commands use prefix matching to check for `true` or `false`. If the parameter is neither (e.g., `HDRPLUS invalid`), the command silently does nothing -- no XML write, no ReloadDriver call, no error response. For commands that trigger ReloadDriver, the reload only occurs when a valid `true` or `false` parameter is provided. This differs from `SETDISPLAYCOUNT` and `SETGPU`, which call ReloadDriver unconditionally regardless of parameter validity.

#### LOG_DEBUG true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle debug logging |
| **Send** | `LOG_DEBUG true` or `LOG_DEBUG false` encoded as UTF-16LE |
| **XML setting** | `debuglogging` |
| **Triggers ReloadDriver** | No |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### LOGGING true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle general logging |
| **Send** | `LOGGING true` or `LOGGING false` encoded as UTF-16LE |
| **XML setting** | `logging` |
| **Triggers ReloadDriver** | No |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### HDRPLUS true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle HDR Plus mode |
| **Send** | `HDRPLUS true` or `HDRPLUS false` encoded as UTF-16LE |
| **XML setting** | `HDRPlus` |
| **Triggers ReloadDriver** | Yes (only when parameter is valid `true` or `false`) |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### SDR10 true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle 10-bit SDR color |
| **Send** | `SDR10 true` or `SDR10 false` encoded as UTF-16LE |
| **XML setting** | `SDR10bit` |
| **Triggers ReloadDriver** | Yes (only when parameter is valid `true` or `false`) |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### CUSTOMEDID true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle custom EDID profiles |
| **Send** | `CUSTOMEDID true` or `CUSTOMEDID false` encoded as UTF-16LE |
| **XML setting** | `CustomEdid` |
| **Triggers ReloadDriver** | Yes (only when parameter is valid `true` or `false`) |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### PREVENTSPOOF true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle spoof prevention |
| **Send** | `PREVENTSPOOF true` or `PREVENTSPOOF false` encoded as UTF-16LE |
| **XML setting** | `PreventSpoof` |
| **Triggers ReloadDriver** | Yes (only when parameter is valid `true` or `false`) |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### CEAOVERRIDE true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle EDID CEA block override |
| **Send** | `CEAOVERRIDE true` or `CEAOVERRIDE false` encoded as UTF-16LE |
| **XML setting** | `EdidCeaOverride` |
| **Triggers ReloadDriver** | Yes (only when parameter is valid `true` or `false`) |
| **Response** | Log output (UTF-8) via log-through-pipe |

#### HARDWARECURSOR true|false

| Property | Value |
|---|---|
| **Purpose** | Toggle hardware cursor support |
| **Send** | `HARDWARECURSOR true` or `HARDWARECURSOR false` encoded as UTF-16LE |
| **XML setting** | `HardwareCursor` |
| **Triggers ReloadDriver** | Yes (only when parameter is valid `true` or `false`) |
| **Response** | Log output (UTF-8) via log-through-pipe |

> **WARNING:** Toggle commands that trigger `ReloadDriver` carry the same crash risk as `SETDISPLAYCOUNT`. Avoid rapid successive calls to these commands.

---

### Query Commands

These commands take no parameters and return information about the driver or system through the log-through-pipe mechanism. All responses are UTF-8.

#### D3DDEVICEGPU

| Property | Value |
|---|---|
| **Purpose** | Initialize a D3D device and log GPU information |
| **Send** | `D3DDEVICEGPU` encoded as UTF-16LE |
| **Response** | GPU information (UTF-8) via log-through-pipe |

#### IDDCXVERSION

| Property | Value |
|---|---|
| **Purpose** | Query the IddCx version in use |
| **Send** | `IDDCXVERSION` encoded as UTF-16LE |
| **Response** | IddCx version string (UTF-8) via log-through-pipe |

#### GETASSIGNEDGPU

| Property | Value |
|---|---|
| **Purpose** | Query the currently assigned GPU |
| **Send** | `GETASSIGNEDGPU` encoded as UTF-16LE |
| **Response** | Assigned GPU name (UTF-8) via log-through-pipe |

#### GETALLGPUS

| Property | Value |
|---|---|
| **Purpose** | List all available GPUs |
| **Send** | `GETALLGPUS` encoded as UTF-16LE |
| **Response** | List of GPU names (UTF-8) via log-through-pipe |

---

### SETGPU "name"

Select which GPU the virtual display driver should use.

| Property | Value |
|---|---|
| **Purpose** | Assign the driver to a specific GPU by name |
| **Send** | `SETGPU "GPU Name"` encoded as UTF-16LE |
| **XML setting** | `gpu` |
| **Triggers ReloadDriver** | Yes |
| **Response** | Log output (UTF-8) via log-through-pipe |

The GPU name must be enclosed in quotes within the command string. Use `GETALLGPUS` to discover available GPU names.

**Example:**

```csharp
await SendDriverCommandAsync("GETALLGPUS");  // discover available GPUs
await SendDriverCommandAsync("SETGPU \"NVIDIA GeForce RTX 4090\"");
```

> **WARNING:** Like other commands that trigger `ReloadDriver`, this carries the same crash risk as `SETDISPLAYCOUNT`. Avoid rapid successive calls.

---

## 5. Log-Through-Pipe Mechanism

When the `SendLogsThroughPipe` setting is enabled in `vdd_settings.xml` (default: `true`), all driver log output from `vddlog()` is written to the connected pipe via `SendToPipe()` as **UTF-8** text.

> **Note:** `SendLogsThroughPipe` cannot be toggled through the pipe protocol. It can only be changed by editing `vdd_settings.xml` directly and reloading the driver (e.g., via `SETDISPLAYCOUNT`). There is no pipe command to enable or disable this setting at runtime.

This has several implications for client code:

- **Responses are log messages, not structured data.** Commands like `SETDISPLAYCOUNT` do not send a dedicated response -- instead, the log messages generated during the reload operation are forwarded through the pipe.
- **Responses may contain interleaved log lines.** A single command may produce multiple log entries from different parts of the driver.
- **The `PING` command's `PONG` response** is sent directly via `SendToPipe()`, not through `vddlog()`. However, the PING handler also calls `vddlog("p", "Heartbeat Ping")` afterward, so clients may receive both `PONG` and additional log data (e.g., `[timestamp] [PIPE] Heartbeat Ping`) if `SendLogsThroughPipe` is enabled.
- **Query commands** (`D3DDEVICEGPU`, `IDDCXVERSION`, `GETASSIGNEDGPU`, `GETALLGPUS`) return their results exclusively through log output.
- **`GETSETTINGS` is the exception.** It writes a `wstring` directly to the pipe as UTF-16LE, bypassing the log-through-pipe mechanism entirely.

Clients should read all data until the pipe disconnects (read returns 0 bytes) to capture the full response.

---

## 6. Complete Command Reference

Summary of all 17 commands supported by the driver's `HandleClient` function. For an extended version with response content descriptions, see the [Pipe Technical Deep Dive — Command Summary](named-pipe-technical-deep-dive.md#appendix-quick-reference).

| Command | Parameters | Triggers ReloadDriver | Response encoding |
|---|---|---|---|
| `PING` | None | No | UTF-8 |
| `SETDISPLAYCOUNT` | `N` (integer) | Yes | UTF-8 |
| `GETSETTINGS` | None | No | UTF-16LE |
| `RELOAD_DRIVER` | None | Yes (dangerous) | UTF-8 |
| `LOG_DEBUG` | `true\|false` | No | UTF-8 |
| `LOGGING` | `true\|false` | No | UTF-8 |
| `HDRPLUS` | `true\|false` | Yes (only with valid param) | UTF-8 |
| `SDR10` | `true\|false` | Yes (only with valid param) | UTF-8 |
| `CUSTOMEDID` | `true\|false` | Yes (only with valid param) | UTF-8 |
| `PREVENTSPOOF` | `true\|false` | Yes (only with valid param) | UTF-8 |
| `CEAOVERRIDE` | `true\|false` | Yes (only with valid param) | UTF-8 |
| `HARDWARECURSOR` | `true\|false` | Yes (only with valid param) | UTF-8 |
| `D3DDEVICEGPU` | None | No | UTF-8 |
| `IDDCXVERSION` | None | No | UTF-8 |
| `GETASSIGNEDGPU` | None | No | UTF-8 |
| `GETALLGPUS` | None | No | UTF-8 |
| `SETGPU` | `"name"` (quoted) | Yes | UTF-8 |

---

## 7. vdd_settings.xml

Monitor configuration is managed through an XML file at:

```
C:\VirtualDisplayDriver\vdd_settings.xml
```

> **Note:** This is the default path. The base directory is configurable via the registry key `HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver\VDDPATH`. If set, the driver uses that path instead of `C:\VirtualDisplayDriver`.

The pipe protocol controls **how many** virtual monitors are active (`SETDISPLAYCOUNT`). The settings file controls **what those monitors look like** -- their resolutions, refresh rates, color depth, and other display properties.

### Key configuration areas

| Area | Details |
|---|---|
| **Resolutions** | 72 presets from 640x480 to 10240x4320; custom resolutions supported |
| **Refresh rates** | Integer and fractional values (e.g., 60 Hz, 59.94 Hz via 60000/1001 notation) |
| **Color format** | RGB 8/10/12-bit, YCbCr 4:4:4, 4:2:2, 4:2:0 at various bit depths |
| **HDR** | 10-bit and 12-bit color depth (requires Windows 11 23H2 or later) |
| **EDID profiles** | Custom monitor identification data for emulating specific displays |
| **GPU selection** | GPU friendly name for targeting a specific adapter in multi-GPU systems (resolved to PCI-bus LUID internally) |
| **Logging** | Debug logging configuration |
| **Cursor settings** | Hardware cursor parameters (128x128 with alpha blending) |

### Obtaining the default settings file

Download the upstream default configuration:

```
https://raw.githubusercontent.com/VirtualDrivers/Virtual-Display-Driver/master/Virtual%20Display%20Driver%20(HDR)/vdd_settings.xml
```

Or use the project's download script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Download-VddDriver.ps1
```

This script downloads both the driver binaries and the default `vdd_settings.xml`.

### Workflow

1. Edit `C:\VirtualDisplayDriver\vdd_settings.xml` to define the desired resolution, refresh rate, and display properties.
2. Send `SETDISPLAYCOUNT N` via the pipe to activate N monitors with those settings.
3. Each activated monitor uses the configuration from `vdd_settings.xml`.

---

## 8. Client Examples

All examples follow the one-shot pattern: connect, send a single command, read until the driver disconnects, close.

### C\#

```csharp
using System.IO.Pipes;
using System.Text;

/// <summary>
/// Sends a single command to the Virtual Display Driver and returns the response.
/// Each call opens and closes its own pipe connection (one-shot protocol).
/// Note: This function decodes responses as UTF-8, which is correct for all commands
/// EXCEPT GETSETTINGS (which responds in UTF-16LE). For GETSETTINGS, see the
/// dedicated example in Section 4.
/// </summary>
async Task<string> SendDriverCommandAsync(string command, CancellationToken ct = default)
{
    await using var pipe = new NamedPipeClientStream(
        ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    connectCts.CancelAfter(TimeSpan.FromSeconds(10));
    await pipe.ConnectAsync(connectCts.Token);

    // Send command as UTF-16LE (matches driver's wchar_t buffer)
    var payload = Encoding.Unicode.GetBytes(command);
    await pipe.WriteAsync(payload, ct);
    await pipe.FlushAsync(ct);

    // Read until driver disconnects
    var buffer = new byte[512];
    var sb = new StringBuilder();
    int bytesRead;
    while ((bytesRead = await pipe.ReadAsync(buffer, ct)) > 0)
        sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

    return sb.ToString().Trim('\0').Trim();
}

// Usage:
string pong = await SendDriverCommandAsync("PING");
Console.WriteLine(pong);  // "PONG"

await SendDriverCommandAsync("SETDISPLAYCOUNT 2");
Console.WriteLine("Activated 2 virtual monitors");

await SendDriverCommandAsync("SETDISPLAYCOUNT 0");
Console.WriteLine("Removed all virtual monitors");
```

### Response Parsing Helpers

Most responses are log messages, not structured data. These snippets help extract useful information:

```csharp
// PING: check for success
bool isDriverAlive = response.Contains("PONG");

// GPU queries: extract GPU names from log lines
var gpuNames = response.Split('\n')
    .Where(line => line.Contains("GPU:"))
    .Select(line => line.Substring(line.IndexOf("GPU:") + 4).Trim())
    .ToList();

// GETSETTINGS: parse the structured response
// Response format: "SETTINGS DEBUG=true|false LOG=true|false"
var match = Regex.Match(settings, @"DEBUG=(\w+)\s+LOG=(\w+)");
bool debugEnabled = match.Groups[1].Value == "true";
bool loggingEnabled = match.Groups[2].Value == "true";
```

### C++

```cpp
#include <windows.h>
#include <string>
#include <vector>
#include <stdexcept>

// Note: This function decodes responses as UTF-8, which is correct for all commands
// EXCEPT GETSETTINGS (which responds in UTF-16LE). For GETSETTINGS, decode the
// response buffer as wchar_t* instead.
std::string SendDriverCommand(const std::wstring& command)
{
    // Connect to the driver pipe
    HANDLE hPipe = CreateFileW(
        L"\\\\.\\pipe\\MTTVirtualDisplayPipe",
        GENERIC_READ | GENERIC_WRITE,
        0, nullptr, OPEN_EXISTING, 0, nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
        throw std::runtime_error("Failed to connect to driver pipe");

    // Send command as UTF-16LE (wchar_t)
    DWORD bytesWritten = 0;
    DWORD cmdSize = static_cast<DWORD>(command.size() * sizeof(wchar_t));
    if (!WriteFile(hPipe, command.c_str(), cmdSize, &bytesWritten, nullptr))
    {
        CloseHandle(hPipe);
        throw std::runtime_error("Failed to write command");
    }
    FlushFileBuffers(hPipe);

    // Read until driver disconnects
    std::string response;
    char buffer[512];
    DWORD bytesRead = 0;
    while (ReadFile(hPipe, buffer, sizeof(buffer), &bytesRead, nullptr) && bytesRead > 0)
        response.append(buffer, bytesRead);

    CloseHandle(hPipe);
    return response;
}

// Usage:
// std::string pong = SendDriverCommand(L"PING");
// // pong == "PONG"
//
// SendDriverCommand(L"SETDISPLAYCOUNT 2");
// SendDriverCommand(L"SETDISPLAYCOUNT 0");
```

### Python

> **Dependency:** Requires `pywin32` (`pip install pywin32`).

```python
import win32file
import win32pipe

PIPE_NAME = r"\\.\pipe\MTTVirtualDisplayPipe"


def send_driver_command(command: str) -> str:
    """
    Send a single command to the Virtual Display Driver.
    Opens a new pipe connection for each command (one-shot protocol).

    Note: This function decodes responses as UTF-8, which is correct for all
    commands EXCEPT GETSETTINGS (which responds in UTF-16LE). For GETSETTINGS,
    decode the response bytes with "utf-16-le" instead.
    """
    # Connect
    handle = win32file.CreateFile(
        PIPE_NAME,
        win32file.GENERIC_READ | win32file.GENERIC_WRITE,
        0, None,
        win32file.OPEN_EXISTING,
        0, None,
    )

    # Send command as UTF-16LE
    payload = command.encode("utf-16-le")
    win32file.WriteFile(handle, payload)
    win32file.FlushFileBuffers(handle)

    # Read until driver disconnects
    response = b""
    while True:
        try:
            _, data = win32file.ReadFile(handle, 512)
            if not data:
                break
            response += data
        except Exception:
            break

    win32file.CloseHandle(handle)
    return response.decode("utf-8", errors="replace").strip("\x00").strip()


if __name__ == "__main__":
    # Health check
    pong = send_driver_command("PING")
    print(f"PING response: {pong}")  # "PONG"

    # Activate 2 virtual monitors
    send_driver_command("SETDISPLAYCOUNT 2")
    print("Activated 2 virtual monitors")

    # Remove all
    send_driver_command("SETDISPLAYCOUNT 0")
    print("Removed all virtual monitors")
```

> **Reference implementation:** The upstream project maintains a full-featured Python pipe control tool at [VirtualDrivers/Python-VDD-Pipe-Control](https://github.com/VirtualDrivers/Python-VDD-Pipe-Control).

### PowerShell

```powershell
$pipeName = "MTTVirtualDisplayPipe"

function Send-DriverCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command
    )

    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
        ".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous)

    try {
        $pipe.Connect(10000)  # 10-second timeout

        # Send command as UTF-16LE
        $payload = [System.Text.Encoding]::Unicode.GetBytes($Command)
        $pipe.Write($payload, 0, $payload.Length)
        $pipe.Flush()

        # Read until driver disconnects
        $buffer = [byte[]]::new(512)
        $sb = [System.Text.StringBuilder]::new()
        while (($bytesRead = $pipe.Read($buffer, 0, $buffer.Length)) -gt 0) {
            [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead))
        }

        return $sb.ToString().Trim([char]0).Trim()
    }
    finally {
        $pipe.Dispose()
    }
}

# Usage:
# $pong = Send-DriverCommand -Command "PING"
# Write-Host "PING response: $pong"  # "PONG"
#
# Send-DriverCommand -Command "SETDISPLAYCOUNT 2"
# Write-Host "Activated 2 virtual monitors"
#
# Send-DriverCommand -Command "SETDISPLAYCOUNT 0"
# Write-Host "Removed all virtual monitors"
```

> **Note:** This function decodes responses as UTF-8, which is correct for all commands except `GETSETTINGS`. For `GETSETTINGS`, replace `[System.Text.Encoding]::UTF8` with `[System.Text.Encoding]::Unicode` (UTF-16LE).

---

## 9. Error Handling Best Practices

### Connect timeout

Always use a timeout when connecting to the pipe. The driver may not be running, or it may be busy processing a previous command:

```csharp
using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
connectCts.CancelAfter(TimeSpan.FromSeconds(10));
await pipe.ConnectAsync(connectCts.Token);
```

### Read until disconnect before sending the next command

The one-shot protocol requires you to fully consume the response (read until 0 bytes) before opening a new connection. Failing to do so can leave the driver in an inconsistent state:

```csharp
var buffer = new byte[512];
while (await pipe.ReadAsync(buffer) > 0) { }
// Now safe to open a new connection for the next command
```

### Serialize commands

Because each command triggers a one-shot connection and `SETDISPLAYCOUNT` triggers a full adapter reload, commands must be sent one at a time. Use a `SemaphoreSlim` or mutex to prevent concurrent callers:

```csharp
private readonly SemaphoreSlim _pipeLock = new(1, 1);

await _pipeLock.WaitAsync(ct);
try
{
    // send command, read response
}
finally
{
    _pipeLock.Release();
}
```

### Exception handling

Handle these exception types:

| Exception | Cause |
|---|---|
| `IOException` | Pipe broken or disconnected unexpectedly |
| `OperationCanceledException` | Connect timeout or caller cancellation |
| `ObjectDisposedException` | Pipe accessed after disposal (application shutdown) |

```csharp
catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
{
    IsConnected = false;
    _logger.LogWarning(ex, "Pipe communication error for command '{Command}'", command);
    throw;
}
```

### Connect retry with backoff

The driver pipe may not be immediately available after driver installation, system boot, or a preceding `SETDISPLAYCOUNT` reload. Implement retry with exponential backoff. Note that each attempt needs a fresh pipe instance and cancellation token source:

```csharp
int delayMs = 200;
for (int attempt = 0; attempt < 5; attempt++)
{
    try
    {
        await using var pipe = new NamedPipeClientStream(
            ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(connectCts.Token);

        // Connected -- send command and read response here
        break;
    }
    catch (OperationCanceledException) when (attempt < 4)
    {
        await Task.Delay(delayMs, ct);
        delayMs *= 2;
    }
}
```

---

## 10. Limits and Caveats

| Constraint | Details |
|---|---|
| **Adapter reload** | `SETDISPLAYCOUNT` and many toggle/GPU commands trigger a full IddCx adapter reinitialization. Avoid rapid successive calls. |
| **Command buffer limit** | The driver reads into `wchar_t buffer[128]` -- max 127 characters (254 bytes). Longer commands are silently truncated. |
| **Prefix matching** | Commands are matched using `wcsncmp` (prefix matching), not exact comparison. For example, `PING_EXTRA` would match the `PING` handler. Avoid sending commands with trailing text beyond the documented parameters. |
| **No per-monitor pipe configuration** | The pipe controls monitor count and driver-level settings only. Per-monitor resolution, refresh rate, and display properties are configured through `vdd_settings.xml`. |
| **No per-monitor status reporting** | The pipe does not report individual monitor details. Clients must track monitor count locally. |
| **One command per connection** | The driver processes a single command per pipe connection, then disconnects. Open a new connection for each command. |
| **Driver crash risk** | The integration test suite warns: "Too many reloads can crash the driver." Space out calls to any command that triggers `ReloadDriver` and avoid calling them in rapid loops or cleanup/teardown code. |
| **Response encoding varies** | Most commands respond with UTF-8 via the log-through-pipe mechanism. `GETSETTINGS` is the exception -- it responds with UTF-16LE. See the encoding table in Section 3. |
| **Interleaved log output** | When `SendLogsThroughPipe` is enabled (default), responses contain interleaved driver log messages, not structured data. See Section 5. |
| **RELOAD_DRIVER is dangerous** | The driver accepts `RELOAD_DRIVER` as a direct command, but sending it crashes the driver ([issue #351](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/351)). Use `SETDISPLAYCOUNT` instead. |
