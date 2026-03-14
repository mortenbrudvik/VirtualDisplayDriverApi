---
name: vdd-protocol
description: Virtual Display Driver (VDD) named pipe protocol reference for writing client code that communicates with the MttVDD driver, adding or removing virtual monitors programmatically via MTTVirtualDisplayPipe, and understanding the VDD wire protocol, commands, encoding, and architecture. Covers all 17 pipe commands, connection lifecycle, thread safety, and critical gotchas like the RELOAD_DRIVER crash bug.
---

# VDD Named Pipe Protocol

The [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MttVDD) exposes a named pipe at `\\.\pipe\MTTVirtualDisplayPipe` for runtime control of virtual monitors on Windows. Protocol stable since driver release 24.12.

## Reference Files

Consult these for detailed information beyond what's in this skill:

| File | When to read |
|---|---|
| `references/named-pipe-api-reference.md` | Need Python/C++/PowerShell code, per-command property tables, GETSETTINGS registry fallback details, or retry/backoff patterns |
| `references/named-pipe-technical-deep-dive.md` | Need sequence diagrams, understanding why RELOAD_DRIVER crashes, thread-safety deep dive, or encoding edge cases |
| `references/virtual-display-driver-overview.md` | Need driver installation steps, vdd_settings.xml setup, architecture overview, capabilities, or known limitations |

---

## Protocol Essentials

### Connection Model: One-Shot

Each command requires its own pipe connection. The driver processes exactly one command, then disconnects. The client detects completion when `ReadAsync` returns 0 bytes.

### Wire Format

- **Commands**: Encode as **UTF-16LE** (`Encoding.Unicode` in .NET). No framing, no length prefix.
- **Responses**: **UTF-8** for all commands except `GETSETTINGS` which returns **UTF-16LE**.
- **Max command length**: 127 `wchar_t` (254 bytes). Longer commands are silently truncated.
- **Prefix matching**: Driver uses `wcsncmp`, not exact match. `PING_EXTRA` would match `PING`. Always send exact documented strings.

### Pipe Properties

| Property | Value |
|---|---|
| Pipe name | `\\.\pipe\MTTVirtualDisplayPipe` |
| Direction | InOut (duplex) |
| Server mode | `PIPE_TYPE_MESSAGE \| PIPE_READMODE_MESSAGE \| PIPE_WAIT` |
| Server buffers | 512 bytes in, 512 bytes out |
| Security | SDDL `D:(A;;GA;;;WD)` — full control to Everyone |
| Connect timeout | 10 seconds recommended |

---

## Command Reference

| Command | Parameters | Triggers Reload | Response Encoding |
|---|---|---|---|
| `PING` | None | No | UTF-8 |
| `SETDISPLAYCOUNT` | `N` (integer) | Yes | UTF-8 |
| `GETSETTINGS` | None | No | **UTF-16LE** |
| `RELOAD_DRIVER` | None | Yes (**BROKEN — do not use**) | N/A |
| `LOG_DEBUG` | `true\|false` | No | UTF-8 |
| `LOGGING` | `true\|false` | No | UTF-8 |
| `HDRPLUS` | `true\|false` | Yes (valid param only) | UTF-8 |
| `SDR10` | `true\|false` | Yes (valid param only) | UTF-8 |
| `CUSTOMEDID` | `true\|false` | Yes (valid param only) | UTF-8 |
| `PREVENTSPOOF` | `true\|false` | Yes (valid param only) | UTF-8 |
| `CEAOVERRIDE` | `true\|false` | Yes (valid param only) | UTF-8 |
| `HARDWARECURSOR` | `true\|false` | Yes (valid param only) | UTF-8 |
| `D3DDEVICEGPU` | None | No | UTF-8 |
| `IDDCXVERSION` | None | No | UTF-8 |
| `GETASSIGNEDGPU` | None | No | UTF-8 |
| `GETALLGPUS` | None | No | UTF-8 |
| `SETGPU` | `"name"` (quoted) | Yes | UTF-8 |

---

## Critical Gotchas

1. **RELOAD_DRIVER is broken.** Never send `RELOAD_DRIVER` directly — it causes undefined behavior due to a type mismatch bug ([issue #351](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/351)). The function receives a pipe HANDLE but passes it to `WdfObjectGet_IndirectDeviceContextWrapper` which expects a WDF device object. Use `SETDISPLAYCOUNT` instead — it persists the count to XML first (the intended workflow) before triggering reload.

2. **Rapid reloads crash the driver.** Space out any command that triggers ReloadDriver. Never call them in loops or cleanup/teardown code. A typical reload takes 2–8 seconds; use 30+ second read timeout.

3. **No display count query.** There is no command to get the current monitor count. Clients must track display count locally and pass the updated total to `SETDISPLAYCOUNT`.

4. **GETSETTINGS encoding differs.** It's the only command that responds in UTF-16LE (format: `SETTINGS DEBUG=true|false LOG=true|false`). All others use UTF-8.

5. **PING response includes log data.** With `SendLogsThroughPipe` enabled (the default), `PING` returns `PONG` plus additional log lines. Check success with `response.Contains("PONG")`, not `response == "PONG"`.

6. **Interleaved log output.** Most responses contain interleaved driver log messages, not structured data. `SendLogsThroughPipe` is enabled by default and cannot be toggled via the pipe — only by editing `vdd_settings.xml` directly.

7. **SETGPU requires quoted name.** Send `SETGPU "NVIDIA GeForce RTX 4090"` with double quotes around the GPU name. Get available names from `GETALLGPUS` first. Omitting quotes causes silent failure.

8. **Pipe vs XML roles.** The pipe controls how many monitors are active. Per-monitor configuration (resolution, refresh rate, color, HDR, EDID) lives in `C:\VirtualDisplayDriver\vdd_settings.xml`. See `references/virtual-display-driver-overview.md` for setup and installation.

---

## C# Client Pattern

This is the canonical one-shot pattern with serialization:

```csharp
private readonly SemaphoreSlim _pipeLock = new(1, 1);

async Task<string> SendCommandAsync(string command, CancellationToken ct)
{
    await _pipeLock.WaitAsync(ct);
    try
    {
        await using var pipe = new NamedPipeClientStream(
            ".", "MTTVirtualDisplayPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(connectCts.Token);

        // Send command as UTF-16LE
        var payload = Encoding.Unicode.GetBytes(command);
        await pipe.WriteAsync(payload, ct);
        await pipe.FlushAsync(ct);

        // Read until driver disconnects (use 30s+ timeout for reload commands)
        var buffer = new byte[512];
        var sb = new StringBuilder();
        int bytesRead;
        while ((bytesRead = await pipe.ReadAsync(buffer, ct)) > 0)
            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

        return sb.ToString().Trim('\0').Trim();
    }
    finally
    {
        _pipeLock.Release();
    }
}
```

The `SemaphoreSlim` ensures only one command is in flight at a time — the read-until-disconnect pattern means the driver has fully completed (including any reload) before the lock is released.

For `GETSETTINGS`, accumulate bytes into a `MemoryStream` and decode with `Encoding.Unicode` after the read loop. The response format is `SETTINGS DEBUG=true|false LOG=true|false`.

For code examples in **C++**, **Python**, and **PowerShell**, see `references/named-pipe-api-reference.md`.

---

## Thread Safety

Serialize all pipe commands with a binary semaphore (`SemaphoreSlim(1, 1)` in C#). The driver can crash or enter an inconsistent state if a second command arrives mid-reload. The read-until-disconnect loop ensures the driver has fully completed (including any reload) before the semaphore is released — the driver calls `DisconnectNamedPipe` only after processing finishes.

---

## Error Handling

- **IOException**: Pipe communication failure (broken pipe, driver unavailable).
- **OperationCanceledException**: Connect or read timeout exceeded.
- **ObjectDisposedException**: Pipe was disposed during an async operation.

Implement retry with exponential backoff for connect failures. Each retry needs a fresh `NamedPipeClientStream` instance and `CancellationTokenSource`. For commands that trigger ReloadDriver, use a 30+ second read timeout — reloads typically take 2–8 seconds but can be slower on loaded systems.
