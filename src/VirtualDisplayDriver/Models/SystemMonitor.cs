namespace VirtualDisplayDriver;

/// <summary>
/// Represents any active display on the system (physical or virtual) with position and resolution data.
/// </summary>
public record SystemMonitor(
    int DisplayNumber,
    string DeviceName,
    string AdapterName,
    int X,
    int Y,
    int Width,
    int Height,
    int RefreshRate,
    bool IsVirtual,
    bool IsPrimary);
