namespace VirtualDisplayDriver;

public record DeviceInfo(string InstanceId, string Description, bool IsEnabled);

public enum DeviceState
{
    NotFound,
    Enabled,
    Disabled
}
