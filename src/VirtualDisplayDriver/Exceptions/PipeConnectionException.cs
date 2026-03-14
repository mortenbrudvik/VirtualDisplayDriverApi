namespace VirtualDisplayDriver;

public class PipeConnectionException : VddException
{
    public PipeConnectionException(string message) : base(message) { }
    public PipeConnectionException(string message, Exception innerException) : base(message, innerException) { }
}
