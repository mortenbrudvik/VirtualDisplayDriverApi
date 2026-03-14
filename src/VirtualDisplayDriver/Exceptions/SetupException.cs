namespace VirtualDisplayDriver;

public class SetupException : VddException
{
    public int? ExitCode { get; }

    public SetupException(string message) : base(message) { }
    public SetupException(string message, int exitCode) : base(message) { ExitCode = exitCode; }
    public SetupException(string message, Exception innerException) : base(message, innerException) { }
}
