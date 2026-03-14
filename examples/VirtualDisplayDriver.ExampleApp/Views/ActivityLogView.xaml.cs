using System.Collections.Specialized;
using System.Windows.Controls;
using VirtualDisplayDriver.ExampleApp.ViewModels;

namespace VirtualDisplayDriver.ExampleApp.Views;

public partial class ActivityLogView : UserControl
{
    public ActivityLogView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ActivityLogViewModel vm)
            vm.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ActivityLogViewModel vm)
            vm.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is ActivityLogViewModel { AutoScroll: true } &&
            LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }
    }
}
