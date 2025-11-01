---
date: 2025-10-31T21:49:54-0700
researcher: Unknown
git_commit: 131193df5c418d874f3cafd3a75539728e0cb8cb
branch: main
repository: bamldotnet
topic: "BAML.NET Code Generation Architecture and Source Generator Implementation"
tags: [research, codebase, code-generation, source-generators, baml-net, metadata]
status: complete
last_updated: 2025-10-31
last_updated_by: Unknown
---

# Research: BAML.NET Code Generation Architecture and Source Generator Implementation

**Date**: 2025-10-31T21:49:54-0700
**Researcher**: Unknown
**Git Commit**: 131193df5c418d874f3cafd3a75539728e0cb8cb
**Branch**: main
**Repository**: bamldotnet

## Research Question
Next up what I need is adding a source generator to the BAML.NET solution which is watching BAML files within the C# project itself and if a BAML file is changed then it invokes the generator with BAML.NET and what it outputs as metadata reads that JSON file and then generates the code with the string builder. It's just types and methods. How does the current BAML.NET codebase work without looking into the BAML folder itself?

## Summary
The BAML.NET solution currently operates as a runtime-based architecture without generated client types or source generators. All BAML functions are invoked dynamically through `BamlRuntime` and `BamlRuntimeAsync` classes using string-based function names and dictionary arguments. The system communicates with a native Rust library (`baml_cffi`) via P/Invoke FFI using Google Protocol Buffers for serialization. While metadata generation infrastructure exists (producing `baml-metadata.json`), it is not yet consumed by any source generators. The codebase is well-prepared for source generator implementation with clear patterns for type conversion, async operations, and streaming.

## Detailed Findings

### Current Architecture (No Code Generation)

**Dynamic Runtime Invocation** (`src/Baml.Net/Core/BamlRuntime.cs` and `BamlRuntimeAsync.cs`)
- Functions are called by string name: `CallFunctionAsync("ExtractResume", args)`
- Arguments passed as `Dictionary<string, object>`
- Results returned as protobuf bytes, deserialized via JSON intermediate format
- No compile-time type safety for function names or arguments
- Generic methods `CallFunctionAsync<T>()` provide runtime type conversion

**FFI Communication Layer** (`src/Baml.Net/FFI/`)
- `BamlNative.cs`: P/Invoke declarations for native library calls
- `BamlNativeHelpers.cs`: Marshaling and memory management
- `BamlCallbackManager.cs`: Bridges native callbacks to C# async/await
- All data crosses FFI boundary as protobuf byte arrays
- Custom DLL resolver loads platform-specific native libraries from `runtimes/{rid}/native/`

### Existing Code Generation Infrastructure

**Protocol Buffer Generation** (`src/Baml.Net/Protobuf/`)
- `cffi.proto` defines message types for FFI communication
- `Grpc.Tools` NuGet package generates C# classes during build
- Generated code in `obj/Debug/net9.0/Protobuf/Cffi.cs`
- Provides strongly-typed protobuf messages: `CFFIValueHolder`, `CFFIFunctionArguments`, etc.
- Pattern shows MSBuild integration for code generation

**Type Conversion System** (`src/Baml.Net/Extensions/BamlValueExtensions.cs`)
- Extension methods convert between C# objects and protobuf messages
- `ToCFFI()`: C# object → protobuf (pattern matching on type)
- `ToObject()`: protobuf → C# object (switch on value case)
- Handles primitives, collections (Dictionary, List), and complex types
- Classes represented as Dictionary with `__type` field
- Enums represented as string values

### JSON Metadata Structure (Ready for Source Generation)

**Metadata Output** (`test-csharp-metadata/output/baml_client/baml-metadata.json`)
```json
{
  "version": "1.0.0",
  "namespace": "BamlClient",
  "types": [
    {
      "name": "Sentiment",
      "kind": "Enum",
      "values": [
        { "name": "Happy", "value": "Happy" },
        { "name": "Sad", "value": "Sad" }
      ],
      "isDynamic": false
    },
    {
      "name": "Recipe",
      "kind": "Record",
      "properties": [
        { "name": "Title", "type": "string", "nullable": false },
        { "name": "Ingredients", "type": "List<string>", "nullable": false },
        { "name": "Calories", "type": "int", "nullable": true }
      ],
      "isDynamic": false
    }
  ],
  "functions": [
    {
      "name": "AnalyzeSentiment",
      "async": true,
      "parameters": [
        { "name": "text", "type": "string", "nullable": false }
      ],
      "returnType": "Sentiment",
      "returnNullable": false
    }
  ],
  "clients": [
    {
      "name": "Gpt4",
      "provider": "openai",
      "retryPolicy": "MyRetryPolicy"
    }
  ],
  "retryPolicies": [
    {
      "name": "MyRetryPolicy",
      "maxRetries": 3,
      "strategy": "ExponentialBackoff",
      "options": {
        "delayMs": 1000,
        "multiplier": 2.0,
        "maxDelayMs": 10000
      }
    }
  ]
}
```

**Metadata Generation** (`baml/engine/generators/languages/csharp-metadata/`)
- Rust-based generator converts BAML IR to JSON metadata
- Type mappings: BAML types → C# types (string, int, double, bool, List<T>, Dictionary<K,V>)
- Name conventions: PascalCase for types, camelCase for parameters
- Preserves nullability information for C# nullable reference types
- Deterministic output using IndexMap for consistent ordering

### Solution Structure for Source Generator Integration

**Project Organization**
```
Baml.Net.sln
├── src/
│   └── Baml.Net/                  # Main library (target for generated code)
│       ├── Core/                  # Runtime classes
│       ├── FFI/                   # Native interop
│       ├── Extensions/            # Type conversion
│       └── Protobuf/              # Protocol buffer definitions
├── bindings/                      # Native library packages (6 platforms)
└── tests/
    └── Baml.Net.Tests/            # Test project
```

**MSBuild Configuration** (`Directory.Build.props` and `Directory.Packages.props`)
- Central package version management
- Shared properties across projects
- Ready for source generator package reference addition

### Patterns for Source Generator Implementation

**Pattern 1: MSBuild-Integrated Code Generation** (Already in use for protobuf)
```xml
<ItemGroup>
  <Protobuf Include="Protobuf\cffi.proto" GrpcServices="None" />
</ItemGroup>
```

**Pattern 2: Dynamic to Static Migration Path**
Current (Dynamic):
```csharp
var result = await runtime.CallFunctionAsync("ExtractResume",
    new Dictionary<string, object> { ["resume"] = resumeText });
```

Target (Generated):
```csharp
var client = new BamlClient(runtime);
var result = await client.ExtractResumeAsync(resumeText);
```

**Pattern 3: Type Generation from Metadata**
Metadata enum → C# enum:
```csharp
public enum Sentiment
{
    Happy,
    Sad,
    Angry
}
```

Metadata class → C# class:
```csharp
public class Recipe
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("ingredients")]
    public List<string> Ingredients { get; set; }

    [JsonPropertyName("calories")]
    public int? Calories { get; set; }
}
```

**Pattern 4: Client Method Generation**
```csharp
public partial class BamlClient
{
    private readonly BamlRuntimeAsync _runtime;

    public async Task<Sentiment> AnalyzeSentimentAsync(string text, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object> { ["text"] = text };
        return await _runtime.CallFunctionAsync<Sentiment>("AnalyzeSentiment", args, null, cancellationToken);
    }
}
```

### Key Implementation Considerations

**File Watching Requirements**
- Need to detect changes to `.baml` files in project
- Trigger metadata regeneration via BAML compiler
- Read updated `baml-metadata.json`
- Regenerate C# code using StringBuilder

**Integration Points**
1. **BAML Compiler Invocation**: Execute native BAML compiler to generate metadata
2. **Metadata Reading**: Deserialize `baml-metadata.json` to C# objects
3. **Code Generation**: Use StringBuilder to create C# source files
4. **Compilation**: Generated files included in C# compilation automatically

**Existing Infrastructure Support**
- Protobuf generation shows MSBuild integration pattern
- Type conversion layer ready for generated types
- Runtime async/streaming patterns established
- Test infrastructure validates functionality

## Code References
- `src/Baml.Net/Core/BamlRuntime.cs:42-88` - Runtime creation from BAML files
- `src/Baml.Net/Core/BamlRuntimeAsync.cs:53-172` - Async function invocation
- `src/Baml.Net/FFI/BamlNative.cs:140-148` - P/Invoke declarations
- `src/Baml.Net/Extensions/BamlValueExtensions.cs:19-58` - Type conversion
- `src/Baml.Net/Protobuf/cffi.proto:1-353` - Protobuf definitions
- `test-csharp-metadata/output/baml_client/baml-metadata.json:1-86` - Metadata format
- `baml/engine/generators/languages/csharp-metadata/src/metadata_schema.rs:186-337` - Metadata generation

## Architecture Documentation

### Current Data Flow (Runtime-Based)
1. User calls `BamlRuntimeAsync.CallFunctionAsync<T>(functionName, args)`
2. Args converted to protobuf via `ToFunctionArguments()`
3. Protobuf serialized to bytes
4. P/Invoke to native library with function name string
5. Native library executes BAML function
6. Result returned as protobuf bytes
7. Deserialized via JSON intermediate to type T

### Target Data Flow (With Source Generator)
1. Source generator reads `.baml` files
2. Invokes BAML compiler to generate `baml-metadata.json`
3. Source generator reads metadata
4. Generates C# classes and BamlClient with typed methods
5. User calls `client.AnalyzeSentimentAsync(text)`
6. Generated method delegates to existing runtime
7. Runtime handles FFI and deserialization

### Key Design Decisions
- **Metadata-driven**: Generator consumes JSON, not BAML directly
- **Separation of concerns**: BAML compiler handles BAML semantics, C# generator handles C# idioms
- **Backward compatible**: Generated code uses existing runtime infrastructure
- **Type safety**: Compile-time checking for function names and arguments
- **Async-first**: All generated methods return Task<T>

## Historical Context (from thoughts/)

**Existing Plans and Research**
- `thoughts/shared/plans/2025-01-30-csharp-generator-implementation.md` - Detailed source generator implementation plan with C# metadata classes
- `thoughts/shared/research/2025-01-30-csharp-support-implementation.md` - Research on language registration and code generation patterns
- `thoughts/baml_dotnet.md` - BAML .NET Integration Guide with compilation pipeline
- `thoughts/shared/plans/2025-01-29-baml-dotnet-phase2-onwards.md` - Phased implementation roadmap (Phase 1 runtime completed)

These documents confirm the project is actively working toward source generator implementation with Phase 1 (runtime) complete and Phase 2 (code generation) planned.

## Related Research
- `thoughts/shared/research/2025-01-30-csharp-support-implementation.md` - BAML language registration patterns
- `thoughts/shared/plans/2025-01-30-csharp-generator-implementation.md` - Source generator implementation details

## Open Questions
1. Should the source generator invoke the BAML compiler directly or expect pre-generated metadata?
2. How to handle incremental generation when only some BAML files change?
3. Should generated code be checked into source control or always regenerated?
4. How to integrate with IDE tooling for IntelliSense and refactoring?
5. What MSBuild properties/items should control generation behavior?