namespace VirtualDisplayDriver;

public class VddException : Exception
{
    public VddException(string message) : base(message) { }
    public VddException(string message, Exception innerException) : base(message, innerException) { }
}
