using Microsoft.Win32;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver;

public static class VirtualDisplayDetection
{
    private const string DefaultInstallPath = @"C:\VirtualDisplayDriver";
    private const string RegistryKey = @"SOFTWARE\MikeTheTech\VirtualDisplayDriver";
    private const string RegistryValueName = "VDDPATH";

    public static bool IsPipeRunning()
        => File.Exists($@"\\.\pipe\{PipeConstants.PipeName}");

    public static bool IsDriverInstalled()
        => GetInstallPath() is not null;

    public static string? GetInstallPath()
    {
        var registryPath = GetRegistryInstallPath();
        if (registryPath is not null && Directory.Exists(registryPath))
            return registryPath;

        if (Directory.Exists(DefaultInstallPath))
            return DefaultInstallPath;

        return null;
    }

    private static string? GetRegistryInstallPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegistryKey);
        return key?.GetValue(RegistryValueName) as string;
    }
}
