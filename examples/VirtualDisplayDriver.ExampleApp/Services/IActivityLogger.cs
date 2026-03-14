using System.Collections.ObjectModel;
using VirtualDisplayDriver.ExampleApp.Models;
using LogLevel = VirtualDisplayDriver.ExampleApp.Models.LogLevel;

namespace VirtualDisplayDriver.ExampleApp.Services;

public interface IActivityLogger
{
    ObservableCollection<LogEntry> Entries { get; }

    void Log(LogLevel level, string category, string message, string? detail = null);
    void LogInfo(string category, string message, string? detail = null);
    void LogSuccess(string category, string message, string? detail = null);
    void LogWarning(string category, string message, string? detail = null);
    void LogError(string category, string message, string? detail = null);
}
