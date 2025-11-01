# Build Scripts

This directory contains scripts for downloading and managing native BAML binaries.

## Download Native Binaries

Before building the solution, you need to download the native BAML binaries from GitHub releases.

### Bash (macOS/Linux)

```bash
# Download latest release
./scripts/download-natives.sh

# Download specific version
./scripts/download-natives.sh -v 0.64.0

# Force re-download
./scripts/download-natives.sh -v 0.64.0 -f
```

### PowerShell (Windows)

```powershell
# Download latest release
.\scripts\download-natives.ps1

# Download specific version
.\scripts\download-natives.ps1 -Version "0.64.0"

# Force re-download
.\scripts\download-natives.ps1 -Version "0.64.0" -Force
```

## How It Works

1. The scripts download native binaries for all supported platforms from the [BoundaryML/baml](https://github.com/BoundaryML/baml) GitHub releases
2. Binaries are extracted to the central `runtimes/` folder at the project root
3. The version is automatically updated in `Directory.Build.props`
4. Native binding projects in `bindings/` reference these binaries

## Directory Structure

```
bamldotnet/
├── runtimes/                   # Central location for native binaries (gitignored)
│   ├── win-x64/
│   │   └── baml_cffi.dll
│   ├── win-arm64/
│   │   └── baml_cffi.dll
│   ├── linux-x64/
│   │   └── libbaml_cffi.so
│   ├── linux-arm64/
│   │   └── libbaml_cffi.so
│   ├── osx-x64/
│   │   └── libbaml_cffi.dylib
│   └── osx-arm64/
│       └── libbaml_cffi.dylib
├── bindings/                   # Native binding projects
│   ├── Baml.Net.Native.win-x64/
│   ├── Baml.Net.Native.win-arm64/
│   ├── Baml.Net.Native.linux-x64/
│   ├── Baml.Net.Native.linux-arm64/
│   ├── Baml.Net.Native.osx-x64/
│   └── Baml.Net.Native.osx-arm64/
└── scripts/                    # Build scripts
    ├── download-natives.sh
    ├── download-natives.ps1
    └── README.md
```

## Building the Solution

After downloading the binaries:

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Create NuGet packages
dotnet pack
```

Note: On macOS/Linux, Windows native projects may fail to build due to missing binaries. This is expected and doesn't affect the main package build.
