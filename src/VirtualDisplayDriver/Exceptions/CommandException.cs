namespace VirtualDisplayDriver;

public class CommandException : VddException
{
    public string? RawResponse { get; }

    public CommandException(string message) : base(message) { }
    public CommandException(string message, string rawResponse) : base(message)
    {
        RawResponse = rawResponse;
    }
    public CommandException(string message, Exception innerException) : base(message, innerException) { }
}
