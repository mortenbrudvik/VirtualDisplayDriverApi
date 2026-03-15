using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VirtualDisplayDriver.ExampleApp.Helpers;

internal static class DwmHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public static void EnableDarkTitleBar(Window window)
    {
        var hwndSource = PresentationSource.FromVisual(window) as HwndSource;
        if (hwndSource is null)
            return;

        var value = 1;
        DwmSetWindowAttribute(hwndSource.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
