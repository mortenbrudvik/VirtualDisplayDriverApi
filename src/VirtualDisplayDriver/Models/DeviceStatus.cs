namespace VirtualDisplayDriver;

public record DeviceInfo(string InstanceId, string Description, bool IsEnabled, bool HasError = false, int? ProblemCode = null);

public enum DeviceState
{
    NotFound,
    Enabled,
    Disabled,
    Error
}
