using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VirtualDisplayDriver.ExampleApp.Helpers;

internal static class IconHelper
{
    public static BitmapSource CreateMonitorIcon()
    {
        const int size = 32;
        const double dpi = 96;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var text = new FormattedText(
                "\uE7F4",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                24,
                Brushes.White,
                dpi);

            var x = (size - text.Width) / 2;
            var y = (size - text.Height) / 2;
            dc.DrawText(text, new Point(x, y));
        }

        var bitmap = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
