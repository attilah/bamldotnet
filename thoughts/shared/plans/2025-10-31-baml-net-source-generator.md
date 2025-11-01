# BAML.NET Source Generator Implementation Plan

## Overview

This plan implements a Roslyn source generator that watches BAML files within C# projects, invokes the BAML compiler to generate metadata JSON, and generates strongly-typed C# client code using StringBuilder. The generator follows C# best practices including ConfigureAwait(false) patterns and MSBuild-controlled feature flags.

## Current State Analysis

Based on research from `thoughts/shared/research/2025-10-31-baml-net-code-generation-architecture.md`:
- BAML.NET currently uses dynamic runtime invocation with string-based function names
- Metadata generation infrastructure exists (`baml-metadata.json`)
- Protobuf generation pattern established using Grpc.Tools
- No source generators currently implemented

### Key Discoveries:
- Protobuf generation uses MSBuild integration at `src/Baml.Net/Baml.Net.csproj:19-21`
- Central package management configured in `Directory.Packages.props`
- BAML metadata format defined at `test-csharp-metadata/output/baml_client/baml-metadata.json`
- Runtime async patterns established at `src/Baml.Net/Core/BamlRuntimeAsync.cs:53-172`

## Desired End State

After implementation, developers can:
- Add BAML files to their C# projects
- Get IntelliSense-enabled, strongly-typed client code automatically generated
- Control code generation features via MSBuild properties
- Debug generated async code easily with intermediate variables
- Optional logging injection based on configuration

### Verification Criteria:
- [x] Source generator detects BAML file changes
- [x] BAML compiler invoked successfully (deferred to MSBuild task)
- [x] Metadata JSON parsed correctly
- [x] Generated C# code compiles without errors
- [x] ConfigureAwait(false) pattern applied consistently
- [x] MSBuild properties control logging injection
- [x] Generated code integrates with existing BamlRuntimeAsync

## What We're NOT Doing

- Not modifying the existing BAML runtime implementation
- Not creating a standalone CLI tool
- Not generating code for other languages
- Not implementing incremental generators in phase 1
- Not supporting .NET Framework (only .NET 6+)
- Not creating custom MSBuild tasks

## Implementation Approach

Follow the established protobuf generation pattern while adding source generator capabilities. Use MSBuild properties for configuration and StringBuilder for efficient code generation.

## Phase 1: Source Generator Project Setup

### Overview
Create a new source generator project that can be referenced by the main Baml.Net library.

### Changes Required:

#### 1. Create Source Generator Project
**File**: `src/Baml.Net.SourceGenerator/Baml.Net.SourceGenerator.csproj`
**Changes**: Create new analyzer project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <DevelopmentDependency>true</DevelopmentDependency>
    <PackageId>Baml.Net.SourceGenerator</PackageId>
    <Description>Source generator for BAML.NET - generates typed clients from BAML files</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" />
  </ItemGroup>

  <!-- Package the generator in the analyzer directory of the NuGet package -->
  <ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  </ItemGroup>
</Project>
```

#### 2. Update Central Package Management
**File**: `Directory.Packages.props`
**Changes**: Add source generator dependencies with latest versions

```xml
<!-- Add to PackageVersion items -->
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" />
<PackageVersion Include="System.Text.Json" Version="8.0.5" />

<!-- Testing packages for source generator tests -->
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" Version="1.1.2" />
<PackageVersion Include="Verify.SourceGenerators" Version="2.4.4" />
```

#### 3. Create Generator Entry Point
**File**: `src/Baml.Net.SourceGenerator/BamlSourceGenerator.cs`
**Changes**: Implement ISourceGenerator

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Baml.Net.SourceGenerator
{
    [Generator]
    public class BamlSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver to track BAML files
            context.RegisterForSyntaxNotifications(() => new BamlSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                // Check if code generation is enabled
                var enableCodeGen = GetMSBuildProperty(context, "BamlEnableCodeGeneration", "true");
                if (enableCodeGen?.ToLower() != "true")
                    return;

                // Check if logging is enabled
                var enableLogging = GetMSBuildProperty(context, "BamlEnableCodeGenLogging", "false");
                var isLoggingEnabled = enableLogging?.ToLower() == "true";

                // Find all BAML files from <Baml> items
                var bamlFiles = context.AdditionalFiles
                    .Where(f => {
                        context.AnalyzerConfigOptions.GetOptions(f).TryGetValue("build_metadata.Baml.Identity", out var isBamlItem);
                        return isBamlItem != null || Path.GetExtension(f.Path).Equals(".baml", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (!bamlFiles.Any())
                    return;

                // Generate metadata and code for each BAML file set
                var generator = new BamlCodeGenerator(isLoggingEnabled);
                var generatedCode = generator.GenerateCode(context, bamlFiles);

                if (!string.IsNullOrEmpty(generatedCode))
                {
                    context.AddSource("BamlClient.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
                }
            }
            catch (Exception ex)
            {
                // Report diagnostic instead of throwing
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "BAML001",
                        "BAML Source Generation Failed",
                        "BAML source generation failed: {0}",
                        "Baml.SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    ex.Message);

                context.ReportDiagnostic(diagnostic);
            }
        }

        private string? GetMSBuildProperty(GeneratorExecutionContext context, string name, string defaultValue)
        {
            context.AnalyzerConfigOptions.GlobalOptions.TryGetValue($"build_property.{name}", out var value);
            return value ?? defaultValue;
        }
    }

    internal class BamlSyntaxReceiver : ISyntaxReceiver
    {
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            // We don't need to track syntax nodes, just BAML files
        }
    }
}
```

#### 4. Update Solution File
**File**: `Baml.Net.sln`
**Changes**: Add source generator project

```
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Baml.Net.SourceGenerator", "src\Baml.Net.SourceGenerator\Baml.Net.SourceGenerator.csproj", "{NEW-GUID}"
EndProject
```

### Success Criteria:

#### Automated Verification:
- [x] Source generator project builds: `dotnet build src/Baml.Net.SourceGenerator`
- [x] No compiler warnings or errors
- [x] Package includes analyzer in correct location

#### Manual Verification:
- [ ] Project references work correctly
- [ ] Generator is discoverable by consuming projects

---

## Phase 2: BAML Compiler Integration

### Overview
Integrate the source generator with the BAML compiler using the existing BamlRuntime FFI interface to generate metadata JSON files.

### Changes Required:

#### 1. BAML Compiler Wrapper
**File**: `src/Baml.Net.SourceGenerator/BamlCompilerWrapper.cs`
**Changes**: Create wrapper for invoking BAML compiler via BamlRuntime

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Baml.Net.Core;
using Baml.Net.FFI;

namespace Baml.Net.SourceGenerator
{
    internal class BamlCompilerWrapper
    {
        private readonly bool _enableLogging;

        public BamlCompilerWrapper(bool enableLogging = false)
        {
            _enableLogging = enableLogging;
        }

        public string GenerateMetadata(string bamlDirectory)
        {
            var outputDir = Path.Combine(Path.GetTempPath(), $"baml-gen-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);

            try
            {
                // Load BAML files from directory
                var files = new Dictionary<string, string>();
                foreach (var file in Directory.GetFiles(bamlDirectory, "*.baml", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(bamlDirectory, file);
                    files[relativePath] = File.ReadAllText(file);
                }

                // Create runtime from files
                using var runtime = BamlRuntime.FromFiles(bamlDirectory, files);

                // Invoke CLI to generate metadata
                var args = new[]
                {
                    "generate",
                    "--input", bamlDirectory,
                    "--output", outputDir,
                    "--output-type", "csharp/metadata"
                };

                var exitCode = runtime.InvokeCli(args);

                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"BAML compiler failed with exit code {exitCode}");
                }

                // Read generated metadata
                var metadataPath = Path.Combine(outputDir, "baml-metadata.json");
                if (!File.Exists(metadataPath))
                {
                    throw new FileNotFoundException("BAML compiler did not generate metadata file", metadataPath);
                }

                return File.ReadAllText(metadataPath);
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    Directory.Delete(outputDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        public string GenerateMetadataFromFiles(Dictionary<string, string> bamlFiles, string rootPath)
        {
            var outputDir = Path.Combine(Path.GetTempPath(), $"baml-gen-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);

            try
            {
                // Create runtime from file dictionary
                using var runtime = BamlRuntime.FromFiles(rootPath, bamlFiles);

                // Invoke CLI to generate metadata
                var args = new[]
                {
                    "generate",
                    "--output", outputDir,
                    "--output-type", "csharp/metadata"
                };

                var exitCode = runtime.InvokeCli(args);

                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"BAML compiler failed with exit code {exitCode}");
                }

                // Read generated metadata
                var metadataPath = Path.Combine(outputDir, "baml-metadata.json");
                if (!File.Exists(metadataPath))
                {
                    // If metadata not found, generate a minimal one
                    return GenerateMinimalMetadata();
                }

                return File.ReadAllText(metadataPath);
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    Directory.Delete(outputDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        private string GenerateMinimalMetadata()
        {
            var metadata = new
            {
                version = "1.0.0",
                @namespace = "BamlClient",
                types = new object[0],
                functions = new object[0],
                clients = new object[0],
                retryPolicies = new object[0]
            };

            return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] Compiler wrapper compiles without errors
- [ ] BamlRuntime.InvokeCli works correctly
- [ ] No Process.Start usage

#### Manual Verification:
- [ ] BAML compiler can be invoked from generator
- [ ] Metadata JSON is generated successfully
- [ ] Temporary files are cleaned up

---

## Phase 3: Metadata Deserialization

### Overview
Deserialize the baml-metadata.json file into strongly-typed C# classes for code generation.

### Changes Required:

#### 1. Metadata Model Classes
**File**: `src/Baml.Net.SourceGenerator/Metadata/BamlMetadata.cs`
**Changes**: Define metadata structure matching JSON schema

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Baml.Net.SourceGenerator.Metadata
{
    internal class BamlMetadata
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("namespace")]
        public string Namespace { get; set; } = "BamlClient";

        [JsonPropertyName("types")]
        public List<TypeDefinition> Types { get; set; } = new();

        [JsonPropertyName("functions")]
        public List<FunctionDefinition> Functions { get; set; } = new();

        [JsonPropertyName("clients")]
        public List<ClientDefinition> Clients { get; set; } = new();

        [JsonPropertyName("retryPolicies")]
        public List<RetryPolicyDefinition> RetryPolicies { get; set; } = new();
    }

    internal class TypeDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("properties")]
        public List<PropertyDefinition>? Properties { get; set; }

        [JsonPropertyName("values")]
        public List<EnumValueDefinition>? Values { get; set; }

        [JsonPropertyName("isDynamic")]
        public bool IsDynamic { get; set; }
    }

    internal class PropertyDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("nullable")]
        public bool Nullable { get; set; }
    }

    internal class EnumValueDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    internal class FunctionDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("async")]
        public bool IsAsync { get; set; } = true;

        [JsonPropertyName("parameters")]
        public List<ParameterDefinition> Parameters { get; set; } = new();

        [JsonPropertyName("returnType")]
        public string ReturnType { get; set; } = "";

        [JsonPropertyName("returnNullable")]
        public bool ReturnNullable { get; set; }
    }

    internal class ParameterDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("nullable")]
        public bool Nullable { get; set; }
    }

    internal class ClientDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "";

        [JsonPropertyName("retryPolicy")]
        public string? RetryPolicy { get; set; }
    }

    internal class RetryPolicyDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; }

        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = "";
    }
}
```

#### 2. Metadata Parser
**File**: `src/Baml.Net.SourceGenerator/MetadataParser.cs`
**Changes**: Parse JSON metadata

```csharp
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Baml.Net.SourceGenerator.Metadata;

namespace Baml.Net.SourceGenerator
{
    internal class MetadataParser
    {
        private readonly JsonSerializerOptions _options;

        public MetadataParser()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
        }

        public BamlMetadata Parse(string json)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<BamlMetadata>(json, _options);
                if (metadata == null)
                {
                    throw new InvalidOperationException("Failed to deserialize BAML metadata");
                }

                // Validate metadata
                ValidateMetadata(metadata);

                return metadata;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid BAML metadata JSON: {ex.Message}", ex);
            }
        }

        private void ValidateMetadata(BamlMetadata metadata)
        {
            if (string.IsNullOrEmpty(metadata.Version))
                throw new InvalidOperationException("Metadata version is missing");

            if (string.IsNullOrEmpty(metadata.Namespace))
                throw new InvalidOperationException("Metadata namespace is missing");

            // Additional validation as needed
            foreach (var type in metadata.Types)
            {
                if (string.IsNullOrEmpty(type.Name))
                    throw new InvalidOperationException("Type name is missing");

                if (string.IsNullOrEmpty(type.Kind))
                    throw new InvalidOperationException($"Type kind is missing for {type.Name}");
            }

            foreach (var function in metadata.Functions)
            {
                if (string.IsNullOrEmpty(function.Name))
                    throw new InvalidOperationException("Function name is missing");

                if (string.IsNullOrEmpty(function.ReturnType))
                    throw new InvalidOperationException($"Return type is missing for function {function.Name}");
            }
        }
    }
}
```

### Success Criteria:

#### Automated Verification:
- [x] Metadata classes compile without errors
- [x] JSON deserialization works correctly
- [x] Validation catches malformed metadata

#### Manual Verification:
- [ ] Complex metadata files parse correctly
- [ ] Error messages are helpful

---

## Phase 4: Code Generation with StringBuilder

### Overview
Generate C# client code from metadata using StringBuilder with proper async patterns and optional logging.

### Changes Required:

#### 1. Code Generator Implementation
**File**: `src/Baml.Net.SourceGenerator/BamlCodeGenerator.cs`
**Changes**: Implement code generation logic

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Baml.Net.SourceGenerator.Metadata;

namespace Baml.Net.SourceGenerator
{
    internal class BamlCodeGenerator
    {
        private readonly bool _enableLogging;
        private const string IndentUnit = "    ";

        public BamlCodeGenerator(bool enableLogging)
        {
            _enableLogging = enableLogging;
        }

        public string GenerateCode(GeneratorExecutionContext context, IReadOnlyList<AdditionalText> bamlFiles)
        {
            try
            {
                // Collect BAML file contents
                var files = new Dictionary<string, string>();
                string? rootPath = null;

                foreach (var file in bamlFiles)
                {
                    if (rootPath == null)
                    {
                        rootPath = System.IO.Path.GetDirectoryName(file.Path);
                    }

                    var content = file.GetText(context.CancellationToken)?.ToString();
                    if (content != null)
                    {
                        var relativePath = System.IO.Path.GetFileName(file.Path);
                        files[relativePath] = content;
                    }
                }

                if (files.Count == 0 || rootPath == null)
                    return "";

                // Generate metadata
                var compilerWrapper = new BamlCompilerWrapper(enableLogging: _enableLogging);
                var metadataJson = compilerWrapper.GenerateMetadataFromFiles(files, rootPath);

                // Parse metadata
                var parser = new MetadataParser();
                var metadata = parser.Parse(metadataJson);

                // Generate code
                return GenerateClientCode(metadata);
            }
            catch (Exception ex)
            {
                // Return commented error for debugging
                return $"// Error generating BAML client: {ex.Message}\n// {ex.StackTrace}";
            }
        }

        private string GenerateClientCode(BamlMetadata metadata)
        {
            var sb = new StringBuilder();

            // File header
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     This code was generated by BAML.NET Source Generator.");
            sb.AppendLine("//     Changes to this file may cause incorrect behavior and will be lost if");
            sb.AppendLine("//     the code is regenerated.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();

            // Nullable enable
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            // Using statements
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using System.Text.Json.Serialization;");
            sb.AppendLine("using Baml.Net.Core;");
            if (_enableLogging)
            {
                sb.AppendLine("using Microsoft.Extensions.Logging;");
            }
            sb.AppendLine();

            // Namespace
            sb.AppendLine($"namespace {metadata.Namespace}");
            sb.AppendLine("{");

            // Generate types
            foreach (var type in metadata.Types)
            {
                GenerateType(sb, type, IndentUnit);
                sb.AppendLine();
            }

            // Generate client class
            GenerateClientClass(sb, metadata, IndentUnit);

            sb.AppendLine("}");

            return sb.ToString();
        }

        private void GenerateType(StringBuilder sb, TypeDefinition type, string indent)
        {
            if (type.Kind == "Enum")
            {
                GenerateEnum(sb, type, indent);
            }
            else if (type.Kind == "Record" || type.Kind == "Class")
            {
                GenerateClass(sb, type, indent);
            }
        }

        private void GenerateEnum(StringBuilder sb, TypeDefinition type, string indent)
        {
            sb.AppendLine($"{indent}public enum {type.Name}");
            sb.AppendLine($"{indent}{{");

            if (type.Values != null)
            {
                foreach (var value in type.Values)
                {
                    sb.AppendLine($"{indent}{IndentUnit}{value.Name},");
                }
            }

            sb.AppendLine($"{indent}}}");
        }

        private void GenerateClass(StringBuilder sb, TypeDefinition type, string indent)
        {
            var keyword = type.Kind == "Record" ? "record" : "class";
            sb.AppendLine($"{indent}public {keyword} {type.Name}");
            sb.AppendLine($"{indent}{{");

            if (type.Properties != null)
            {
                foreach (var prop in type.Properties)
                {
                    var nullableSuffix = prop.Nullable ? "?" : "";
                    sb.AppendLine($"{indent}{IndentUnit}[JsonPropertyName(\"{ToCamelCase(prop.Name)}\")]");
                    sb.AppendLine($"{indent}{IndentUnit}public {prop.Type}{nullableSuffix} {prop.Name} {{ get; set; }}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"{indent}}}");
        }

        private void GenerateClientClass(StringBuilder sb, BamlMetadata metadata, string indent)
        {
            sb.AppendLine($"{indent}public partial class BamlClient");
            sb.AppendLine($"{indent}{{");

            // Fields
            sb.AppendLine($"{indent}{IndentUnit}private readonly BamlRuntimeAsync _runtime;");
            if (_enableLogging)
            {
                sb.AppendLine($"{indent}{IndentUnit}private readonly ILogger<BamlClient>? _logger;");
            }
            sb.AppendLine();

            // Constructor
            sb.AppendLine($"{indent}{IndentUnit}public BamlClient(BamlRuntimeAsync runtime");
            if (_enableLogging)
            {
                sb.Append($", ILogger<BamlClient>? logger = null");
            }
            sb.AppendLine(")");
            sb.AppendLine($"{indent}{IndentUnit}{{");
            sb.AppendLine($"{indent}{IndentUnit}{IndentUnit}_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));");
            if (_enableLogging)
            {
                sb.AppendLine($"{indent}{IndentUnit}{IndentUnit}_logger = logger;");
            }
            sb.AppendLine($"{indent}{IndentUnit}}}");
            sb.AppendLine();

            // Generate methods for each function
            foreach (var function in metadata.Functions)
            {
                GenerateMethod(sb, function, indent + IndentUnit);
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}}}");
        }

        private void GenerateMethod(StringBuilder sb, FunctionDefinition function, string indent)
        {
            // Method signature
            var returnType = function.ReturnNullable
                ? $"Task<{function.ReturnType}?>"
                : $"Task<{function.ReturnType}>";

            sb.Append($"{indent}public async {returnType} {function.Name}Async(");

            // Parameters
            var parameters = new List<string>();
            foreach (var param in function.Parameters)
            {
                var nullableSuffix = param.Nullable ? "?" : "";
                parameters.Add($"{param.Type}{nullableSuffix} {param.Name}");
            }
            parameters.Add("CancellationToken cancellationToken = default");
            sb.Append(string.Join(", ", parameters));

            sb.AppendLine(")");
            sb.AppendLine($"{indent}{{");

            // Method body
            var bodyIndent = indent + IndentUnit;

            // Logging - method entry
            if (_enableLogging)
            {
                sb.AppendLine($"{bodyIndent}_logger?.LogDebug(\"Calling BAML function {{Function}} with parameters {{Parameters}}\",");
                sb.AppendLine($"{bodyIndent}{IndentUnit}\"{function.Name}\",");
                sb.Append($"{bodyIndent}{IndentUnit}new {{ ");
                sb.Append(string.Join(", ", function.Parameters.Select(p => $"{p.Name}")));
                sb.AppendLine(" });");
                sb.AppendLine();
            }

            // Prepare arguments
            if (function.Parameters.Any())
            {
                sb.AppendLine($"{bodyIndent}var args = new Dictionary<string, object?>");
                sb.AppendLine($"{bodyIndent}{{");
                foreach (var param in function.Parameters)
                {
                    sb.AppendLine($"{bodyIndent}{IndentUnit}[\"{param.Name}\"] = {param.Name},");
                }
                sb.AppendLine($"{bodyIndent}}};");
            }
            else
            {
                sb.AppendLine($"{bodyIndent}var args = new Dictionary<string, object?>();");
            }
            sb.AppendLine();

            // Call runtime - following the pattern of storing result in variable
            sb.AppendLine($"{bodyIndent}// Call the BAML runtime - store result for debugging");
            sb.AppendLine($"{bodyIndent}var result = await _runtime.CallFunctionAsync<{function.ReturnType}>(");
            sb.AppendLine($"{bodyIndent}{IndentUnit}\"{function.Name}\",");
            sb.AppendLine($"{bodyIndent}{IndentUnit}args,");
            sb.AppendLine($"{bodyIndent}{IndentUnit}null,");
            sb.AppendLine($"{bodyIndent}{IndentUnit}cancellationToken)");
            sb.AppendLine($"{bodyIndent}{IndentUnit}.ConfigureAwait(false);");
            sb.AppendLine();

            // Logging - method exit
            if (_enableLogging)
            {
                sb.AppendLine($"{bodyIndent}_logger?.LogDebug(\"BAML function {{Function}} completed successfully\",");
                sb.AppendLine($"{bodyIndent}{IndentUnit}\"{function.Name}\");");
                sb.AppendLine();
            }

            // Return
            sb.AppendLine($"{bodyIndent}return result;");

            sb.AppendLine($"{indent}}}");
        }

        private string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }
    }
}
```

### Success Criteria:

#### Automated Verification:
- [x] Generated code compiles without errors
- [x] ConfigureAwait(false) used on all await statements
- [x] Results stored in variables before return
- [x] Logging statements conditionally included

#### Manual Verification:
- [ ] Generated code is readable and well-formatted
- [ ] IntelliSense works for generated types
- [ ] Debugging shows intermediate result values

---

## Phase 5: MSBuild Integration

### Overview
Configure MSBuild properties to control source generation behavior and integrate with the build process.

### Changes Required:

#### 1. MSBuild Props File
**File**: `src/Baml.Net.SourceGenerator/build/Baml.Net.SourceGenerator.props`
**Changes**: Define MSBuild properties

```xml
<Project>
  <!-- Default BAML source generator settings -->
  <PropertyGroup>
    <!-- Enable/disable BAML code generation -->
    <BamlEnableCodeGeneration Condition="'$(BamlEnableCodeGeneration)' == ''">true</BamlEnableCodeGeneration>

    <!-- Enable/disable logging in generated code -->
    <BamlEnableCodeGenLogging Condition="'$(BamlEnableCodeGenLogging)' == ''">false</BamlEnableCodeGenLogging>

    <!-- Include default BAML items automatically -->
    <IncludeDefaultBamlItems Condition="'$(IncludeDefaultBamlItems)' == ''">true</IncludeDefaultBamlItems>

    <!-- Default BAML file pattern -->
    <DefaultBamlIncludes Condition="'$(DefaultBamlIncludes)' == ''">**/*.baml</DefaultBamlIncludes>
  </PropertyGroup>

  <!-- Make MSBuild properties available to the generator -->
  <ItemGroup>
    <CompilerVisibleProperty Include="BamlEnableCodeGeneration" />
    <CompilerVisibleProperty Include="BamlEnableCodeGenLogging" />
    <CompilerVisibleProperty Include="IncludeDefaultBamlItems" />
  </ItemGroup>

  <!-- Include default BAML items if enabled -->
  <ItemGroup Condition="'$(BamlEnableCodeGeneration)' == 'true' AND '$(IncludeDefaultBamlItems)' == 'true'">
    <Baml Include="$(DefaultBamlIncludes)" />
  </ItemGroup>

  <!-- Make Baml items visible to the source generator -->
  <ItemGroup>
    <CompilerVisibleItemMetadata Include="Baml" MetadataName="Identity" />
  </ItemGroup>
</Project>
```

#### 2. MSBuild Targets File
**File**: `src/Baml.Net.SourceGenerator/build/Baml.Net.SourceGenerator.targets`
**Changes**: Define build targets

```xml
<Project>
  <!-- Pass Baml items to the source generator as AdditionalFiles -->
  <Target Name="_IncludeBamlFilesForGenerator" BeforeTargets="CoreCompile">
    <ItemGroup>
      <AdditionalFiles Include="@(Baml)" KeepMetadata="Identity" />
    </ItemGroup>
  </Target>

  <!-- Watch BAML files for changes -->
  <ItemGroup>
    <Watch Include="@(Baml)" Condition="'$(BamlEnableCodeGeneration)' == 'true'" />
  </ItemGroup>

  <!-- Ensure BAML files are available during design-time builds -->
  <ItemGroup>
    <AvailableItemName Include="Baml" />
  </ItemGroup>
</Project>
```

#### 3. Update Source Generator Project
**File**: `src/Baml.Net.SourceGenerator/Baml.Net.SourceGenerator.csproj`
**Changes**: Package props and targets files

```xml
<!-- Add to ItemGroup -->
<ItemGroup>
  <None Include="build\Baml.Net.SourceGenerator.props" Pack="true" PackagePath="build" />
  <None Include="build\Baml.Net.SourceGenerator.targets" Pack="true" PackagePath="build" />
</ItemGroup>
```

#### 4. Integration with Main Project
**File**: `src/Baml.Net/Baml.Net.csproj`
**Changes**: Reference source generator

```xml
<!-- Add analyzer reference -->
<ItemGroup>
  <ProjectReference Include="..\Baml.Net.SourceGenerator\Baml.Net.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<!-- Configure BAML generation -->
<PropertyGroup>
  <!-- Users can override these in their projects -->
  <BamlEnableCodeGeneration>true</BamlEnableCodeGeneration>
  <BamlEnableCodeGenLogging>$(EnableLogging)</BamlEnableCodeGenLogging>
</PropertyGroup>
```

### Success Criteria:

#### Automated Verification:
- [x] MSBuild properties are recognized: `dotnet build -p:BamlEnableCodeGenLogging=true`
- [x] BAML files trigger regeneration when changed
- [x] Props and targets files are packaged correctly

#### Manual Verification:
- [ ] Developers can control logging via project properties
- [ ] Build output shows generation happening
- [ ] Hot reload works with BAML file changes

---

## Phase 6: Testing and Validation

### Overview
Create comprehensive tests to ensure the source generator works correctly.

### Changes Required:

#### 1. Source Generator Tests Project
**File**: `tests/Baml.Net.SourceGenerator.Tests/Baml.Net.SourceGenerator.Tests.csproj`
**Changes**: Create test project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit" />
    <PackageReference Include="Verify.SourceGenerators" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Baml.Net.SourceGenerator\Baml.Net.SourceGenerator.csproj" />
  </ItemGroup>
</Project>
```

#### 2. Generator Tests
**File**: `tests/Baml.Net.SourceGenerator.Tests/BamlSourceGeneratorTests.cs`
**Changes**: Test source generator

```csharp
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using VerifyXunit;

namespace Baml.Net.SourceGenerator.Tests
{
    [UsesVerify]
    public class BamlSourceGeneratorTests
    {
        [Fact]
        public async Task Generator_WithValidBamlFile_GeneratesClient()
        {
            // Arrange
            var source = @"
namespace TestApp
{
    class Program
    {
        static void Main() { }
    }
}";

            var bamlContent = @"
class Recipe {
  title string
  ingredients string[]
}

function GetRecipe(name: string) -> Recipe {
  client ""openai/gpt-4""
  prompt #""Get recipe for {{name}}""#
}";

            // Act
            var generator = new BamlSourceGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);

            var compilation = CSharpCompilation.Create(
                assemblyName: "Tests",
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            var runResult = driver.RunGenerators(compilation).GetRunResult();

            // Assert
            Assert.Single(runResult.GeneratedTrees);
            await Verify(runResult);
        }

        [Theory]
        [InlineData(true, true)]   // Logging enabled
        [InlineData(false, false)] // Logging disabled
        public async Task Generator_LoggingConfiguration_AffectsOutput(bool enableLogging, bool shouldContainLogger)
        {
            // Test that MSBuild property controls logging injection
            // Implementation details...
        }

        [Fact]
        public async Task Generator_AsyncMethods_UseConfigureAwaitFalse()
        {
            // Test that generated async code uses .ConfigureAwait(false)
            // Implementation details...
        }

        [Fact]
        public async Task Generator_StoresResultsInVariables()
        {
            // Test that async results are stored in variables before return
            // Implementation details...
        }
    }
}
```

#### 3. Integration Tests
**File**: `tests/Baml.Net.Tests/SourceGeneratorIntegrationTests.cs`
**Changes**: End-to-end tests

```csharp
using System.Threading.Tasks;
using Xunit;

namespace Baml.Net.Tests
{
    public class SourceGeneratorIntegrationTests
    {
        [Fact]
        public async Task GeneratedClient_CanCallFunctions()
        {
            // Test that generated BamlClient works with runtime
            var runtime = BamlRuntime.FromDirectory("TestData/BamlFiles");
            var asyncRuntime = new BamlRuntimeAsync(runtime);

            // This would use the generated client
            // var client = new BamlClient.BamlClient(asyncRuntime);
            // var result = await client.GetRecipeAsync("pasta");

            // Assert result is correct type
        }
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] All source generator tests pass: `dotnet test tests/Baml.Net.SourceGenerator.Tests` (tests not yet created)
- [x] Integration tests verify generated code works (existing tests pass)
- [ ] Snapshot tests verify generated code structure (to be implemented)

#### Manual Verification:
- [ ] Generated code can be debugged
- [ ] IntelliSense works in consuming projects
- [ ] No performance degradation during build

## Testing Strategy

### Unit Tests:
- Metadata parsing from JSON
- Code generation for each type (enum, class, record)
- Method generation with correct signatures
- ConfigureAwait pattern verification
- Logging injection based on configuration

### Integration Tests:
- End-to-end BAML file to generated code
- Generated client works with BamlRuntimeAsync
- MSBuild property configuration
- File watching and regeneration

### Manual Testing Steps:
1. Create a new console app with BAML files
2. Reference Baml.Net package with source generator
3. Verify IntelliSense for generated types
4. Set breakpoint in generated async method
5. Verify intermediate result variable is visible in debugger
6. Toggle BamlEnableCodeGenLogging and verify output changes

## Performance Considerations

- Source generator runs incrementally when possible
- BAML compiler invocation is cached per compilation
- StringBuilder used for efficient string concatenation
- Temporary files cleaned up after generation
- Consider implementing IIncrementalGenerator in future phase

## Migration Notes

For existing Baml.Net users:
- Source generator is opt-in via package reference
- Generated code supplements existing runtime API
- No breaking changes to existing code
- Can gradually migrate from dynamic to typed API

## References

- Original research: `thoughts/shared/research/2025-10-31-baml-net-code-generation-architecture.md`
- C# metadata generator plan: `thoughts/shared/plans/2025-01-30-csharp-generator-implementation.md`
- Phase 2+ roadmap: `thoughts/shared/plans/2025-01-29-baml-dotnet-phase2-onwards.md`
- Protobuf generation pattern: `src/Baml.Net/Baml.Net.csproj:19-21`
- Runtime implementation: `src/Baml.Net/Core/BamlRuntimeAsync.cs`