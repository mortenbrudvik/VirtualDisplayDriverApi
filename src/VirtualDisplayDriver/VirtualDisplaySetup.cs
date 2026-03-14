using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VirtualDisplayDriver.Setup;

namespace VirtualDisplayDriver;

public class VirtualDisplaySetup : IVirtualDisplaySetup
{
    private const string GitHubApiUrl = "https://api.github.com/repos/VirtualDrivers/Virtual-Display-Driver/releases/latest";
    private const string InfFileName = "MttVDD.inf";

    private readonly HttpClient _httpClient;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<VirtualDisplaySetup>? _logger;

    internal VirtualDisplaySetup(HttpClient httpClient, IProcessRunner processRunner, ILogger<VirtualDisplaySetup>? logger = null)
    {
        _httpClient = httpClient;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task InstallDriverAsync(
        string installPath = @"C:\VirtualDisplayDriver",
        IProgress<SetupProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Fetch latest release info
        progress?.Report(new SetupProgress(SetupStage.FetchingRelease, 0.0, "Fetching latest release info..."));
        _logger?.LogInformation("Fetching latest release from GitHub...");

        var release = await FetchLatestReleaseAsync(ct);
        var assetName = GetAssetName();
        var asset = Array.Find(release.Assets, a => a.Name == assetName)
            ?? throw new SetupException($"Asset '{assetName}' not found in release {release.TagName}. " +
                $"Available: {string.Join(", ", release.Assets.Select(a => a.Name))}");

        _logger?.LogInformation("Found release {Tag}, downloading {Asset} ({Size} KB)",
            release.TagName, asset.Name, asset.Size / 1024);

        // Download ZIP
        progress?.Report(new SetupProgress(SetupStage.Downloading, 0.05, $"Downloading {asset.Name}..."));
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"vdd-install-{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadFileAsync(asset.BrowserDownloadUrl, tempZipPath, asset.Size, progress, ct);

            // Extract
            progress?.Report(new SetupProgress(SetupStage.Extracting, 0.85, $"Extracting to {installPath}..."));
            _logger?.LogInformation("Extracting to {Path}", installPath);

            Directory.CreateDirectory(installPath);
            ZipFile.ExtractToDirectory(tempZipPath, installPath, overwriteFiles: true);

            // Find INF file
            var infPath = Directory.EnumerateFiles(installPath, InfFileName, SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new SetupException($"{InfFileName} not found in {installPath}. Extraction may have failed.");

            // Install driver via pnputil (elevated).
            // Use cmd /c to capture pnputil output to a temp file, since elevated
            // processes launched with UseShellExecute cannot have stdout redirected.
            progress?.Report(new SetupProgress(SetupStage.InstallingDriver, 0.90, "Installing driver (admin required)..."));
            _logger?.LogInformation("Installing driver from {InfPath}", infPath);

            var pnpOutputFile = Path.Combine(Path.GetTempPath(), $"vdd-pnputil-{Guid.NewGuid():N}.log");
            try
            {
                var exitCode = await _processRunner.RunElevatedAsync(
                    "cmd.exe", $"/c pnputil.exe /add-driver \"{infPath}\" /install > \"{pnpOutputFile}\" 2>&1", ct);

                string? pnpOutput = null;
                if (File.Exists(pnpOutputFile))
                {
                    pnpOutput = await File.ReadAllTextAsync(pnpOutputFile, ct);
                    _logger?.LogInformation("pnputil output: {Output}", pnpOutput);
                }

                if (exitCode != 0)
                    throw new SetupException(
                        $"Driver installation failed (exit code {exitCode}).{(pnpOutput != null ? $" pnputil: {pnpOutput.Trim()}" : "")}", exitCode);

                // Verify the driver registered with the store
                var oemInf = await FindOemInfNameAsync(ct);
                if (oemInf == null)
                    throw new SetupException(
                        $"pnputil returned success but the driver was not found in the driver store." +
                        $"{(pnpOutput != null ? $" pnputil output: {pnpOutput.Trim()}" : "")}");

                // For root-enumerated virtual devices, pnputil /add-driver only adds to the store.
                // We must explicitly create the device node if one doesn't exist yet.
                var existingDevice = await GetDeviceInfoAsync(ct);
                if (existingDevice == null)
                {
                    progress?.Report(new SetupProgress(SetupStage.InstallingDriver, 0.95, "Creating virtual display device..."));
                    _logger?.LogInformation("No device node found — creating root-enumerated device for {Inf}", oemInf);
                    await CreateDeviceNodeAsync(infPath, ct);
                }

                progress?.Report(new SetupProgress(SetupStage.Complete, 1.0, "Driver installed successfully."));
                _logger?.LogInformation("Driver installed successfully from release {Tag} as {OemInf}", release.TagName, oemInf);
            }
            finally
            {
                try { if (File.Exists(pnpOutputFile)) File.Delete(pnpOutputFile); }
                catch { /* best-effort cleanup */ }
            }
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    public async Task UninstallDriverAsync(CancellationToken ct = default)
    {
        _logger?.LogInformation("Uninstalling driver...");

        var oemInfName = await FindOemInfNameAsync(ct)
            ?? throw new SetupException("Virtual Display Driver not found in installed drivers.");

        // /uninstall /force: removes driver from devices and deletes the driver package.
        // Device nodes are kept so /add-driver /install can reattach without a reboot.
        var exitCode = await _processRunner.RunElevatedAsync(
            "pnputil.exe", $"/delete-driver \"{oemInfName}\" /uninstall /force", ct);

        if (exitCode != 0)
            throw new SetupException($"Driver uninstallation failed. pnputil exit code: {exitCode}", exitCode);

        _logger?.LogInformation("Driver uninstalled successfully.");
    }

    public async Task<DeviceState> GetDeviceStateAsync(CancellationToken ct = default)
    {
        var info = await GetDeviceInfoAsync(ct);
        if (info is null) return DeviceState.NotFound;
        if (info.HasError) return DeviceState.Error;
        return info.IsEnabled ? DeviceState.Enabled : DeviceState.Disabled;
    }

    public async Task<DeviceInfo?> GetDeviceInfoAsync(CancellationToken ct = default)
    {
        var all = await GetAllDeviceInfoAsync(ct);
        return all.Count > 0 ? all[0] : null;
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetAllDeviceInfoAsync(CancellationToken ct = default)
    {
        var output = await _processRunner.RunAndCaptureOutputAsync(
            "pnputil.exe", "/enum-devices /class Display", ct);

        return ParseAllDeviceInfo(output);
    }

    public async Task EnableDeviceAsync(CancellationToken ct = default)
    {
        var devices = await GetAllDeviceInfoAsync(ct);
        if (devices.Count == 0)
            throw new SetupException("Virtual Display Driver device not found. Is the driver installed?");

        foreach (var device in devices)
        {
            if (device.IsEnabled) continue;

            _logger?.LogInformation("Enabling device {InstanceId}", device.InstanceId);
            var exitCode = await _processRunner.RunElevatedAsync(
                "pnputil.exe", $"/enable-device \"{device.InstanceId}\"", ct);

            if (exitCode != 0)
                throw new SetupException($"Failed to enable device {device.InstanceId}. pnputil exit code: {exitCode}", exitCode);
        }

        _logger?.LogInformation("All devices enabled successfully.");
    }

    public async Task DisableDeviceAsync(CancellationToken ct = default)
    {
        var devices = await GetAllDeviceInfoAsync(ct);
        if (devices.Count == 0)
            throw new SetupException("Virtual Display Driver device not found. Is the driver installed?");

        foreach (var device in devices)
        {
            if (!device.IsEnabled && !device.HasError) continue;

            _logger?.LogInformation("Disabling device {InstanceId}", device.InstanceId);
            var exitCode = await _processRunner.RunElevatedAsync(
                "pnputil.exe", $"/disable-device \"{device.InstanceId}\"", ct);

            if (exitCode != 0)
                throw new SetupException($"Failed to disable device {device.InstanceId}. pnputil exit code: {exitCode}", exitCode);
        }

        _logger?.LogInformation("All devices disabled successfully.");
    }

    public async Task RestartDeviceAsync(CancellationToken ct = default)
    {
        var devices = await GetAllDeviceInfoAsync(ct);
        if (devices.Count == 0)
            throw new SetupException("Virtual Display Driver device not found. Is the driver installed?");

        foreach (var device in devices)
        {
            _logger?.LogInformation("Restarting device {InstanceId}", device.InstanceId);
            var exitCode = await _processRunner.RunElevatedAsync(
                "pnputil.exe", $"/restart-device \"{device.InstanceId}\"", ct);

            if (exitCode != 0)
                throw new SetupException($"Failed to restart device {device.InstanceId}. pnputil exit code: {exitCode}", exitCode);
        }

        _logger?.LogInformation("All devices restarted successfully.");
    }

    // --- Private helpers ---

    private async Task<GitHubRelease> FetchLatestReleaseAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SetupException("Failed to connect to GitHub. Check your internet connection.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new SetupException($"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<GitHubRelease>(json)
                ?? throw new SetupException("Failed to parse GitHub release response.");
        }
    }

    internal static string GetAssetName()
    {
        // The upstream GitHub releases only provide two architecture variants:
        // "x86" (covers both x86 and x64) and "ARM64".
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "ARM64",
            Architecture.X64 => "x86",
            Architecture.X86 => "x86",
            _ => throw new SetupException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };

        return $"VirtualDisplayDriver-{arch}.Driver.Only.zip";
    }

    private async Task DownloadFileAsync(
        string url, string destinationPath, long expectedSize,
        IProgress<SetupProgress>? progress, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

        var buffer = new byte[8192];
        long bytesDownloaded = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesDownloaded += bytesRead;

            if (totalBytes > 0)
            {
                var downloadPercent = (double)bytesDownloaded / totalBytes;
                progress?.Report(new SetupProgress(
                    SetupStage.Downloading,
                    0.05 + downloadPercent * 0.80,
                    $"Downloading... {bytesDownloaded / 1024} KB / {totalBytes / 1024} KB"));
            }
        }
    }

    internal static DeviceInfo? ParseDeviceInfo(string pnpUtilOutput)
        => ParseAllDeviceInfo(pnpUtilOutput).FirstOrDefault();

    internal static IReadOnlyList<DeviceInfo> ParseAllDeviceInfo(string pnpUtilOutput)
    {
        var results = new List<DeviceInfo>();

        if (string.IsNullOrWhiteSpace(pnpUtilOutput))
            return results;

        // pnputil /enum-devices output is block-based, separated by blank lines.
        var blocks = pnpUtilOutput.Split(
            ["\r\n\r\n", "\n\n"],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0) continue;

                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                values[key] = value;
            }

            var isVdd = (values.TryGetValue("Device Description", out var desc) &&
                         desc.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase)) ||
                        (values.TryGetValue("Manufacturer Name", out var mfr) &&
                         mfr.Contains("MikeTheTech", StringComparison.OrdinalIgnoreCase)) ||
                        (values.TryGetValue("Driver Name", out var drv) &&
                         drv.Contains("MttVDD", StringComparison.OrdinalIgnoreCase));

            if (!isVdd) continue;

            if (!values.TryGetValue("Instance ID", out var instanceId))
                continue;

            var description = desc ?? "Virtual Display Driver";
            values.TryGetValue("Status", out var status);

            var isEnabled = status?.Contains("Started", StringComparison.OrdinalIgnoreCase) == true;
            var hasError = status?.Contains("Problem", StringComparison.OrdinalIgnoreCase) == true;

            int? problemCode = null;
            if (hasError && values.TryGetValue("Problem Code", out var problemStr))
            {
                var spaceIndex = problemStr.IndexOf(' ');
                var codeStr = spaceIndex > 0 ? problemStr[..spaceIndex] : problemStr;
                if (int.TryParse(codeStr, out var code))
                    problemCode = code;
            }

            results.Add(new DeviceInfo(instanceId, description, isEnabled, hasError, problemCode));
        }

        return results;
    }

    private async Task<string?> FindOemInfNameAsync(CancellationToken ct)
    {
        var output = await _processRunner.RunAndCaptureOutputAsync(
            "pnputil.exe", "/enum-drivers", ct);

        return ParseOemInfName(output);
    }

    internal static string? ParseOemInfName(string pnpUtilOutput)
    {
        if (string.IsNullOrWhiteSpace(pnpUtilOutput))
            return null;

        var blocks = pnpUtilOutput.Split(
            ["\r\n\r\n", "\n\n"],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            // Look for a block that references MttVDD.inf as the original name
            if (!block.Contains(InfFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0) continue;

                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();

                // The published/OEM name line, e.g. "Published Name: oem42.inf"
                if (key.Contains("Published", StringComparison.OrdinalIgnoreCase) &&
                    value.StartsWith("oem", StringComparison.OrdinalIgnoreCase) &&
                    value.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a root-enumerated device node using SetupDI APIs via an elevated PowerShell process.
    /// This is equivalent to what <c>devcon install</c> does: it creates the device node so that
    /// Windows PnP can match it to the driver already in the store.
    /// </summary>
    private async Task CreateDeviceNodeAsync(string infPath, CancellationToken ct)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"vdd-create-device-{Guid.NewGuid():N}.ps1");
        var outputPath = Path.Combine(Path.GetTempPath(), $"vdd-create-device-{Guid.NewGuid():N}.log");

        try
        {
            // Write the PowerShell script that P/Invokes SetupDI to create the device node,
            // then calls UpdateDriverForPlugAndPlayDevices to install the driver on it.
            var script = CreateDeviceNodeScript
                .Replace("{{INF_PATH}}", infPath.Replace(@"\", @"\\"))
                .Replace("{{OUTPUT_PATH}}", outputPath.Replace(@"\", @"\\"));

            await File.WriteAllTextAsync(scriptPath, script, ct);

            var exitCode = await _processRunner.RunElevatedAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                ct);

            string? output = null;
            if (File.Exists(outputPath))
                output = (await File.ReadAllTextAsync(outputPath, ct)).Trim();

            _logger?.LogInformation("Device node creation result: exit={ExitCode} output={Output}", exitCode, output);

            if (exitCode != 0 || output?.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase) != true)
                throw new SetupException($"Failed to create virtual display device.{(output != null ? $" {output}" : "")}");
        }
        finally
        {
            try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }

    // PowerShell script that creates a root-enumerated device node via SetupDI P/Invoke.
    // Placeholders: {{INF_PATH}}, {{OUTPUT_PATH}}
    private const string CreateDeviceNodeScript = """
        $code = @"
        using System;
        using System.Runtime.InteropServices;

        public class VddDeviceCreator
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct SP_DEVINFO_DATA
            {
                public int cbSize;
                public Guid ClassGuid;
                public int DevInst;
                public IntPtr Reserved;
            }

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr hwndParent);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool SetupDiCreateDeviceInfoW(
                IntPtr DeviceInfoSet, string DeviceName, ref Guid ClassGuid,
                string DeviceDescription, IntPtr hwndParent, uint CreationFlags,
                ref SP_DEVINFO_DATA DeviceInfoData);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool SetupDiSetDeviceRegistryPropertyW(
                IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
                uint Property, byte[] PropertyBuffer, uint PropertyBufferSize);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool SetupDiCallClassInstaller(
                uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

            [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool UpdateDriverForPlugAndPlayDevicesW(
                IntPtr hwndParent, string HardwareId, string FullInfPath,
                uint InstallFlags, out bool bRebootRequired);

            const uint DICD_GENERATE_ID = 1;
            const uint SPDRP_HARDWAREID = 1;
            const uint DIF_REGISTERDEVICE = 0x19;

            public static string CreateAndInstall(string hardwareId, string infPath)
            {
                Guid classGuid = new Guid("4D36E968-E325-11CE-BFC1-08002BE10318");
                IntPtr devInfoSet = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
                if (devInfoSet == (IntPtr)(-1))
                    return "FAILED: SetupDiCreateDeviceInfoList error " + Marshal.GetLastWin32Error();

                try
                {
                    SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                    if (!SetupDiCreateDeviceInfoW(devInfoSet, "Display", ref classGuid,
                        null, IntPtr.Zero, DICD_GENERATE_ID, ref devInfoData))
                        return "FAILED: SetupDiCreateDeviceInfo error " + Marshal.GetLastWin32Error();

                    byte[] hwIdBytes = System.Text.Encoding.Unicode.GetBytes(hardwareId + "\0\0");
                    if (!SetupDiSetDeviceRegistryPropertyW(devInfoSet, ref devInfoData,
                        SPDRP_HARDWAREID, hwIdBytes, (uint)hwIdBytes.Length))
                        return "FAILED: SetupDiSetDeviceRegistryProperty error " + Marshal.GetLastWin32Error();

                    if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, devInfoSet, ref devInfoData))
                        return "FAILED: SetupDiCallClassInstaller error " + Marshal.GetLastWin32Error();

                    bool rebootRequired;
                    if (!UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hardwareId, infPath, 0, out rebootRequired))
                    {
                        int err = Marshal.GetLastWin32Error();
                        return "FAILED: UpdateDriverForPlugAndPlayDevices error " + err;
                    }

                    return rebootRequired ? "SUCCESS_REBOOT" : "SUCCESS";
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfoSet);
                }
            }
        }
        "@

        try {
            Add-Type -TypeDefinition $code
            $result = [VddDeviceCreator]::CreateAndInstall("Root\MttVDD", "{{INF_PATH}}")
            $result | Out-File -FilePath "{{OUTPUT_PATH}}" -Encoding UTF8
            if ($result.StartsWith("FAILED")) { exit 1 }
        } catch {
            "ERROR: $($_.Exception.Message)" | Out-File -FilePath "{{OUTPUT_PATH}}" -Encoding UTF8
            exit 1
        }
        """;
}
