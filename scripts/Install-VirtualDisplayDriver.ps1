<#
.SYNOPSIS
    Downloads and installs the Virtual Display Driver (VDD) from GitHub.

.DESCRIPTION
    Fetches the latest release from VirtualDrivers/Virtual-Display-Driver,
    downloads the driver-only package for the current architecture,
    extracts it to C:\VirtualDisplayDriver, and installs via devcon or pnputil.
    Automatically elevates to admin when installation is required.

.PARAMETER InstallPath
    Target installation directory. Defaults to C:\VirtualDisplayDriver.

.PARAMETER SkipInstall
    Download and extract only, do not install the driver.

.EXAMPLE
    .\Install-VirtualDisplayDriver.ps1
    .\Install-VirtualDisplayDriver.ps1 -SkipInstall
    .\Install-VirtualDisplayDriver.ps1 -InstallPath "D:\Drivers\VDD"
#>
param(
    [string]$InstallPath = "C:\VirtualDisplayDriver",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

# Self-elevate if not running as admin and installation is needed
if (-not $SkipInstall) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "Restarting as Administrator..." -ForegroundColor Yellow
        $args = @("-ExecutionPolicy", "Bypass", "-File", $PSCommandPath, "-InstallPath", $InstallPath)
        Start-Process powershell -Verb RunAs -ArgumentList $args -Wait
        exit $LASTEXITCODE
    }
}

$repo = "VirtualDrivers/Virtual-Display-Driver"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"

# Determine architecture-specific asset name pattern
$arch = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    "Arm64" { "ARM64" }
    default { "x86" }
}
$assetPattern = "VirtualDisplayDriver-$arch.Driver.Only.zip"

Write-Host "Fetching latest release from $repo..." -ForegroundColor Cyan
$release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "VDD-Installer" }
$tag = $release.tag_name
Write-Host "Latest release: $tag" -ForegroundColor Green

$asset = $release.assets | Where-Object { $_.name -eq $assetPattern }
if (-not $asset) {
    Write-Error "Asset '$assetPattern' not found in release $tag. Available assets: $($release.assets.name -join ', ')"
    exit 1
}

$downloadUrl = $asset.browser_download_url
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "vdd-install-$tag"
$zipPath = Join-Path $tempDir $asset.name

# Download
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1KB)) KB)..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing

# Extract
Write-Host "Extracting to $InstallPath..." -ForegroundColor Cyan
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}
Expand-Archive -Path $zipPath -DestinationPath $InstallPath -Force

# Clean up temp files
Remove-Item $tempDir -Recurse -Force

Write-Host "Driver files extracted to $InstallPath" -ForegroundColor Green

if ($SkipInstall) {
    Write-Host "Skipping driver installation (-SkipInstall specified)." -ForegroundColor Yellow
    Write-Host "To install manually, run: pnputil /add-driver `"$InstallPath\MttVDD.inf`" /install" -ForegroundColor Yellow
    exit 0
}

# Install the driver
$infPath = Get-ChildItem -Path $InstallPath -Filter "MttVDD.inf" -Recurse | Select-Object -First 1
if (-not $infPath) {
    Write-Error "MttVDD.inf not found in $InstallPath. Extract may have failed."
    exit 1
}

Write-Host "Installing driver from $($infPath.FullName)..." -ForegroundColor Cyan
pnputil /add-driver $infPath.FullName /install

if ($LASTEXITCODE -eq 0) {
    Write-Host "Driver installed successfully." -ForegroundColor Green
} else {
    Write-Error "Driver installation failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
