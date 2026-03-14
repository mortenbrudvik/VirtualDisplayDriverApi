using System.Globalization;
using System.Windows.Data;

namespace VirtualDisplayDriver.ExampleApp.Converters;

public class BoolToStatusConverter : IValueConverter
{
    public string TrueText { get; set; } = "Connected";
    public string FalseText { get; set; } = "Disconnected";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
