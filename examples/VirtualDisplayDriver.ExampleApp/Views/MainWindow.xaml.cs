using System.Windows;
using System.Windows.Interop;
using VirtualDisplayDriver.ExampleApp.Helpers;
using VirtualDisplayDriver.ExampleApp.Services;
using VirtualDisplayDriver.ExampleApp.ViewModels;

namespace VirtualDisplayDriver.ExampleApp.Views;

public partial class MainWindow : Window
{
    private readonly IMonitorService _monitorService;
    private const int WM_DISPLAYCHANGE = 0x007E;

    public MainWindow(MainViewModel viewModel, IMonitorService monitorService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _monitorService = monitorService;
        Icon = IconHelper.CreateMonitorIcon();

        Loaded += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmHelper.EnableDarkTitleBar(this);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
            Dispatcher.BeginInvoke(() => _monitorService.RefreshTopology());

        return IntPtr.Zero;
    }
}
