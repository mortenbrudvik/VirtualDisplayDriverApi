namespace VirtualDisplayDriver.ExampleApp.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Detail = null);
