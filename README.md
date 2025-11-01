# Baml.Net - BAML Runtime for .NET 9.0

A .NET implementation of the BAML (Boundary AI Markup Language) runtime, providing AI function orchestration and LLM integration for .NET applications.

## 🏗️ Project Status

**Phase:** Initial Setup - Native Package Infrastructure ✅

The solution structure and native package framework have been successfully implemented. The foundation is ready for:
1. Native BAML binary integration
2. Managed C# runtime code implementation

## 📦 Solution Structure

```
Baml.Net/
├── Baml.Net.sln                  # Main solution file
├── Directory.Build.props          # Shared MSBuild properties
├── Directory.Packages.props       # Centralized package version management
├── global.json                    # .NET SDK version pinning
│
├── src/
│   └── Baml.Net/                 # Main managed library (net9.0)
│       ├── Baml.Net.csproj
│       ├── Core/                 # BamlRuntime, BamlRuntimeAsync
│       ├── FFI/                  # P/Invoke interop layer
│       ├── Types/                # BAML types and builders
│       └── Extensions/           # Helper extensions
│
├── bindings/                     # Platform-specific native packages
│   ├── Directory.Build.props     # Shared native package config
│   ├── Baml.Net.Native.win-x64/
│   ├── Baml.Net.Native.win-arm64/
│   ├── Baml.Net.Native.linux-x64/
│   ├── Baml.Net.Native.linux-arm64/
│   ├── Baml.Net.Native.osx-x64/
│   └── Baml.Net.Native.osx-arm64/
│
├── tests/
│   └── Baml.Net.Tests/           # Unit and integration tests
│       ├── TestBamlSrc/          # BAML test files
│       └── TestData/             # Test data files
│
└── scripts/
    ├── download-natives.sh       # Download native binaries
    ├── download-natives.ps1      # Windows PowerShell version
    └── sync-test-baml-files.sh   # Sync BAML test files
```

## 🚀 Quick Start

### Prerequisites

- .NET 9.0 SDK or later
- Visual Studio 2022 or JetBrains Rider (optional)

### Build

```bash
# Clone the repository
git clone <repository-url>
cd bamldotnet

# Build the solution
dotnet build Baml.Net.sln

# Clean and rebuild
dotnet clean Baml.Net.sln
dotnet build Baml.Net.sln
```

### Current Build Status

✅ **All 7 projects build successfully**
- 1 main package (Baml.Net - net9.0)
- 6 native packages (netstandard2.0)

## 🎯 Package Architecture

This project follows the **n+1 NuGet package pattern**:

- **1 Main Package (Baml.Net)**: Contains managed C# code
- **N Native Packages**: One per platform, containing native BAML binaries

### Supported Platforms

- Windows x64 (win-x64)
- Windows ARM64 (win-arm64)
- Linux x64 (linux-x64)
- Linux ARM64 (linux-arm64)
- macOS x64 (osx-x64)
- macOS ARM64 (osx-arm64)

### Runtime Identifier (RID) Resolution

The main package automatically references the correct native package based on the target platform's RID. Consumers only need to install `Baml.Net`.

## 📋 Configuration

### Shared Properties (Directory.Build.props)

All projects inherit common settings:

- **Version**: 1.0.0
- **Target Framework**: net9.0 (main), netstandard2.0 (native)
- **NuGet Dependencies**:
  - Google.Protobuf: 3.28.3
  - Grpc.Tools: 2.66.0
- **Build Settings**: Documentation generation, nullable reference types

### Versioning

NuGet package versions are centrally managed using MSBuild properties:

```xml
<PackageReference Include="Google.Protobuf" Version="$(GoogleProtobufVersion)" />
```

## 🔧 Development

### Adding Native Binaries

Use the automated download script to fetch native binaries:

```bash
# Download latest BAML native binaries
./scripts/download-natives.sh

# Or download a specific version
./scripts/download-natives.sh -v 0.212.0

# Force re-download
./scripts/download-natives.sh -v 0.212.0 -f
```

Binaries are placed in `runtimes/{rid}/native/` directories. To build packages:

```bash
dotnet pack
```

Packages are output to `artifacts/nuget/`.

### Adding Managed Code

The main `Baml.Net` package contains the .NET runtime implementation:

**Current Structure**:
```
src/Baml.Net/
├── Core/           # BamlRuntime, BamlRuntimeAsync
├── FFI/            # P/Invoke interop layer
├── Types/          # BAML types (BamlImage, BamlAudio, etc.)
├── Extensions/     # Helper extensions
└── Exceptions/     # Custom exceptions
```

See [thoughts/baml_dotnet.md](thoughts/baml_dotnet.md) for complete implementation details.

## 📚 Documentation

- **[thoughts/baml_dotnet.md](thoughts/baml_dotnet.md)**: Complete implementation guide with TypeScript parity features
- **[scripts/README.md](scripts/README.md)**: Scripts documentation and usage

## 🤝 Contributing

This project follows standard .NET conventions:

1. All projects use Directory.Build.props for shared configuration
2. Native packages target netstandard2.0
3. Main package targets net9.0
4. Protocol Buffers for FFI serialization

## 📄 License

Apache-2.0

## 🔗 Related Projects

- [BAML](https://github.com/BoundaryML/baml) - The main BAML project
