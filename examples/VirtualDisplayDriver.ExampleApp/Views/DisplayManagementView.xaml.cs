using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace VirtualDisplayDriver.ExampleApp.Views;

public partial class DisplayManagementView : UserControl
{
    public DisplayManagementView()
    {
        InitializeComponent();
    }

    private void OpenWindowsDisplaySettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
    }
}
