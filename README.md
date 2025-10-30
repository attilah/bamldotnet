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
├── BUILD_STATUS.md               # Detailed build status report
├── baml_dotnet.md                # Complete implementation guide
│
├── src/
│   ├── Baml.Net/                 # Main managed library (net9.0)
│   │   ├── Baml.Net.csproj
│   │   └── build/
│   │       └── Baml.Net.targets  # Auto-reference native packages
│   │
│   └── Native/                   # Platform-specific native packages
│       ├── Baml.Net.Native.win-x64/
│       ├── Baml.Net.Native.win-arm64/
│       ├── Baml.Net.Native.linux-x64/
│       ├── Baml.Net.Native.linux-arm64/
│       ├── Baml.Net.Native.osx-x64/
│       └── Baml.Net.Native.osx-arm64/
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

1. Download BAML native binaries from releases
2. Place in respective `src/Native/Baml.Net.Native.{rid}/runtimes/{rid}/native/`
3. Enable package generation in Directory.Build.props:
   ```xml
   <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
   ```
4. Build: `dotnet pack`

### Adding Managed Code

The main `Baml.Net` package is ready for implementation:

**Planned Structure** (from baml_dotnet.md):
```
src/Baml.Net/
├── Core/           # BamlRuntime, RuntimeContext
├── FFI/            # P/Invoke declarations
├── Protobuf/       # Generated protobuf classes
├── Types/          # BamlImage, TypeBuilder, etc.
├── Streaming/      # IAsyncEnumerable support
├── Events/         # Event system
├── Client/         # ClientRegistry
├── Logging/        # Collector, FunctionLog
├── Exceptions/     # Custom exceptions
└── Extensions/     # Helper methods
```

## 📚 Documentation

- **[baml_dotnet.md](baml_dotnet.md)**: Complete implementation guide with TypeScript parity features
- **[BUILD_STATUS.md](BUILD_STATUS.md)**: Current build status and next steps

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
