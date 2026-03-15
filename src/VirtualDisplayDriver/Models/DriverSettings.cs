namespace VirtualDisplayDriver;

public record DriverSettings(
    bool DebugLogging,
    bool Logging,
    bool HdrPlus = false,
    bool Sdr10Bit = false,
    bool CustomEdid = false,
    bool PreventSpoof = false,
    bool CeaOverride = false,
    bool HardwareCursor = false);
