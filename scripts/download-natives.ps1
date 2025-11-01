<#
.SYNOPSIS
    Downloads native BAML binaries from GitHub releases and places them in the runtimes directory.

.DESCRIPTION
    This script downloads the native BAML C FFI binaries for all supported platforms from the BoundaryML/baml
    GitHub releases. It places the binaries in a central runtimes directory at the project root and updates
    the version in Directory.Build.props.

.PARAMETER Version
    The BAML version to download. If not specified, fetches the latest release from GitHub.

.PARAMETER Force
    Force re-download even if files already exist.

.EXAMPLE
    .\download-natives.ps1
    Downloads the latest BAML release binaries.

.EXAMPLE
    .\download-natives.ps1 -Version "0.64.0"
    Downloads BAML version 0.64.0 binaries.

.EXAMPLE
    .\download-natives.ps1 -Version "0.64.0" -Force
    Forces re-download of version 0.64.0 binaries.
#>

param(
    [string]$Version = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# GitHub repository information
$Owner = "BoundaryML"
$Repo = "baml"
$BaseUrl = "https://github.com/$Owner/$Repo/releases/download"

# Platform mappings: RID -> (asset name, binary filename)
$Platforms = @{
    "win-x64"     = @{ Asset = "baml_cffi-x86_64-pc-windows-msvc.dll"; Binary = "baml_cffi.dll" }
    "win-arm64"   = @{ Asset = "baml_cffi-aarch64-pc-windows-msvc.dll"; Binary = "baml_cffi.dll" }
    "linux-x64"   = @{ Asset = "libbaml_cffi-x86_64-unknown-linux-gnu.so"; Binary = "libbaml_cffi.so" }
    "linux-arm64" = @{ Asset = "libbaml_cffi-aarch64-unknown-linux-gnu.so"; Binary = "libbaml_cffi.so" }
    "osx-x64"     = @{ Asset = "libbaml_cffi-x86_64-apple-darwin.dylib"; Binary = "libbaml_cffi.dylib" }
    "osx-arm64"   = @{ Asset = "libbaml_cffi-aarch64-apple-darwin.dylib"; Binary = "libbaml_cffi.dylib" }
}

function Get-LatestRelease {
    Write-Host "Fetching latest BAML release from GitHub..." -ForegroundColor Cyan
    try {
        $response = Invoke-RestMethod -Uri "https://api.github.com/repos/$Owner/$Repo/releases/latest" -Headers @{ "User-Agent" = "Baml.Net-Downloader" }
        $latestVersion = $response.tag_name
        if ($latestVersion.StartsWith("v")) {
            $latestVersion = $latestVersion.Substring(1)
        }
        Write-Host "Latest release: $latestVersion" -ForegroundColor Green
        return $latestVersion
    }
    catch {
        Write-Error "Failed to fetch latest release: $_"
        exit 1
    }
}

function Update-DirectoryBuildProps {
    param([string]$NewVersion)

    $propsFile = Join-Path (Split-Path $PSScriptRoot -Parent) "Directory.Build.props"

    if (-not (Test-Path $propsFile)) {
        Write-Error "Directory.Build.props not found at: $propsFile"
        exit 1
    }

    Write-Host "Updating Directory.Build.props with version $NewVersion..." -ForegroundColor Cyan

    $content = Get-Content $propsFile -Raw

    # Check if BamlNativeVersion property exists
    if ($content -match '<BamlNativeVersion>.*?</BamlNativeVersion>') {
        # Update existing version
        $content = $content -replace '<BamlNativeVersion>.*?</BamlNativeVersion>', "<BamlNativeVersion>$NewVersion</BamlNativeVersion>"
    }
    else {
        # Add BamlNativeVersion property after other version properties
        if ($content -match '(<GoogleProtobufVersion>.*?</GoogleProtobufVersion>)') {
            $content = $content -replace '(<GoogleProtobufVersion>.*?</GoogleProtobufVersion>)', "`$1`n    <BamlNativeVersion>$NewVersion</BamlNativeVersion>"
        }
        else {
            Write-Error "Could not find insertion point for BamlNativeVersion in Directory.Build.props"
            exit 1
        }
    }

    Set-Content $propsFile -Value $content -NoNewline
    Write-Host "Updated Directory.Build.props with BamlNativeVersion=$NewVersion" -ForegroundColor Green
}

function Download-Binary {
    param(
        [string]$Url,
        [string]$OutputDir,
        [string]$BinaryName
    )

    try {
        Write-Host "  Downloading from $Url..." -ForegroundColor Gray

        # Ensure output directory exists
        if (-not (Test-Path $OutputDir)) {
            New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
        }

        # Download binary directly to destination
        $destPath = Join-Path $OutputDir $BinaryName
        Invoke-WebRequest -Uri $Url -OutFile $destPath -UseBasicParsing

        Write-Host "  Binary saved to: $destPath" -ForegroundColor Gray

        # Verify file size
        $fileInfo = Get-Item $destPath
        Write-Host "  Size: $($fileInfo.Length) bytes" -ForegroundColor Gray
    }
    catch {
        Write-Host "  Failed to download: $_" -ForegroundColor Red
        if (Test-Path $destPath) { Remove-Item $destPath -Force }
        throw
    }
}

# Main script
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "BAML Native Binary Downloader" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Determine version to download
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-LatestRelease
}
else {
    # Remove 'v' prefix if present
    if ($Version.StartsWith("v")) {
        $Version = $Version.Substring(1)
    }
    Write-Host "Using specified version: $Version" -ForegroundColor Green
}

# Update Directory.Build.props
Update-DirectoryBuildProps -NewVersion $Version

Write-Host ""
Write-Host "Downloading native binaries for version $Version..." -ForegroundColor Cyan
Write-Host ""

$successCount = 0
$failedPlatforms = @()

foreach ($platform in $Platforms.GetEnumerator()) {
    $rid = $platform.Key
    $asset = $platform.Value.Asset
    $binary = $platform.Value.Binary

    Write-Host "[$rid] Processing..." -ForegroundColor Yellow

    $rootDir = Split-Path $PSScriptRoot -Parent
    $outputDir = Join-Path $rootDir "runtimes/$rid"
    $binaryPath = Join-Path $outputDir $binary

    # Check if binary already exists and Force is not set
    if ((Test-Path $binaryPath) -and -not $Force) {
        Write-Host "[$rid] Binary already exists. Use -Force to re-download." -ForegroundColor Gray
        $successCount++
        Write-Host ""
        continue
    }

    $downloadUrl = "$BaseUrl/$Version/$asset"

    try {
        Download-Binary -Url $downloadUrl -OutputDir $outputDir -BinaryName $binary
        Write-Host "[$rid] SUCCESS" -ForegroundColor Green
        $successCount++
    }
    catch {
        Write-Host "[$rid] FAILED: $_" -ForegroundColor Red
        $failedPlatforms += $rid
    }

    Write-Host ""
}

# Summary
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Download Summary" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Successfully downloaded: $successCount/$($Platforms.Count)" -ForegroundColor $(if ($successCount -eq $Platforms.Count) { "Green" } else { "Yellow" })

if ($failedPlatforms.Count -gt 0) {
    Write-Host "Failed platforms: $($failedPlatforms -join ', ')" -ForegroundColor Red
    exit 1
}
else {
    Write-Host ""
    Write-Host "All native binaries downloaded successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Enable package generation in Directory.Build.props:" -ForegroundColor Gray
    Write-Host "     <GeneratePackageOnBuild>true</GeneratePackageOnBuild>" -ForegroundColor Gray
    Write-Host "  2. Build NuGet packages:" -ForegroundColor Gray
    Write-Host "     dotnet pack Baml.Net.sln" -ForegroundColor Gray
    exit 0
}
