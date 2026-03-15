namespace VirtualDisplayDriver;

/// <summary>
/// Represents a supported display resolution and refresh rate.
/// </summary>
public record DisplayMode(int Width, int Height, int RefreshRate)
{
    public override string ToString() => $"{Width} x {Height}";
}
