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

            // Install driver via pnputil (elevated)
            progress?.Report(new SetupProgress(SetupStage.InstallingDriver, 0.90, "Installing driver (admin required)..."));
            _logger?.LogInformation("Installing driver from {InfPath}", infPath);

            var exitCode = await _processRunner.RunElevatedAsync(
                "pnputil.exe", $"/add-driver \"{infPath}\" /install", ct);

            if (exitCode != 0)
                throw new SetupException($"Driver installation failed. pnputil exit code: {exitCode}", exitCode);

            progress?.Report(new SetupProgress(SetupStage.Complete, 1.0, "Driver installed successfully."));
            _logger?.LogInformation("Driver installed successfully from release {Tag}", release.TagName);
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
        var output = await _processRunner.RunAndCaptureOutputAsync(
            "pnputil.exe", "/enum-devices /class Display", ct);

        return ParseDeviceInfo(output);
    }

    public async Task EnableDeviceAsync(CancellationToken ct = default)
    {
        var info = await GetDeviceInfoAsync(ct)
            ?? throw new SetupException("Virtual Display Driver device not found. Is the driver installed?");

        if (info.IsEnabled)
        {
            _logger?.LogInformation("Device is already enabled.");
            return;
        }

        _logger?.LogInformation("Enabling device {InstanceId}", info.InstanceId);

        var exitCode = await _processRunner.RunElevatedAsync(
            "pnputil.exe", $"/enable-device \"{info.InstanceId}\"", ct);

        if (exitCode != 0)
            throw new SetupException($"Failed to enable device. pnputil exit code: {exitCode}", exitCode);

        _logger?.LogInformation("Device enabled successfully.");
    }

    public async Task DisableDeviceAsync(CancellationToken ct = default)
    {
        var info = await GetDeviceInfoAsync(ct)
            ?? throw new SetupException("Virtual Display Driver device not found. Is the driver installed?");

        if (!info.IsEnabled)
        {
            _logger?.LogInformation("Device is already disabled.");
            return;
        }

        _logger?.LogInformation("Disabling device {InstanceId}", info.InstanceId);

        var exitCode = await _processRunner.RunElevatedAsync(
            "pnputil.exe", $"/disable-device \"{info.InstanceId}\"", ct);

        if (exitCode != 0)
            throw new SetupException($"Failed to disable device. pnputil exit code: {exitCode}", exitCode);

        _logger?.LogInformation("Device disabled successfully.");
    }

    public async Task RestartDeviceAsync(CancellationToken ct = default)
    {
        var info = await GetDeviceInfoAsync(ct)
            ?? throw new SetupException("Virtual Display Driver device not found. Is the driver installed?");

        _logger?.LogInformation("Restarting device {InstanceId}", info.InstanceId);

        var exitCode = await _processRunner.RunElevatedAsync(
            "pnputil.exe", $"/restart-device \"{info.InstanceId}\"", ct);

        if (exitCode != 0)
            throw new SetupException($"Failed to restart device. pnputil exit code: {exitCode}", exitCode);

        _logger?.LogInformation("Device restarted successfully.");
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
    {
        if (string.IsNullOrWhiteSpace(pnpUtilOutput))
            return null;

        // pnputil /enum-devices output is block-based, separated by blank lines.
        // Each block has key-value pairs like:
        //   Instance ID:        ROOT\DISPLAY\0000
        //   Device Description: Virtual Display
        //   Status:             Started
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

            // Match on manufacturer or driver name to identify VDD
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
                // Problem Code format: "43 (0x2B) [CM_PROB_FAILED_POST_START]"
                var spaceIndex = problemStr.IndexOf(' ');
                var codeStr = spaceIndex > 0 ? problemStr[..spaceIndex] : problemStr;
                if (int.TryParse(codeStr, out var code))
                    problemCode = code;
            }

            return new DeviceInfo(instanceId, description, isEnabled, hasError, problemCode);
        }

        return null;
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
}
