namespace VirtualDisplayDriver;

public class VirtualDisplayOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReloadSpacing { get; set; } = TimeSpan.FromSeconds(30);
    public int InitialDisplayCount { get; set; }
    public int MaxDisplayCount { get; set; } = 16;
}
