# Virtual Display Driver API

A C#/.NET 8 wrapper library for the [Virtual Display Driver (VDD)](https://github.com/VirtualDrivers/Virtual-Display-Driver) — a Windows Indirect Display Driver that creates virtual monitors without physical hardware.

This library provides a safe, typed API over the VDD named-pipe protocol, handling connection lifecycle, command serialization, reload spacing, and response parsing.

## Prerequisites

- Windows 10 or 11
- .NET 8.0 or later
- [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) installed and running

## Quick Start

### Standalone usage

```csharp
using VirtualDisplayDriver;
using VirtualDisplayDriver.Pipe;

var options = new VirtualDisplayOptions();
var client = new VddPipeClient(options);
await using var manager = new VirtualDisplayManager(client, options);

// Check if the driver is running
if (await manager.PingAsync())
{
    // Add 2 virtual monitors
    await manager.SetDisplayCountAsync(2);

    // Query GPU info
    var gpus = await manager.GetAllGpusAsync();
    foreach (var gpu in gpus)
        Console.WriteLine($"GPU: {gpu}");

    // Remove all monitors
    await manager.RemoveAllDisplaysAsync();
}
```

### With dependency injection

```csharp
using VirtualDisplayDriver.DependencyInjection;

services.AddVirtualDisplayDriver(opts =>
{
    opts.ConnectTimeout = TimeSpan.FromSeconds(5);
    opts.ReloadSpacing = TimeSpan.FromSeconds(30);
});
```

Then inject `IVirtualDisplayManager`:

```csharp
public class MyService(IVirtualDisplayManager displayManager)
{
    public async Task SetupDisplaysAsync()
    {
        await displayManager.SetDisplayCountAsync(2);
    }
}
```

## Architecture

The library has two layers:

- **`IVirtualDisplayManager`** — High-level managed API with command serialization, automatic reload spacing, display count tracking, and response parsing. This is what most consumers should use.
- **`IVddPipeClient`** — Low-level pipe client that sends raw commands and returns raw strings. No safety guardrails. Available as an escape hatch via `manager.PipeClient`.

## Documentation

- **[API Wrapper Reference](docs/api-wrapper-reference.md)** — Full API reference for this library
- **[VDD Overview](docs/virtual-display-driver-overview.md)** — What VDD is, key capabilities, architecture, and configuration
- **[Named Pipe API Reference](docs/named-pipe-api-reference.md)** — Raw pipe protocol commands, encoding, and multi-language examples
- **[Named Pipe Technical Deep Dive](docs/named-pipe-technical-deep-dive.md)** — Wire protocol, connection model, thread safety, and encoding details
