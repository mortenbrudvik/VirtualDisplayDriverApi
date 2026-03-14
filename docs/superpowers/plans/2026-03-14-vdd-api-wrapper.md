# VDD API Wrapper Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a C#/.NET 8 API wrapper library for the Virtual Display Driver named-pipe protocol with a low-level pipe client and high-level managed API.

**Architecture:** Two-layer design — a stateless `VddPipeClient` handles raw one-shot pipe I/O with per-command encoding, and a `VirtualDisplayManager` adds command serialization, reload spacing, display count tracking, and response parsing. Single project, namespace-separated.

**Tech Stack:** .NET 8.0-windows, xUnit, FluentAssertions, NSubstitute, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Logging.Abstractions

**Spec:** `docs/superpowers/specs/2026-03-14-vdd-api-wrapper-design.md`

**Protocol docs:** `docs/named-pipe-api-reference.md`, `docs/named-pipe-technical-deep-dive.md`

---

## File Map

### Source files (`src/VirtualDisplayDriver/`)

| File | Responsibility |
|---|---|
| `VirtualDisplayDriver.csproj` | Project file, net8.0-windows, package refs |
| `Pipe/PipeConstants.cs` | Pipe name (`MTTVirtualDisplayPipe`), read buffer size (512) |
| `Pipe/IVddPipeClient.cs` | Interface: 6 async methods returning raw strings |
| `Pipe/VddPipeClient.cs` | Implementation: one-shot pipe connections, encoding, command formatting |
| `Models/DriverSettings.cs` | `record DriverSettings(bool DebugLogging, bool Logging)` |
| `Exceptions/VddException.cs` | Base exception |
| `Exceptions/PipeConnectionException.cs` | Pipe unavailable / timeout |
| `Exceptions/CommandException.cs` | Unexpected response / parse failure |
| `VirtualDisplayOptions.cs` | `ConnectTimeout`, `ReloadSpacing` |
| `IVirtualDisplayManager.cs` | Interface: full managed API |
| `VirtualDisplayManager.cs` | Implementation: serialization, spacing, tracking, parsing |
| `DependencyInjection/ServiceCollectionExtensions.cs` | `AddVirtualDisplayDriver()` |

### Test files (`tests/VirtualDisplayDriver.Tests/`)

| File | What it tests |
|---|---|
| `VirtualDisplayDriver.Tests.csproj` | Test project, refs xUnit/FluentAssertions/NSubstitute |
| `Exceptions/ExceptionTests.cs` | Exception hierarchy and constructors |
| `Models/DriverSettingsTests.cs` | Record equality |
| `Pipe/VddPipeClientCommandFormattingTests.cs` | Command string construction (via integration or reflection) |
| `VirtualDisplayManagerTests.cs` | Core manager: serialization, spacing, count tracking, disposal |
| `VirtualDisplayManagerPingTests.cs` | PingAsync behavior (success, failure, non-PONG) |
| `VirtualDisplayManagerDisplayCountTests.cs` | Set/Add/Remove validation and clamping |
| `VirtualDisplayManagerParsingTests.cs` | GetSettings, GPU query parsing |
| `VirtualDisplayManagerToggleTests.cs` | Toggle command delegation and reload spacing |
| `DependencyInjection/ServiceCollectionExtensionsTests.cs` | DI registration |

### Integration test files (`tests/VirtualDisplayDriver.IntegrationTests/`)

| File | What it tests |
|---|---|
| `VirtualDisplayDriver.IntegrationTests.csproj` | Integration test project (opt-in, not CI) |
| `PipeClientIntegrationTests.cs` | Round-trip: PING, GETSETTINGS, query commands against real driver |

---

## Chunk 1: Project Setup & Foundation

### Task 1: Create solution and projects

**Files:**
- Create: `src/VirtualDisplayDriver/VirtualDisplayDriver.csproj`
- Create: `tests/VirtualDisplayDriver.Tests/VirtualDisplayDriver.Tests.csproj`
- Create: `tests/VirtualDisplayDriver.IntegrationTests/VirtualDisplayDriver.IntegrationTests.csproj`
- Create: `VirtualDisplayDriverApi.sln`

- [ ] **Step 1: Create solution and library project**

Run from the repo root (`C:\code\nuget-packages\VirtualDisplayDriverApi`):

```bash
dotnet new sln -n VirtualDisplayDriverApi
dotnet new classlib -n VirtualDisplayDriver -o src/VirtualDisplayDriver -f net8.0
dotnet sln add src/VirtualDisplayDriver/VirtualDisplayDriver.csproj
```

- [ ] **Step 2: Configure the library project**

Replace `src/VirtualDisplayDriver/VirtualDisplayDriver.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>VirtualDisplayDriver</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.*" />
  </ItemGroup>

</Project>
```

Delete the auto-generated `Class1.cs`.

- [ ] **Step 3: Create the unit test project**

```bash
dotnet new xunit -n VirtualDisplayDriver.Tests -o tests/VirtualDisplayDriver.Tests -f net8.0
dotnet sln add tests/VirtualDisplayDriver.Tests/VirtualDisplayDriver.Tests.csproj
dotnet add tests/VirtualDisplayDriver.Tests/VirtualDisplayDriver.Tests.csproj reference src/VirtualDisplayDriver/VirtualDisplayDriver.csproj
```

Replace `tests/VirtualDisplayDriver.Tests/VirtualDisplayDriver.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VirtualDisplayDriver\VirtualDisplayDriver.csproj" />
  </ItemGroup>

</Project>
```

Delete the auto-generated `UnitTest1.cs`.

- [ ] **Step 4: Create the integration test project**

```bash
dotnet new xunit -n VirtualDisplayDriver.IntegrationTests -o tests/VirtualDisplayDriver.IntegrationTests -f net8.0
dotnet sln add tests/VirtualDisplayDriver.IntegrationTests/VirtualDisplayDriver.IntegrationTests.csproj
dotnet add tests/VirtualDisplayDriver.IntegrationTests/VirtualDisplayDriver.IntegrationTests.csproj reference src/VirtualDisplayDriver/VirtualDisplayDriver.csproj
```

Replace `tests/VirtualDisplayDriver.IntegrationTests/VirtualDisplayDriver.IntegrationTests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VirtualDisplayDriver\VirtualDisplayDriver.csproj" />
  </ItemGroup>

</Project>
```

Delete the auto-generated `UnitTest1.cs`.

- [ ] **Step 5: Create directory structure**

```bash
mkdir -p src/VirtualDisplayDriver/Pipe
mkdir -p src/VirtualDisplayDriver/Models
mkdir -p src/VirtualDisplayDriver/Exceptions
mkdir -p src/VirtualDisplayDriver/DependencyInjection
mkdir -p tests/VirtualDisplayDriver.Tests/Exceptions
mkdir -p tests/VirtualDisplayDriver.Tests/Models
mkdir -p tests/VirtualDisplayDriver.Tests/Pipe
mkdir -p tests/VirtualDisplayDriver.Tests/DependencyInjection
```

- [ ] **Step 6: Build to verify setup**

```bash
dotnet build VirtualDisplayDriverApi.sln
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```
feat: scaffold solution with library and test projects
```

---

### Task 2: Exceptions

**Files:**
- Create: `src/VirtualDisplayDriver/Exceptions/VddException.cs`
- Create: `src/VirtualDisplayDriver/Exceptions/PipeConnectionException.cs`
- Create: `src/VirtualDisplayDriver/Exceptions/CommandException.cs`
- Create: `tests/VirtualDisplayDriver.Tests/Exceptions/ExceptionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VirtualDisplayDriver.Tests/Exceptions/ExceptionTests.cs`:

```csharp
using FluentAssertions;
using VirtualDisplayDriver;

namespace VirtualDisplayDriver.Tests.Exceptions;

public class ExceptionTests
{
    [Fact]
    public void VddException_IsException()
    {
        var ex = new VddException("test");
        ex.Should().BeAssignableTo<Exception>();
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void VddException_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new VddException("test", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void PipeConnectionException_IsVddException()
    {
        var ex = new PipeConnectionException("pipe broken");
        ex.Should().BeAssignableTo<VddException>();
    }

    [Fact]
    public void PipeConnectionException_WithInnerException()
    {
        var inner = new IOException("pipe error");
        var ex = new PipeConnectionException("failed", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void CommandException_IsVddException()
    {
        var ex = new CommandException("bad response");
        ex.Should().BeAssignableTo<VddException>();
    }

    [Fact]
    public void CommandException_WithRawResponse()
    {
        var ex = new CommandException("parse failed", "RAW_DATA");
        ex.RawResponse.Should().Be("RAW_DATA");
        ex.Message.Should().Contain("parse failed");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~ExceptionTests" --no-restore
```

Expected: FAIL — types not found.

- [ ] **Step 3: Implement exceptions**

Create `src/VirtualDisplayDriver/Exceptions/VddException.cs`:

```csharp
namespace VirtualDisplayDriver;

public class VddException : Exception
{
    public VddException(string message) : base(message) { }
    public VddException(string message, Exception innerException) : base(message, innerException) { }
}
```

Create `src/VirtualDisplayDriver/Exceptions/PipeConnectionException.cs`:

```csharp
namespace VirtualDisplayDriver;

public class PipeConnectionException : VddException
{
    public PipeConnectionException(string message) : base(message) { }
    public PipeConnectionException(string message, Exception innerException) : base(message, innerException) { }
}
```

Create `src/VirtualDisplayDriver/Exceptions/CommandException.cs`:

```csharp
namespace VirtualDisplayDriver;

public class CommandException : VddException
{
    public string? RawResponse { get; }

    public CommandException(string message) : base(message) { }
    public CommandException(string message, string rawResponse) : base(message)
    {
        RawResponse = rawResponse;
    }
    public CommandException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~ExceptionTests" --no-restore
```

Expected: 6 passed.

- [ ] **Step 5: Commit**

```
feat: add VddException, PipeConnectionException, CommandException
```

---

### Task 3: Options, Constants & Models

**Files:**
- Create: `src/VirtualDisplayDriver/VirtualDisplayOptions.cs`
- Create: `src/VirtualDisplayDriver/Pipe/PipeConstants.cs`
- Create: `src/VirtualDisplayDriver/Models/DriverSettings.cs`
- Create: `tests/VirtualDisplayDriver.Tests/Models/DriverSettingsTests.cs`

- [ ] **Step 1: Write the failing tests for DriverSettings and VirtualDisplayOptions**

Create `tests/VirtualDisplayDriver.Tests/Models/DriverSettingsTests.cs`:

```csharp
using FluentAssertions;
using VirtualDisplayDriver;

namespace VirtualDisplayDriver.Tests.Models;

public class DriverSettingsTests
{
    [Fact]
    public void DriverSettings_RecordEquality()
    {
        var a = new DriverSettings(true, false);
        var b = new DriverSettings(true, false);
        a.Should().Be(b);
    }

    [Fact]
    public void DriverSettings_RecordInequality()
    {
        var a = new DriverSettings(true, false);
        var b = new DriverSettings(false, false);
        a.Should().NotBe(b);
    }

    [Fact]
    public void DriverSettings_Properties()
    {
        var settings = new DriverSettings(DebugLogging: true, Logging: false);
        settings.DebugLogging.Should().BeTrue();
        settings.Logging.Should().BeFalse();
    }

    [Fact]
    public void VirtualDisplayOptions_Defaults()
    {
        var opts = new VirtualDisplayOptions();
        opts.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(10));
        opts.ReloadSpacing.Should().Be(TimeSpan.FromSeconds(30));
        opts.InitialDisplayCount.Should().Be(0);
    }

    [Fact]
    public void VirtualDisplayOptions_RespectsInitialCount()
    {
        var opts = new VirtualDisplayOptions { InitialDisplayCount = 5 };
        opts.InitialDisplayCount.Should().Be(5);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~DriverSettingsTests" --no-restore
```

Expected: FAIL — type not found.

- [ ] **Step 3: Implement Options, Constants, and DriverSettings**

Create `src/VirtualDisplayDriver/VirtualDisplayOptions.cs`:

```csharp
namespace VirtualDisplayDriver;

public class VirtualDisplayOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReloadSpacing { get; set; } = TimeSpan.FromSeconds(30);
    public int InitialDisplayCount { get; set; }
}
```

Create `src/VirtualDisplayDriver/Pipe/PipeConstants.cs`:

```csharp
namespace VirtualDisplayDriver.Pipe;

internal static class PipeConstants
{
    public const string PipeName = "MTTVirtualDisplayPipe";
    public const int ReadBufferSize = 512;
}
```

Create `src/VirtualDisplayDriver/Models/DriverSettings.cs`:

```csharp
namespace VirtualDisplayDriver;

public record DriverSettings(bool DebugLogging, bool Logging);
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~DriverSettingsTests" --no-restore
```

Expected: 5 passed.

- [ ] **Step 5: Commit**

```
feat: add VirtualDisplayOptions, PipeConstants, DriverSettings
```

---

## Chunk 2: Low-Level Pipe Client

### Task 4: IVddPipeClient interface

**Files:**
- Create: `src/VirtualDisplayDriver/Pipe/IVddPipeClient.cs`

- [ ] **Step 1: Create the interface**

Create `src/VirtualDisplayDriver/Pipe/IVddPipeClient.cs`:

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

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/VirtualDisplayDriver
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
feat: add IVddPipeClient interface
```

---

### Task 5: VddPipeClient implementation

**Files:**
- Create: `src/VirtualDisplayDriver/Pipe/VddPipeClient.cs`

This is the core pipe I/O implementation. It is not unit-testable in isolation (requires a live named pipe) — it will be covered by integration tests in Task 11.

- [ ] **Step 1: Implement VddPipeClient**

Create `src/VirtualDisplayDriver/Pipe/VddPipeClient.cs`:

```csharp
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
        => SendUtf8CommandAsync($"SETGPU \"{gpuName}\"", ct);

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

            var payload = Encoding.Unicode.GetBytes(command);
            await pipe.WriteAsync(payload, ct);
            await pipe.FlushAsync(ct);

            using var ms = new MemoryStream();
            var buffer = new byte[PipeConstants.ReadBufferSize];
            int bytesRead;
            while ((bytesRead = await pipe.ReadAsync(buffer, ct)) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
            }

            _logger.LogDebug("Command {Command} completed, received {ByteCount} bytes", command, ms.Length);
            return ms.ToArray();
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Connection timeout for command: {Command}", command);
            throw new PipeConnectionException(
                $"Connection to VDD pipe timed out after {_options.ConnectTimeout.TotalSeconds}s for command: {command}", ex);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Pipe I/O error for command: {Command}", command);
            throw new PipeConnectionException(
                $"Pipe communication failed for command: {command}", ex);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/VirtualDisplayDriver
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
feat: implement VddPipeClient with one-shot pipe I/O
```

---

## Chunk 3: High-Level Manager — Core Behavior

### Task 6: IVirtualDisplayManager interface

**Files:**
- Create: `src/VirtualDisplayDriver/IVirtualDisplayManager.cs`

- [ ] **Step 1: Create the interface**

Create `src/VirtualDisplayDriver/IVirtualDisplayManager.cs`:

```csharp
using VirtualDisplayDriver.Pipe;

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

    Task SetHdrPlusAsync(bool enabled, CancellationToken ct = default);
    Task SetSdr10BitAsync(bool enabled, CancellationToken ct = default);
    Task SetCustomEdidAsync(bool enabled, CancellationToken ct = default);
    Task SetPreventSpoofAsync(bool enabled, CancellationToken ct = default);
    Task SetCeaOverrideAsync(bool enabled, CancellationToken ct = default);
    Task SetHardwareCursorAsync(bool enabled, CancellationToken ct = default);

    Task SetDebugLoggingAsync(bool enabled, CancellationToken ct = default);
    Task SetLoggingAsync(bool enabled, CancellationToken ct = default);

    Task<string> GetD3DDeviceGpuAsync(CancellationToken ct = default);
    Task<string> GetIddCxVersionAsync(CancellationToken ct = default);
    Task<string> GetAssignedGpuAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllGpusAsync(CancellationToken ct = default);
    Task SetGpuAsync(string gpuName, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/VirtualDisplayDriver
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
feat: add IVirtualDisplayManager interface
```

---

### Task 7: VirtualDisplayManager — constructor, disposal, PingAsync

**Files:**
- Create: `src/VirtualDisplayDriver/VirtualDisplayManager.cs`
- Create: `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerTests.cs`
- Create: `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerPingTests.cs`

- [ ] **Step 1: Write failing tests for constructor, disposal, and initial state**

Create `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new();

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    [Fact]
    public async Task Constructor_SetsInitialState()
    {
        await using var manager = CreateManager();
        manager.DisplayCount.Should().Be(0);
        manager.IsConnected.Should().BeFalse();
        manager.PipeClient.Should().BeSameAs(_mockClient);
    }

    [Fact]
    public async Task Constructor_ThrowsOnNullClient()
    {
        var act = () => new VirtualDisplayManager(null!, _options);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Constructor_ThrowsOnNullOptions()
    {
        var act = () => new VirtualDisplayManager(_mockClient, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DisposeAsync_SubsequentCallsThrowObjectDisposed()
    {
        var manager = CreateManager();
        await manager.DisposeAsync();

        var act = () => manager.SetDisplayCountAsync(1);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Commands_AreSerialized()
    {
        await using var manager = CreateManager();
        var callOrder = new List<int>();
        var tcs1 = new TaskCompletionSource<string>();

        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callOrder.Add(1);
                var result = await tcs1.Task;
                callOrder.Add(2);
                return result;
            });

        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add(3);
                return Task.FromResult("1.10");
            });

        var pingTask = manager.PingAsync();
        // Give the ping task time to acquire the semaphore
        await Task.Delay(50);
        var queryTask = manager.GetIddCxVersionAsync();
        await Task.Delay(50);

        // Query should be blocked — only ping started
        callOrder.Should().Equal(1);

        // Release ping
        tcs1.SetResult("PONG");
        await pingTask;
        await queryTask;

        callOrder.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task CallerCancellation_PassesThroughAsOperationCanceledException()
    {
        await using var manager = CreateManager();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => manager.SetDisplayCountAsync(1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IsConnected_SetTrueAfterSuccessfulNonPingCommand()
    {
        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns("1.10");
        await using var manager = CreateManager();

        manager.IsConnected.Should().BeFalse();
        await manager.GetIddCxVersionAsync();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task IsConnected_SetFalseAfterNonPingCommandFailure()
    {
        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new PipeConnectionException("driver not running"));
        await using var manager = CreateManager();

        var act = () => manager.SetDisplayCountAsync(1);
        await act.Should().ThrowAsync<PipeConnectionException>();
        manager.IsConnected.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Write failing tests for PingAsync**

Create `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerPingTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerPingTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new();

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenResponseContainsPong()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>()).Returns("PONG");
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeTrue();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenResponseContainsPongWithLogData()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .Returns("PONG\n[2024-01-01] [PIPE] Heartbeat Ping");
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeTrue();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenResponseDoesNotContainPong()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>()).Returns("UNEXPECTED");
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeFalse();
        manager.IsConnected.Should().BeTrue(); // pipe worked, just unexpected response
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenPipeConnectionFails()
    {
        _mockClient.PingAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new PipeConnectionException("driver not running"));
        await using var manager = CreateManager();

        var result = await manager.PingAsync();

        result.Should().BeFalse();
        manager.IsConnected.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~VirtualDisplayManagerTests|FullyQualifiedName~VirtualDisplayManagerPingTests" --no-restore
```

Expected: FAIL — `VirtualDisplayManager` type not found.

- [ ] **Step 4: Implement VirtualDisplayManager (core + PingAsync)**

Create `src/VirtualDisplayDriver/VirtualDisplayManager.cs`:

```csharp
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
            var response = await command(ct);
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
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~VirtualDisplayManagerTests|FullyQualifiedName~VirtualDisplayManagerPingTests" --no-restore
```

Expected: 9 passed.

- [ ] **Step 6: Commit**

```
feat: implement VirtualDisplayManager with serialization, disposal, PingAsync
```

---

### Task 8: Display count validation tests

**Files:**
- Create: `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerDisplayCountTests.cs`

- [ ] **Step 1: Write tests for display count operations**

Create `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerDisplayCountTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerDisplayCountTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new() { ReloadSpacing = TimeSpan.Zero };

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    public VirtualDisplayManagerDisplayCountTests()
    {
        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");
    }

    [Fact]
    public async Task SetDisplayCountAsync_UpdatesDisplayCount()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(3);
        manager.DisplayCount.Should().Be(3);
    }

    [Fact]
    public async Task SetDisplayCountAsync_ThrowsForNegative()
    {
        await using var manager = CreateManager();
        var act = () => manager.SetDisplayCountAsync(-1);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SetDisplayCountAsync_AllowsZero()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(0);
        manager.DisplayCount.Should().Be(0);
    }

    [Fact]
    public async Task AddDisplaysAsync_IncrementsCount()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(2);
        await manager.AddDisplaysAsync(3);
        manager.DisplayCount.Should().Be(5);
        await _mockClient.Received().SetDisplayCountAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddDisplaysAsync_ThrowsForZero()
    {
        await using var manager = CreateManager();
        var act = () => manager.AddDisplaysAsync(0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AddDisplaysAsync_ThrowsForNegative()
    {
        await using var manager = CreateManager();
        var act = () => manager.AddDisplaysAsync(-1);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RemoveDisplaysAsync_DecrementsCount()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(5);
        await manager.RemoveDisplaysAsync(2);
        manager.DisplayCount.Should().Be(3);
        await _mockClient.Received().SetDisplayCountAsync(3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveDisplaysAsync_ClampsToZero()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(2);
        await manager.RemoveDisplaysAsync(5);
        manager.DisplayCount.Should().Be(0);
        await _mockClient.Received().SetDisplayCountAsync(0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveDisplaysAsync_ThrowsForZero()
    {
        await using var manager = CreateManager();
        var act = () => manager.RemoveDisplaysAsync(0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RemoveAllDisplaysAsync_SetsCountToZero()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(3);
        await manager.RemoveAllDisplaysAsync();
        manager.DisplayCount.Should().Be(0);
        await _mockClient.Received().SetDisplayCountAsync(0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisplayCount_RespectsInitialValueFromOptions()
    {
        var opts = new VirtualDisplayOptions { InitialDisplayCount = 4, ReloadSpacing = TimeSpan.Zero };
        await using var manager = new VirtualDisplayManager(_mockClient, opts);
        manager.DisplayCount.Should().Be(4);
    }

    [Fact]
    public async Task DisplayCount_UnchangedAfterFailedCommand()
    {
        await using var manager = CreateManager();
        await manager.SetDisplayCountAsync(3);
        manager.DisplayCount.Should().Be(3);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new PipeConnectionException("pipe broke"));

        var act = () => manager.SetDisplayCountAsync(5);
        await act.Should().ThrowAsync<PipeConnectionException>();
        manager.DisplayCount.Should().Be(3); // unchanged
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~VirtualDisplayManagerDisplayCountTests" --no-restore
```

Expected: 11 passed.

- [ ] **Step 3: Commit**

```
test: add display count validation and clamping tests
```

---

## Chunk 4: Manager — Parsing, Toggles & Reload Spacing

### Task 9: Response parsing tests

**Files:**
- Create: `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerParsingTests.cs`

- [ ] **Step 1: Write parsing tests**

Create `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerParsingTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerParsingTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new();

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    [Theory]
    [InlineData("SETTINGS DEBUG=true LOG=false", true, false)]
    [InlineData("SETTINGS DEBUG=false LOG=true", false, true)]
    [InlineData("SETTINGS DEBUG=true LOG=true", true, true)]
    [InlineData("SETTINGS DEBUG=false LOG=false", false, false)]
    [InlineData("SETTINGS DEBUG=True LOG=False", true, false)]
    public async Task GetSettingsAsync_ParsesAllCombinations(
        string response, bool expectedDebug, bool expectedLog)
    {
        _mockClient.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(response);
        await using var manager = CreateManager();

        var settings = await manager.GetSettingsAsync();

        settings.DebugLogging.Should().Be(expectedDebug);
        settings.Logging.Should().Be(expectedLog);
    }

    [Fact]
    public async Task GetSettingsAsync_ThrowsOnInvalidFormat()
    {
        _mockClient.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns("GARBAGE DATA");
        await using var manager = CreateManager();

        var act = () => manager.GetSettingsAsync();

        var ex = await act.Should().ThrowAsync<CommandException>();
        ex.Which.RawResponse.Should().Be("GARBAGE DATA");
    }

    [Fact]
    public async Task GetAllGpusAsync_ParsesGpuLines()
    {
        _mockClient.SendQueryAsync("GETALLGPUS", Arg.Any<CancellationToken>())
            .Returns("[LOG] GPU: NVIDIA GeForce RTX 4090\n[LOG] GPU: Intel UHD 770");
        await using var manager = CreateManager();

        var gpus = await manager.GetAllGpusAsync();

        gpus.Should().Equal("NVIDIA GeForce RTX 4090", "Intel UHD 770");
    }

    [Fact]
    public async Task GetAllGpusAsync_HandlesTimestampPrefixedLogLines()
    {
        _mockClient.SendQueryAsync("GETALLGPUS", Arg.Any<CancellationToken>())
            .Returns("[2024-01-15 10:30:45] [GPU] GPU: NVIDIA GeForce RTX 4090\n[2024-01-15 10:30:45] [GPU] GPU: Intel UHD 770");
        await using var manager = CreateManager();

        var gpus = await manager.GetAllGpusAsync();

        gpus.Should().Equal("NVIDIA GeForce RTX 4090", "Intel UHD 770");
    }

    [Fact]
    public async Task GetAllGpusAsync_ReturnsEmptyListWhenNoMatch()
    {
        _mockClient.SendQueryAsync("GETALLGPUS", Arg.Any<CancellationToken>())
            .Returns("no gpus here");
        await using var manager = CreateManager();

        var gpus = await manager.GetAllGpusAsync();

        gpus.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssignedGpuAsync_ReturnsFirstGpuMatch()
    {
        _mockClient.SendQueryAsync("GETASSIGNEDGPU", Arg.Any<CancellationToken>())
            .Returns("[LOG] Assigned GPU: NVIDIA GeForce RTX 4090");
        await using var manager = CreateManager();

        var gpu = await manager.GetAssignedGpuAsync();

        gpu.Should().Be("NVIDIA GeForce RTX 4090");
    }

    [Fact]
    public async Task GetAssignedGpuAsync_ThrowsWhenNoGpuLine()
    {
        _mockClient.SendQueryAsync("GETASSIGNEDGPU", Arg.Any<CancellationToken>())
            .Returns("no gpu info");
        await using var manager = CreateManager();

        var act = () => manager.GetAssignedGpuAsync();

        await act.Should().ThrowAsync<CommandException>();
    }

    [Fact]
    public async Task GetIddCxVersionAsync_ReturnsFullResponse()
    {
        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns("IddCx version: 1.10.0");
        await using var manager = CreateManager();

        var version = await manager.GetIddCxVersionAsync();

        version.Should().Be("IddCx version: 1.10.0");
    }

    [Fact]
    public async Task GetIddCxVersionAsync_ThrowsOnEmptyResponse()
    {
        _mockClient.SendQueryAsync("IDDCXVERSION", Arg.Any<CancellationToken>())
            .Returns("");
        await using var manager = CreateManager();

        var act = () => manager.GetIddCxVersionAsync();

        await act.Should().ThrowAsync<CommandException>();
    }

    [Fact]
    public async Task GetD3DDeviceGpuAsync_ReturnsFullResponse()
    {
        _mockClient.SendQueryAsync("D3DDEVICEGPU", Arg.Any<CancellationToken>())
            .Returns("GPU Device: NVIDIA GeForce RTX 4090");
        await using var manager = CreateManager();

        var result = await manager.GetD3DDeviceGpuAsync();

        result.Should().Be("GPU Device: NVIDIA GeForce RTX 4090");
    }

    [Fact]
    public async Task GetD3DDeviceGpuAsync_ThrowsOnEmptyResponse()
    {
        _mockClient.SendQueryAsync("D3DDEVICEGPU", Arg.Any<CancellationToken>())
            .Returns("  ");
        await using var manager = CreateManager();

        var act = () => manager.GetD3DDeviceGpuAsync();

        await act.Should().ThrowAsync<CommandException>();
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~VirtualDisplayManagerParsingTests" --no-restore
```

Expected: 10 passed.

- [ ] **Step 3: Commit**

```
test: add response parsing tests for GetSettings, GPU queries
```

---

### Task 10: Toggle and GPU command tests

**Files:**
- Create: `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerToggleTests.cs`

- [ ] **Step 1: Write toggle and SetGpu tests**

Create `tests/VirtualDisplayDriver.Tests/VirtualDisplayManagerToggleTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.Tests;

public class VirtualDisplayManagerToggleTests
{
    private readonly IVddPipeClient _mockClient = Substitute.For<IVddPipeClient>();
    private readonly VirtualDisplayOptions _options = new() { ReloadSpacing = TimeSpan.Zero };

    private VirtualDisplayManager CreateManager() => new(_mockClient, _options);

    public VirtualDisplayManagerToggleTests()
    {
        _mockClient.SendToggleAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns("");
        _mockClient.SetGpuAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetHdrPlusAsync_DelegatesToPipeClient(bool enabled)
    {
        await using var manager = CreateManager();
        await manager.SetHdrPlusAsync(enabled);
        await _mockClient.Received().SendToggleAsync("HDRPLUS", enabled, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSdr10BitAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetSdr10BitAsync(true);
        await _mockClient.Received().SendToggleAsync("SDR10", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCustomEdidAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetCustomEdidAsync(true);
        await _mockClient.Received().SendToggleAsync("CUSTOMEDID", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPreventSpoofAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetPreventSpoofAsync(false);
        await _mockClient.Received().SendToggleAsync("PREVENTSPOOF", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCeaOverrideAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetCeaOverrideAsync(true);
        await _mockClient.Received().SendToggleAsync("CEAOVERRIDE", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetHardwareCursorAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetHardwareCursorAsync(true);
        await _mockClient.Received().SendToggleAsync("HARDWARECURSOR", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDebugLoggingAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetDebugLoggingAsync(true);
        await _mockClient.Received().SendToggleAsync("LOG_DEBUG", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLoggingAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetLoggingAsync(false);
        await _mockClient.Received().SendToggleAsync("LOGGING", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetGpuAsync_DelegatesToPipeClient()
    {
        await using var manager = CreateManager();
        await manager.SetGpuAsync("NVIDIA GeForce RTX 4090");
        await _mockClient.Received().SetGpuAsync("NVIDIA GeForce RTX 4090", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetGpuAsync_ThrowsForInvalidName(string? gpuName)
    {
        await using var manager = CreateManager();
        var act = () => manager.SetGpuAsync(gpuName!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReloadSpacing_DelaysBetweenReloadCommands()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromMilliseconds(200) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.SetDisplayCountAsync(1);
        await manager.SetDisplayCountAsync(2);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(150); // allow some timing slack
    }

    [Fact]
    public async Task ReloadSpacing_HonorsCancellationToken()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromSeconds(30) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        await manager.SetDisplayCountAsync(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => manager.SetDisplayCountAsync(2, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NonReloadCommands_SkipSpacing()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromSeconds(10) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");
        _mockClient.PingAsync(Arg.Any<CancellationToken>()).Returns("PONG");

        await manager.SetDisplayCountAsync(1);

        // Non-reload commands should NOT wait for spacing
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.PingAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task SetDebugLoggingAsync_SkipsReloadSpacing()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromSeconds(10) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        await manager.SetDisplayCountAsync(1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.SetDebugLoggingAsync(true);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task SetGpuAsync_EnforcesReloadSpacing()
    {
        var options = new VirtualDisplayOptions { ReloadSpacing = TimeSpan.FromMilliseconds(200) };
        await using var manager = new VirtualDisplayManager(_mockClient, options);

        _mockClient.SetDisplayCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("");

        await manager.SetDisplayCountAsync(1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await manager.SetGpuAsync("NVIDIA GeForce RTX 4090");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(150);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~VirtualDisplayManagerToggleTests" --no-restore
```

Expected: 13 passed.

- [ ] **Step 3: Commit**

```
test: add toggle delegation, SetGpu validation, and reload spacing tests
```

---

## Chunk 5: DI Extensions & Integration Tests

### Task 11: DI ServiceCollection extensions

**Files:**
- Create: `src/VirtualDisplayDriver/DependencyInjection/ServiceCollectionExtensions.cs`
- Create: `tests/VirtualDisplayDriver.Tests/DependencyInjection/ServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write failing DI tests**

Create `tests/VirtualDisplayDriver.Tests/DependencyInjection/ServiceCollectionExtensionsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VirtualDisplayDriver.DependencyInjection;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVirtualDisplayDriver_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver();
        var provider = services.BuildServiceProvider();

        provider.GetService<IVddPipeClient>().Should().NotBeNull();
        provider.GetService<IVirtualDisplayManager>().Should().NotBeNull();
    }

    [Fact]
    public void AddVirtualDisplayDriver_RegistersAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver();
        var provider = services.BuildServiceProvider();

        var client1 = provider.GetService<IVddPipeClient>();
        var client2 = provider.GetService<IVddPipeClient>();
        client1.Should().BeSameAs(client2);

        var manager1 = provider.GetService<IVirtualDisplayManager>();
        var manager2 = provider.GetService<IVirtualDisplayManager>();
        manager1.Should().BeSameAs(manager2);
    }

    [Fact]
    public void AddVirtualDisplayDriver_AcceptsConfiguration()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver(opts =>
        {
            opts.ConnectTimeout = TimeSpan.FromSeconds(5);
            opts.ReloadSpacing = TimeSpan.FromSeconds(60);
        });
        var provider = services.BuildServiceProvider();

        var manager = provider.GetService<IVirtualDisplayManager>();
        manager.Should().NotBeNull();
    }

    [Fact]
    public void AddVirtualDisplayDriver_ManagerExposesPipeClient()
    {
        var services = new ServiceCollection();
        services.AddVirtualDisplayDriver();
        var provider = services.BuildServiceProvider();

        var manager = provider.GetRequiredService<IVirtualDisplayManager>();
        var client = provider.GetRequiredService<IVddPipeClient>();
        manager.PipeClient.Should().BeSameAs(client);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~ServiceCollectionExtensionsTests" --no-restore
```

Expected: FAIL — `AddVirtualDisplayDriver` not found.

- [ ] **Step 3: Implement ServiceCollectionExtensions**

Create `src/VirtualDisplayDriver/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualDisplayDriver(
        this IServiceCollection services,
        Action<VirtualDisplayOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<VirtualDisplayOptions>(_ => { });

        services.AddSingleton<IVddPipeClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<VirtualDisplayOptions>>().Value;
            var logger = sp.GetService<ILogger<VddPipeClient>>();
            return new VddPipeClient(options, logger);
        });

        services.AddSingleton<IVirtualDisplayManager>(sp =>
        {
            var client = sp.GetRequiredService<IVddPipeClient>();
            var options = sp.GetRequiredService<IOptions<VirtualDisplayOptions>>().Value;
            var logger = sp.GetService<ILogger<VirtualDisplayManager>>();
            return new VirtualDisplayManager(client, options, logger);
        });

        return services;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/VirtualDisplayDriver.Tests --filter "FullyQualifiedName~ServiceCollectionExtensionsTests" --no-restore
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```
feat: add DI ServiceCollection extensions
```

---

### Task 12: Integration tests (opt-in)

**Files:**
- Create: `tests/VirtualDisplayDriver.IntegrationTests/PipeClientIntegrationTests.cs`

These tests require the VDD driver to be installed and running. They are excluded from CI via a `Trait` filter.

- [ ] **Step 1: Write integration tests**

Create `tests/VirtualDisplayDriver.IntegrationTests/PipeClientIntegrationTests.cs`:

```csharp
using FluentAssertions;
using VirtualDisplayDriver;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver.IntegrationTests;

[Trait("Category", "Integration")]
public class PipeClientIntegrationTests
{
    private readonly VddPipeClient _client = new(new VirtualDisplayOptions());

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        var response = await _client.PingAsync();
        response.Should().Contain("PONG");
    }

    [Fact]
    public async Task GetSettings_ReturnsSettingsString()
    {
        var response = await _client.GetSettingsAsync();
        response.Should().Contain("SETTINGS");
        response.Should().Contain("DEBUG=");
        response.Should().Contain("LOG=");
    }

    [Fact]
    public async Task GetIddCxVersion_ReturnsNonEmpty()
    {
        var response = await _client.SendQueryAsync("IDDCXVERSION");
        response.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAllGpus_ReturnsNonEmpty()
    {
        var response = await _client.SendQueryAsync("GETALLGPUS");
        response.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Manager_Ping_ReturnsTrue()
    {
        await using var manager = new VirtualDisplayManager(_client, new VirtualDisplayOptions());
        var result = await manager.PingAsync();
        result.Should().BeTrue();
        manager.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Manager_GetSettings_ReturnsParsed()
    {
        await using var manager = new VirtualDisplayManager(_client, new VirtualDisplayOptions());
        var settings = await manager.GetSettingsAsync();
        // Just verify it parses without throwing
        settings.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build tests/VirtualDisplayDriver.IntegrationTests
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
test: add opt-in integration tests for VDD pipe protocol
```

---

### Task 13: Run all unit tests and verify

- [ ] **Step 1: Run full unit test suite**

```bash
dotnet test tests/VirtualDisplayDriver.Tests -v normal
```

Expected: All tests pass. Verify count matches expected (~50 tests across all test files).

- [ ] **Step 2: Run integration tests (if driver is installed)**

```bash
dotnet test tests/VirtualDisplayDriver.IntegrationTests --filter "Category=Integration" -v normal
```

Expected: All pass (only if VDD driver is running). Skip if driver not installed.

- [ ] **Step 3: Final commit**

```
chore: verify all tests pass
```

---

## Summary

| Chunk | Tasks | What it delivers |
|---|---|---|
| 1: Foundation | 1-3 | Solution, exceptions, options, constants, models |
| 2: Pipe Client | 4-5 | `IVddPipeClient` + `VddPipeClient` implementation |
| 3: Manager Core | 6-8 | `IVirtualDisplayManager` + constructor/disposal/ping/display count |
| 4: Parsing & Toggles | 9-10 | Response parsing, toggle commands, reload spacing |
| 5: DI & Integration | 11-13 | `AddVirtualDisplayDriver()`, integration tests, full verification |

Total: 13 tasks, ~60 unit tests, 6 integration tests.
