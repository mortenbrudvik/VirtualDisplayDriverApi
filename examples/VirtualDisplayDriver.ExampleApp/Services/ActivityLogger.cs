using System.Collections.ObjectModel;
using System.Windows;
using VirtualDisplayDriver.ExampleApp.Models;
using LogLevel = VirtualDisplayDriver.ExampleApp.Models.LogLevel;

namespace VirtualDisplayDriver.ExampleApp.Services;

public class ActivityLogger : IActivityLogger
{
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Log(LogLevel level, string category, string message, string? detail = null)
    {
        var entry = new LogEntry(DateTime.Now, level, category, message, detail);

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Entries.Add(entry);
        else
            Application.Current?.Dispatcher.Invoke(() => Entries.Add(entry));
    }

    public void LogInfo(string category, string message, string? detail = null)
        => Log(LogLevel.Info, category, message, detail);

    public void LogSuccess(string category, string message, string? detail = null)
        => Log(LogLevel.Success, category, message, detail);

    public void LogWarning(string category, string message, string? detail = null)
        => Log(LogLevel.Warning, category, message, detail);

    public void LogError(string category, string message, string? detail = null)
        => Log(LogLevel.Error, category, message, detail);
}
