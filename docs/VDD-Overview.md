# Virtual Display Driver (VDD) - Complete Overview

> **Repository:** [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
> **License:** MIT
> **Stars:** ~8,600 | **Forks:** ~350 (as of March 2026)
> **Latest Release:** 25.7.23 (July 2025)
> **Status:** Actively maintained (last commit Feb 2026, verified March 2026)

---

## 1. What Is It?

Virtual Display Driver (VDD) is a **Windows Indirect Display Driver (IDD)** that creates virtual monitors without requiring physical hardware. It uses Microsoft's **IddCx 1.10** framework (the latest available) and runs as a **User-Mode Driver Framework (UMDF)** driver, which significantly reduces the risk of system crashes (BSODs) compared to kernel-mode alternatives.

The project originated as a fork of `roshkins/IddSampleDriver` in October 2023 and has since become the most feature-rich and actively maintained IDD implementation available.

---

## 2. Key Capabilities

| Feature | Details |
|---------|---------|
| **Virtual Monitors** | Create multiple virtual displays with custom resolutions and refresh rates |
| **Resolution Support** | 71 presets from 640x480 to 10240x4320, plus custom resolutions |
| **Refresh Rates** | Floating-point precision (e.g., 59.97 Hz, 23.976 Hz) |
| **HDR Support** | 10-bit and 12-bit color depth (requires Windows 11 23H2+) |
| **Color Formats** | RGB 8/10/12-bit, YCbCr 4:4:4, 4:2:2, 4:2:0 (various bit depths) |
| **Hardware Cursor** | 128x128 pixels with alpha blending and XOR support |
| **Custom EDID** | Emulate specific monitor profiles via EDID data |
| **Multi-GPU** | PCI-bus LUID selection for multi-adapter systems |
| **Platform Support** | x86, x64, ARM, ARM64 |
| **Code Signing** | Officially signed via SignPath.io (no test-signing needed on x64) |
| **Audio Driver** | Bundled virtual audio driver included |

---

## 3. Common Use Cases

1. **Game/App Streaming** - OBS Studio, Sunshine/Moonlight, Parsec, etc.
2. **Virtual Reality** - Additional virtual screens for VR workflows
3. **Screen Recording** - Dedicated capture display
4. **Remote Desktop** - Headless servers, cloud VMs (AWS, Azure)
5. **GPU Computing** - Run GPU workloads without a physical monitor
6. **Multi-Monitor Testing** - Simulate multi-display setups for development

---

## 4. Architecture & Technical Details

### 4.1 Driver Model

```
┌─────────────────────────────────────────────┐
│              Windows Display Stack           │
│  ┌─────────┐  ┌──────────┐  ┌────────────┐ │
│  │ Desktop  │  │ Display  │  │   IddCx    │ │
│  │ Window   │──│ Manager  │──│ Framework  │ │
│  │ Manager  │  │          │  │   v1.10    │ │
│  └─────────┘  └──────────┘  └─────┬──────┘ │
│                                    │        │
│  ┌─────────────────────────────────┴──────┐ │
│  │     MttVDD (Virtual Display Driver)    │ │
│  │  ┌──────────────┐ ┌─────────────────┐  │ │
│  │  │ Direct3D     │ │ SwapChain       │  │ │
│  │  │ Device Mgr   │ │ Processor       │  │ │
│  │  │ (D3D11.2)    │ │ (Frame Buffer)  │  │ │
│  │  └──────────────┘ └─────────────────┘  │ │
│  │  ┌──────────────┐ ┌─────────────────┐  │ │
│  │  │ EDID         │ │ Named Pipe      │  │ │
│  │  │ Profiles     │ │ Control IPC     │  │ │
│  │  └──────────────┘ └─────────────────┘  │ │
│  └────────────────────────────────────────┘ │
│                    UMDF 2.15 (User Mode)     │
└─────────────────────────────────────────────┘
```

- **IddCx 1.10** - Microsoft's Indirect Display Driver Class Extension (latest version)
- **UMDF 2.15/2.25** - User-Mode Driver Framework (2.25 for x64, 2.15 for ARM)
- **Direct3D 11.2 + DXGI 1.5** - GPU rendering pipeline
- **Named Pipes** - IPC mechanism for runtime control
- **XML Configuration** - Settings file at `C:\VirtualDisplayDriver\vdd_settings.xml`

### 4.2 Core Classes

| Class | Purpose |
|-------|---------|
| `Direct3DDevice` | Manages the D3D11 device, adapter enumeration, and GPU selection |
| `SwapChainProcessor` | Threaded buffer consumer that processes frames from the IddCx swap chain |

### 4.3 IddCx Callbacks Implemented

- Adapter initialization and finalization
- Monitor arrival/departure and EDID parsing
- Swap chain assignment/unassignment
- Mode commit (resolution + refresh rate activation)
- HDR metadata configuration
- Gamma ramp control
- Target mode queries (resolution/refresh enumeration)
- Hardware cursor support

### 4.4 Named Pipe IPC

The driver creates a named pipe (`MTTVirtualDisplayPipe`) for runtime control using explicit `SECURITY_ATTRIBUTES` with SDDL string `D:(A;;GA;;;WD)` — `GA` (Generic All) granted to `WD` (World/Everyone), allowing full control from any local process.

The pipe supports 17 commands:

| Command | Purpose |
|---------|---------|
| `CYCLEDISPLAY` | Add or remove a virtual monitor |
| `SETRESOLUTION` | Set resolution and refresh rate |
| `HDR` | Toggle HDR mode |
| `REFRESH` | Reload settings from XML |
| `LOG_DEBUG` | Control debug log output |
| `LOGGING` | Configure logging settings |
| `HDRPLUS` | HDR+ mode control |
| `SDR10` | SDR 10-bit mode control |
| `CUSTOMEDID` | Apply custom EDID data |
| `PREVENTSPOOF` | Control monitor spoofing prevention |
| `CEAOVERRIDE` | Override CEA display modes |
| `HARDWARECURSOR` | Toggle hardware cursor support |
| `D3DDEVICEGPU` | Set D3D device GPU selection |
| `IDDCXVERSION` | Query IddCx framework version |
| `GETASSIGNEDGPU` | Query the currently assigned GPU |
| `GETALLGPUS` | Enumerate all available GPUs |
| `SETGPU` | Set GPU for rendering |

---

## 5. Repository Structure

```
Virtual-Display-Driver/
├── Virtual Display Driver (HDR)/
│   └── MttVDD/                          # Core driver project
│       ├── Driver.cpp / Driver.h        # Main driver implementation
│       ├── MttVDD.inf                   # Windows driver installation manifest
│       ├── MttVDD.vcxproj               # Visual Studio project (multi-platform)
│       ├── Edid/                         # Custom EDID monitor profiles
│       └── ThirdParty/                   # UMDF 2.15 headers
│
├── Virtual-Audio-Driver (Latest Stable)/ # Bundled virtual audio driver
│
├── Community Scripts/                    # PowerShell/Batch automation
│   ├── Install-VDD.ps1                  # Automated installation
│   ├── Change-Resolution.ps1            # Resolution switching
│   ├── Toggle-HDR.ps1                   # HDR enable/disable
│   └── ... (9+ scripts total)
│
├── GetIddCx/                            # Utility to query IddCx version
│
├── vdd_settings.xml                     # XML configuration file
├── option.txt                           # 71 resolution presets
└── README.md
```

### 5.1 Language Breakdown

| Language | Size |
|----------|------|
| C++ | 548 KB (primary) |
| C | 103 KB |
| PowerShell | Scripts |
| Batch | Scripts |

### 5.2 Build System

- **IDE:** Visual Studio (`.sln` / `.vcxproj`)
- **Targets:** Debug/Release for Win32, x64, ARM, ARM64
- **UMDF Version:** 2.25 (x64), 2.15 (ARM/ARM64)
- **IddCx Version:** 1.10 (x64 only, lower for other platforms)
- **Security:** Spectre mitigation enabled (x64)
- **Dependencies:** VC++ Redistributable runtime

---

## 6. Configuration (`vdd_settings.xml`)

The driver is configured via an XML file at `C:\VirtualDisplayDriver\vdd_settings.xml`:

- **Resolutions & Refresh Rates** - Custom lists with floating-point precision
- **Color Format** - RGB/YCbCr variants, bit depth selection
- **HDR Parameters** - Enable/disable, bit depth (10/12)
- **EDID Profiles** - Custom monitor identification data
- **GPU Selection** - PCI-bus LUID for multi-GPU systems
- **Logging** - Debug logging configuration
- **Cursor Settings** - Hardware cursor parameters
- **`auto_resolutions`** - EDID-based automatic resolution generation with filtering (min/max refresh rate, min/max resolution, fractional rate exclusion)
- **`color_advanced`** - Bit depth management (`auto_select_from_color_space`, `force_bit_depth`, `fp16_surface_support`, `sdr_white_level`)
- **`monitor_emulation`** - Physical dimension emulation and manufacturer spoofing (loaded but currently unused in driver)
- **`edid_integration`** - Auto-configure from EDID profiles (`enabled`, `auto_configure_from_edid`, `edid_profile_path`, `override_manual_settings`, `fallback_on_error`)

The `option.txt` file provides 71 resolution presets ranging from 640x480 to 10240x4320.

> **Note:** The upstream `option.txt` contains likely typos in three entries:
> - `800, 5000, 500` — height 5000 likely should be 600
> - `1280, 5000, 500` — height 5000 likely should be 1024
> - `10240, 432, 500` — height 432 likely should be 4320

---

## 7. Companion Tools

| Tool | Description |
|------|-------------|
| **Virtual Driver Control (VDC)** | GUI application for installing, managing, and configuring virtual displays |
| **Community PowerShell Scripts** | 9+ automation scripts for install, resolution changes, HDR toggle, etc. |
| **GetIddCx** | CLI utility to query the installed IddCx framework version |

---

## 8. Installation & Requirements

### Requirements

- Windows 10 or Windows 11 (x64/x86/ARM/ARM64)
- VC++ Redistributable
- For HDR: Windows 11 23H2 or later
- For ARM64: Test signing must be enabled (Win 11 24H2+)

### Installation

1. Download the latest release or use the VDC GUI installer
2. Driver installs to `C:\VirtualDisplayDriver\`
3. Device appears as `Root\MttVDD` in Device Manager
4. Configure via `vdd_settings.xml`

### Uninstallation

- **Important:** Uninstall VDD before major GPU driver updates to avoid conflicts

---

## 9. Known Limitations & Issues

### Active Issues

| Issue | Severity |
|-------|----------|
| Black screen on Windows 25H2 | High |
| GPU/chipset driver update conflicts | High |
| Cannot set as primary display (Win 11 24H2/25H2) | Medium |
| Multiple virtual monitors cause stutter | Medium |
| Installation failures (recurring) | Medium |

### Technical Limitations

- ARM64 requires test signing (not production-signed for ARM)
- Audio driver has signature issues on Windows Server 2025
- Mouse cursor positioning problems reported
- Game bar overlays don't appear on virtual displays
- GPU metrics broken with NVIDIA/AMD monitoring apps
- `XorCursorSupportLevel` setting is loaded but unused (code bug)
- EDID integration disabled by default

---

## 10. Community & Development

- **Core Team:** 2 primary contributors (itsmikethetech with 242 commits, bud3699 with 82 commits)
- **Open Issues:** ~180
- **Development Pace:** Moderate, with regular updates
- **Most Requested Feature:** C/C++ API for programmatic control
- **Other Requests:** Minimize to tray, custom monitor IDs, auto start/stop, resolution changes without restart

---

## 11. Competitive Position

VDD is the **most advanced open-source IDD implementation** available:

| Feature | VDD | Other IDD Projects |
|---------|-----|-------------------|
| IddCx Version | 1.10 (latest) | 1.2 - 1.5 |
| HDR | Yes | Rare |
| ARM64 | Yes | No |
| Custom EDID | Yes | No |
| Float Refresh Rates | Yes | No |
| Code Signed | Yes | Rarely |
| Active Development | Yes | Mostly abandoned |

---

## 12. Key Takeaways for Research

1. **IddCx 1.10 is the target framework** - latest Microsoft API for virtual displays
2. **UMDF (user-mode) is the safe approach** - no kernel-mode BSOD risk
3. **C++ with WRL COM is the implementation pattern** - standard Windows driver approach
4. **Named pipes for IPC** - 17 commands for runtime control
5. **XML for configuration** - persistent settings
6. **No programmatic API exists yet** - most requested community feature
7. **D3D11 + DXGI for rendering** - standard GPU pipeline
8. **The driver is essentially a frame buffer consumer** - SwapChainProcessor grabs frames from IddCx
