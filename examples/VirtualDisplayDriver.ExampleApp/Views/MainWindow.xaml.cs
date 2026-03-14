using System.Windows;
using VirtualDisplayDriver.ExampleApp.ViewModels;

namespace VirtualDisplayDriver.ExampleApp.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
