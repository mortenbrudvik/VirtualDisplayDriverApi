using System.Security;
using System.Xml;
using Microsoft.Win32;
using VirtualDisplayDriver.Pipe;

namespace VirtualDisplayDriver;

public static class VirtualDisplayDetection
{
    private const string DefaultInstallPath = @"C:\VirtualDisplayDriver";
    private const string RegistryKey = @"SOFTWARE\MikeTheTech\VirtualDisplayDriver";
    private const string RegistryValueName = "VDDPATH";
    private const string SettingsFileName = "vdd_settings.xml";

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

    /// <summary>
    /// Returns the full path to vdd_settings.xml, searching the install directory
    /// and its subdirectories. Returns null if not found.
    /// </summary>
    public static string? GetSettingsFilePath()
    {
        var installPath = GetInstallPath();
        if (installPath is null) return null;

        // Check install root first, then search subdirectories
        // (the ZIP extraction may create a nested VirtualDisplayDriver folder)
        var directPath = Path.Combine(installPath, SettingsFileName);
        if (File.Exists(directPath)) return directPath;

        try
        {
            return Directory.EnumerateFiles(installPath, SettingsFileName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the configured display count from vdd_settings.xml.
    /// Returns 0 if the settings file is not found or cannot be parsed.
    /// </summary>
    public static int GetConfiguredDisplayCount()
    {
        var settingsPath = GetSettingsFilePath();
        if (settingsPath is null) return 0;

        try
        {
            var doc = new XmlDocument();
            doc.Load(settingsPath);

            var countNode = doc.SelectSingleNode("//count");
            if (countNode is not null && int.TryParse(countNode.InnerText.Trim(), out var count) && count >= 0)
                return count;

            return 0;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Writes the display count to vdd_settings.xml.
    /// This matches how the official VDD Control app works — it writes XML first,
    /// then sends a pipe command to trigger a driver reload.
    /// </summary>
    public static bool SetConfiguredDisplayCount(int count)
    {
        var settingsPath = GetSettingsFilePath();
        if (settingsPath is null) return false;

        try
        {
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(settingsPath);

            var countNode = doc.SelectSingleNode("//count");
            if (countNode is not null)
            {
                countNode.InnerText = count.ToString();
                doc.Save(settingsPath);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads all driver settings from vdd_settings.xml.
    /// Returns default (all false) if the file is not found or cannot be parsed.
    /// </summary>
    public static DriverSettings GetSettingsFromXml()
    {
        var settingsPath = GetSettingsFilePath();
        if (settingsPath is null) return new DriverSettings(false, false);

        try
        {
            var doc = new XmlDocument();
            doc.Load(settingsPath);

            return new DriverSettings(
                DebugLogging: ReadBool(doc, "//debuglogging"),
                Logging: ReadBool(doc, "//logging"),
                HdrPlus: ReadBool(doc, "//HDRPlus"),
                Sdr10Bit: ReadBool(doc, "//SDR10bit"),
                CustomEdid: ReadBool(doc, "//CustomEdid"),
                PreventSpoof: ReadBool(doc, "//PreventSpoof"),
                CeaOverride: ReadBool(doc, "//EdidCeaOverride"),
                HardwareCursor: ReadBool(doc, "//HardwareCursor"));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return new DriverSettings(false, false);
        }
    }

    private static bool ReadBool(XmlDocument doc, string xpath)
    {
        var node = doc.SelectSingleNode(xpath);
        return node is not null
               && bool.TryParse(node.InnerText.Trim(), out var value)
               && value;
    }

    private static string? GetRegistryInstallPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKey);
            return key?.GetValue(RegistryValueName) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
