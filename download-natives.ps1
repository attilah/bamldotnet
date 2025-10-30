<#
.SYNOPSIS
    Downloads native BAML binaries from GitHub releases and places them in the correct runtime directories.

.DESCRIPTION
    This script downloads the native BAML C FFI binaries for all supported platforms from the BoundaryML/baml
    GitHub releases. It places the binaries in the appropriate runtime directories for each native NuGet package
    and updates the version in Directory.Build.props.

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

# Platform mappings: RID -> (asset name pattern, binary filename)
$Platforms = @{
    "win-x64"     = @{ Asset = "baml-windows-x86_64.tar.gz"; Binary = "baml_cffi.dll" }
    "win-arm64"   = @{ Asset = "baml-windows-aarch64.tar.gz"; Binary = "baml_cffi.dll" }
    "linux-x64"   = @{ Asset = "baml-linux-x86_64.tar.gz"; Binary = "libbaml_cffi.so" }
    "linux-arm64" = @{ Asset = "baml-linux-aarch64.tar.gz"; Binary = "libbaml_cffi.so" }
    "osx-x64"     = @{ Asset = "baml-macos-x86_64.tar.gz"; Binary = "libbaml_cffi.dylib" }
    "osx-arm64"   = @{ Asset = "baml-macos-aarch64.tar.gz"; Binary = "libbaml_cffi.dylib" }
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

    $propsFile = Join-Path $PSScriptRoot "Directory.Build.props"

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

function Download-AndExtract {
    param(
        [string]$Url,
        [string]$OutputDir,
        [string]$BinaryName
    )

    $tempFile = [System.IO.Path]::GetTempFileName() + ".tar.gz"
    $tempExtractDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())

    try {
        Write-Host "  Downloading from $Url..." -ForegroundColor Gray
        Invoke-WebRequest -Uri $Url -OutFile $tempFile -UseBasicParsing

        Write-Host "  Extracting archive..." -ForegroundColor Gray
        New-Item -ItemType Directory -Path $tempExtractDir -Force | Out-Null

        # Extract tar.gz (requires tar.exe available in Windows 10+ or external tool)
        if (Get-Command tar -ErrorAction SilentlyContinue) {
            tar -xzf $tempFile -C $tempExtractDir
        }
        else {
            Write-Error "tar.exe not found. Please install tar or use Windows 10+ which includes it."
            exit 1
        }

        # Find the binary in the extracted files
        $binaryPath = Get-ChildItem -Path $tempExtractDir -Filter $BinaryName -Recurse -File | Select-Object -First 1

        if (-not $binaryPath) {
            Write-Error "Could not find $BinaryName in the extracted archive"
            exit 1
        }

        # Ensure output directory exists
        if (-not (Test-Path $OutputDir)) {
            New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
        }

        # Copy binary to destination
        $destPath = Join-Path $OutputDir $BinaryName
        Copy-Item -Path $binaryPath.FullName -Destination $destPath -Force

        Write-Host "  Binary extracted to: $destPath" -ForegroundColor Gray

        # Verify file size
        $fileInfo = Get-Item $destPath
        Write-Host "  Size: $($fileInfo.Length) bytes" -ForegroundColor Gray
    }
    finally {
        # Cleanup
        if (Test-Path $tempFile) { Remove-Item $tempFile -Force }
        if (Test-Path $tempExtractDir) { Remove-Item $tempExtractDir -Recurse -Force }
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

    $outputDir = Join-Path $PSScriptRoot "src/Native/Baml.Net.Native.$rid/runtimes/$rid/native"
    $binaryPath = Join-Path $outputDir $binary

    # Check if binary already exists and Force is not set
    if ((Test-Path $binaryPath) -and -not $Force) {
        Write-Host "[$rid] Binary already exists. Use -Force to re-download." -ForegroundColor Gray
        $successCount++
        continue
    }

    $downloadUrl = "$BaseUrl/$Version/$asset"

    try {
        Download-AndExtract -Url $downloadUrl -OutputDir $outputDir -BinaryName $binary
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
