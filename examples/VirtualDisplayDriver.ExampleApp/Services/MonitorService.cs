using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VirtualDisplayDriver.ExampleApp.Services;

public partial class MonitorService : ObservableObject, IMonitorService
{
    private readonly ObservableCollection<SystemMonitor> _monitors = [];

    public MonitorService()
    {
        Monitors = new ReadOnlyObservableCollection<SystemMonitor>(_monitors);
    }

    public ReadOnlyObservableCollection<SystemMonitor> Monitors { get; }

    [ObservableProperty]
    private SystemMonitor? _selectedMonitor;

    public void RefreshTopology()
    {
        var allMonitors = VirtualDisplayConfiguration.GetAllMonitors();

        var previousDeviceName = SelectedMonitor?.DeviceName;

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            UpdateCollection(allMonitors, previousDeviceName);
        else
            Application.Current?.Dispatcher.Invoke(() => UpdateCollection(allMonitors, previousDeviceName));
    }

    private void UpdateCollection(IReadOnlyList<SystemMonitor> monitors, string? previousDeviceName)
    {
        _monitors.Clear();
        foreach (var monitor in monitors)
            _monitors.Add(monitor);

        SelectedMonitor = previousDeviceName is not null
            ? monitors.FirstOrDefault(m => m.DeviceName == previousDeviceName)
            : null;
    }
}
