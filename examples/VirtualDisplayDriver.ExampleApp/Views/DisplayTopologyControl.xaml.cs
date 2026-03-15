using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using VirtualDisplayDriver.ExampleApp.ViewModels;

namespace VirtualDisplayDriver.ExampleApp.Views;

public partial class DisplayTopologyControl : UserControl
{
    private static readonly DropShadowEffect GlowEffect = new()
    {
        Color = Color.FromRgb(0x89, 0xB4, 0xFA),
        ShadowDepth = 0,
        BlurRadius = 15,
        Opacity = 0.6
    };

    private bool _layoutPending;

    public DisplayTopologyControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        // Only re-layout when the map border itself resizes, not the whole control
        MapBorder.SizeChanged += (_, _) => ScheduleLayout();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (MonitorList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            LayoutMonitors();
        else
            MonitorList.ItemContainerGenerator.StatusChanged += OnContainersGenerated;

        if (MonitorList.ItemsSource is INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnMonitorsChanged;
    }

    private void OnMonitorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
        ScheduleLayout();
    }

    private void OnContainersGenerated(object? sender, EventArgs e)
    {
        if (MonitorList.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;

        MonitorList.ItemContainerGenerator.StatusChanged -= OnContainersGenerated;
        UpdateEmptyState();
        ScheduleLayout();
    }

    private void ScheduleLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _layoutPending = false;
            LayoutMonitors();
        });
    }

    private void UpdateEmptyState()
    {
        var hasMonitors = MonitorList.Items.Count > 0;
        EmptyText.Visibility = hasMonitors ? Visibility.Collapsed : Visibility.Visible;
        MonitorList.Visibility = hasMonitors ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LayoutMonitors()
    {
        var vm = DataContext as DisplayManagementViewModel;
        if (vm is null) return;

        var monitors = vm.Monitors;
        if (monitors.Count == 0) return;

        var canvas = FindCanvas(MonitorList);
        if (canvas is null) return;

        // Use the map border's inner size (minus padding) for available space
        var padding = MapBorder.Padding;
        var availableWidth = MapBorder.ActualWidth - padding.Left - padding.Right;
        var availableHeight = MapBorder.ActualHeight - padding.Top - padding.Bottom;

        // Reserve space for the legend at the bottom
        availableHeight -= 24;

        if (availableWidth <= 0 || availableHeight <= 0) return;

        // Compute the bounding rect of all monitors
        var minX = monitors.Min(m => m.X);
        var minY = monitors.Min(m => m.Y);
        var maxX = monitors.Max(m => m.X + m.Width);
        var maxY = monitors.Max(m => m.Y + m.Height);
        var totalWidth = (double)(maxX - minX);
        var totalHeight = (double)(maxY - minY);

        if (totalWidth <= 0 || totalHeight <= 0) return;

        // Scale to fill the available space
        var scale = Math.Min(availableWidth / totalWidth, availableHeight / totalHeight);

        // Center offset
        var scaledWidth = totalWidth * scale;
        var scaledHeight = totalHeight * scale;
        var offsetX = (availableWidth - scaledWidth) / 2;
        var offsetY = (availableHeight - scaledHeight) / 2;

        for (var i = 0; i < monitors.Count; i++)
        {
            var container = MonitorList.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container is null) continue;

            var m = monitors[i];
            Canvas.SetLeft(container, (m.X - minX) * scale + offsetX);
            Canvas.SetTop(container, (m.Y - minY) * scale + offsetY);
            container.Width = m.Width * scale;
            container.Height = m.Height * scale;
        }

        canvas.Width = availableWidth;
        canvas.Height = availableHeight;
    }

    private static Canvas? FindCanvas(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Canvas canvas) return canvas;
            var found = FindCanvas(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void Monitor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SystemMonitor monitor }) return;
        if (DataContext is not DisplayManagementViewModel vm) return;

        vm.SelectedMonitor = monitor;
        UpdateDetailPanel(monitor);
        UpdateSelectionGlow(monitor);
    }

    private void UpdateDetailPanel(SystemMonitor monitor)
    {
        DetailPanel.Visibility = Visibility.Visible;
        Placeholder.Visibility = Visibility.Hidden;

        DisplayName.Text = $"Display {monitor.DisplayNumber}";
        ResolutionValue.Text = $"{monitor.Width} \u00d7 {monitor.Height}";
        RefreshRateValue.Text = $"{monitor.RefreshRate} Hz";
        PositionValue.Text = $"{monitor.X}, {monitor.Y}";
        DeviceValue.Text = monitor.DeviceName;

        if (monitor.IsVirtual)
        {
            TypeBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x89, 0xB4, 0xFA));
            TypeText.Foreground = FindResource("AccentPrimary") as Brush;
            TypeText.Text = "Virtual";

            // Populate resolution picker — unique resolutions only (no Hz duplicates)
            var modes = VirtualDisplayConfiguration.GetSupportedModes(monitor.DeviceName)
                .GroupBy(m => (m.Width, m.Height))
                .Select(g => g.First())
                .ToList();
            ModeComboBox.ItemsSource = modes;
            ModeComboBox.SelectedItem = modes.FirstOrDefault(m => m.Width == monitor.Width && m.Height == monitor.Height);
            ResolutionPicker.Visibility = Visibility.Visible;
        }
        else
        {
            TypeBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x6C, 0x70, 0x86));
            TypeText.Foreground = FindResource("TextSecondary") as Brush;
            TypeText.Text = "Physical";
            ResolutionPicker.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyResolution_Click(object sender, RoutedEventArgs e)
    {
        if (ModeComboBox.SelectedItem is not DisplayMode mode) return;
        if (DataContext is not DisplayManagementViewModel vm) return;
        if (vm.SelectedMonitor is not { } monitor) return;

        try
        {
            VirtualDisplayConfiguration.SetResolutionByDeviceName(
                monitor.DeviceName, mode.Width, mode.Height);
            vm.ErrorMessage = null;
        }
        catch (VddException ex)
        {
            vm.ErrorMessage = ex.Message;
        }
    }

    private void UpdateSelectionGlow(SystemMonitor selected)
    {
        for (var i = 0; i < MonitorList.Items.Count; i++)
        {
            var container = MonitorList.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container is null) continue;

            var border = FindChild<System.Windows.Controls.Border>(container);
            if (border is null) continue;

            var monitor = MonitorList.Items[i] as SystemMonitor;
            border.Effect = monitor?.DeviceName == selected.DeviceName ? GlowEffect : null;
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var found = FindChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}
