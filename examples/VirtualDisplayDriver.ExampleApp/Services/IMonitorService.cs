using System.Collections.ObjectModel;

namespace VirtualDisplayDriver.ExampleApp.Services;

public interface IMonitorService
{
    ReadOnlyObservableCollection<SystemMonitor> Monitors { get; }
    SystemMonitor? SelectedMonitor { get; set; }
    void RefreshTopology();
}
