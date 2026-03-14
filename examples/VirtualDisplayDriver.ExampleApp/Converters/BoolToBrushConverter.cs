using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VirtualDisplayDriver.ExampleApp.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
