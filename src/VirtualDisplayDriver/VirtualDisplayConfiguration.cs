using System.Runtime.InteropServices;

namespace VirtualDisplayDriver;

public static class VirtualDisplayConfiguration
{
    private const string VddMonitorDeviceId = "MTT1337";

    /// <summary>
    /// Returns all active display monitors on the system with position, resolution, and type information.
    /// </summary>
    public static IReadOnlyList<SystemMonitor> GetAllMonitors()
    {
        var result = new List<SystemMonitor>();
        var adapter = NewDisplayDevice();

        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) == 0)
            {
                adapter.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
                continue;
            }

            var devMode = NewDevMode();
            if (!NativeMethods.EnumDisplaySettings(adapter.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref devMode))
            {
                adapter.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
                continue;
            }

            var monitor = NewDisplayDevice();
            var isVirtual = NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0)
                            && monitor.DeviceID.Contains(VddMonitorDeviceId, StringComparison.OrdinalIgnoreCase);

            var isPrimary = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

            var displayNumber = 0;
            var name = adapter.DeviceName; // e.g. "\\.\DISPLAY3"
            if (name.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase))
                int.TryParse(name.AsSpan(@"\\.\DISPLAY".Length), out displayNumber);

            result.Add(new SystemMonitor(
                displayNumber,
                adapter.DeviceName,
                adapter.DeviceString ?? "",
                devMode.dmPositionX,
                devMode.dmPositionY,
                (int)devMode.dmPelsWidth,
                (int)devMode.dmPelsHeight,
                (int)devMode.dmDisplayFrequency,
                isVirtual,
                isPrimary));

            adapter.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
        }

        return result;
    }

    /// <summary>
    /// Returns all supported display modes for the given device, sorted by resolution (descending) then refresh rate (descending).
    /// </summary>
    public static IReadOnlyList<DisplayMode> GetSupportedModes(string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        var modes = new HashSet<(int W, int H, int Hz)>();
        var devMode = NewDevMode();

        for (var i = 0; NativeMethods.EnumDisplaySettings(deviceName, i, ref devMode); i++)
        {
            if (devMode.dmPelsWidth > 0 && devMode.dmPelsHeight > 0)
                modes.Add(((int)devMode.dmPelsWidth, (int)devMode.dmPelsHeight, (int)devMode.dmDisplayFrequency));

            devMode = NewDevMode();
        }

        return modes
            .OrderByDescending(m => m.W)
            .ThenByDescending(m => m.H)
            .ThenByDescending(m => m.Hz)
            .Select(m => new DisplayMode(m.W, m.H, m.Hz))
            .ToList();
    }

    /// <summary>
    /// Returns all active virtual display monitors with their current resolution.
    /// </summary>
    public static IReadOnlyList<VirtualMonitor> GetVirtualMonitors()
    {
        var result = new List<VirtualMonitor>();
        var adapter = NewDisplayDevice();

        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) == 0)
            {
                adapter.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
                continue;
            }

            var monitor = NewDisplayDevice();
            if (NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0)
                && monitor.DeviceID.Contains(VddMonitorDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                var devMode = NewDevMode();
                NativeMethods.EnumDisplaySettings(adapter.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref devMode);

                result.Add(new VirtualMonitor(
                    result.Count,
                    adapter.DeviceName,
                    (int)devMode.dmPelsWidth,
                    (int)devMode.dmPelsHeight,
                    (int)devMode.dmDisplayFrequency));
            }

            adapter.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
        }

        return result;
    }

    /// <summary>
    /// Sets the resolution of a virtual display by its index (from <see cref="GetVirtualMonitors"/>).
    /// </summary>
    public static void SetResolution(int monitorIndex, int width, int height, int refreshRate = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        var monitors = GetVirtualMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index {monitorIndex} is out of range. Found {monitors.Count} virtual monitor(s).");

        var deviceName = monitors[monitorIndex].DeviceName;
        SetResolutionByDeviceName(deviceName, width, height, refreshRate);
    }

    /// <summary>
    /// Sets the resolution of a virtual display by its Windows device name (e.g. \\.\DISPLAY3).
    /// </summary>
    public static void SetResolutionByDeviceName(string deviceName, int width, int height, int refreshRate = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var devMode = NewDevMode();
        devMode.dmPelsWidth = (uint)width;
        devMode.dmPelsHeight = (uint)height;
        devMode.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;

        if (refreshRate > 0)
        {
            devMode.dmDisplayFrequency = (uint)refreshRate;
            devMode.dmFields |= NativeMethods.DM_DISPLAYFREQUENCY;
        }

        var result = NativeMethods.ChangeDisplaySettingsEx(
            deviceName, ref devMode, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero);

        if (result != NativeMethods.DISP_CHANGE_SUCCESSFUL)
        {
            var message = result switch
            {
                NativeMethods.DISP_CHANGE_BADMODE => $"The resolution {width}x{height}" +
                    (refreshRate > 0 ? $"@{refreshRate}Hz" : "") +
                    " is not supported by this virtual display.",
                NativeMethods.DISP_CHANGE_FAILED => "The display driver failed to apply the resolution change.",
                NativeMethods.DISP_CHANGE_RESTART => "A system restart is required to apply this resolution change.",
                _ => $"ChangeDisplaySettingsEx failed with code {result}."
            };
            throw new VddException(message);
        }
    }

    private static NativeMethods.DISPLAY_DEVICE NewDisplayDevice()
    {
        var d = new NativeMethods.DISPLAY_DEVICE();
        d.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
        return d;
    }

    private static NativeMethods.DEVMODE NewDevMode()
    {
        var dm = new NativeMethods.DEVMODE();
        dm.dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>();
        return dm;
    }

    internal static class NativeMethods
    {
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const uint CDS_UPDATEREGISTRY = 0x00000001;
        public const uint DM_PELSWIDTH = 0x00080000;
        public const uint DM_PELSHEIGHT = 0x00100000;
        public const uint DM_DISPLAYFREQUENCY = 0x00400000;
        public const int DISPLAY_DEVICE_ACTIVE = 0x00000001;
        public const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
        public const int DISP_CHANGE_SUCCESSFUL = 0;
        public const int DISP_CHANGE_RESTART = 1;
        public const int DISP_CHANGE_FAILED = -1;
        public const int DISP_CHANGE_BADMODE = -2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool EnumDisplayDevices(
            string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(
            string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }
    }
}
