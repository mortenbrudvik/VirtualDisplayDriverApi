using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VirtualDisplayDriver.ExampleApp.Models;
using LogLevel = VirtualDisplayDriver.ExampleApp.Models.LogLevel;

namespace VirtualDisplayDriver.ExampleApp.Converters;

public class LogLevelToBrushConverter : IValueConverter
{
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LogLevel level ? level switch
        {
            LogLevel.Info => InfoBrush,
            LogLevel.Success => SuccessBrush,
            LogLevel.Warning => WarningBrush,
            LogLevel.Error => ErrorBrush,
            _ => InfoBrush
        } : InfoBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
