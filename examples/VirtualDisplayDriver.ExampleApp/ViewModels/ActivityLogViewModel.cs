using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualDisplayDriver.ExampleApp.Models;
using VirtualDisplayDriver.ExampleApp.Services;

namespace VirtualDisplayDriver.ExampleApp.ViewModels;

public partial class ActivityLogViewModel : ObservableObject
{
    private readonly IActivityLogger _logger;

    [ObservableProperty]
    private bool _autoScroll = true;

    public ObservableCollection<LogEntry> Entries => _logger.Entries;

    public ActivityLogViewModel(IActivityLogger logger)
    {
        _logger = logger;
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logger.Entries.Clear();
    }

    [RelayCommand]
    private void CopyLog()
    {
        if (Entries.Count == 0) return;

        var text = string.Join(Environment.NewLine,
            Entries.Select(e => $"[{e.Timestamp:HH:mm:ss.fff}] [{e.Level}] [{e.Category}] {e.Message}" +
                                (e.Detail is not null ? $"\n  {e.Detail}" : "")));
        Clipboard.SetText(text);
    }
}
