# BAML .NET Integration Guide

## Executive Summary

This document provides a comprehensive analysis of how to invoke the BAML compiler from a .NET console application and obtain its internal representation for generating a .NET BAML client. Based on extensive research of the BAML codebase, this guide presents multiple integration strategies and architectural insights.

**Requirements:**
- **.NET 9.0** (minimum and target framework)
- **Latest NuGet packages** (always verify versions at https://www.nuget.org/packages)
  - Google.Protobuf: 3.28.3 or later
  - Grpc.Tools: 2.66.0 or later

## Table of Contents

1. [BAML Architecture Overview](#baml-architecture-overview)
2. [Integration Approach: C FFI with P/Invoke](#integration-approach-c-ffi-with-p-invoke)
3. [TypeScript Parity Features](#typescript-parity-features)
4. [Detailed Implementation Guide](#detailed-implementation-guide)
5. [Internal Representations and Data Structures](#internal-representations-and-data-structures)
6. [Code Generation Pipeline](#code-generation-pipeline)
7. [C FFI Interface Reference](#c-ffi-interface-reference)
8. [Example Implementation Patterns](#example-implementation-patterns)
9. [Solution Structure and Package Architecture](#solution-structure-and-package-architecture)
10. [Building and Deployment](#building-and-deployment)

## BAML Architecture Overview

### Compilation Pipeline

The BAML compiler follows a multi-stage transformation pipeline:

```
BAML Source Files → Parser → AST → ParserDatabase → HIR → THIR → Bytecode → VM Execution
                                          ↓
                                  IntermediateRepr (IR)
                                          ↓
                              Language-Specific Code Generation
```

### Key Components

1. **Parser** (`internal-baml-ast`): Converts BAML source to Abstract Syntax Tree
2. **ParserDatabase** (`internal-baml-parser-database`): Validates and resolves names/types
3. **HIR** (High-level Intermediate Representation): Desugared representation
4. **THIR** (Typed HIR): Type-annotated HIR
5. **IntermediateRepr**: Final validated representation for code generation
6. **Bytecode Compiler**: Generates stack-based VM instructions
7. **Runtime**: Executes bytecode and manages LLM interactions

## Integration Approach: C FFI with P/Invoke

This document focuses exclusively on the C FFI approach, which provides the most comprehensive and performant integration with BAML.

### Overview

The C FFI (Foreign Function Interface) approach uses P/Invoke to call BAML's native Rust library directly from .NET code. This is the recommended and only approach covered in this guide.

**Key Benefits:**
- Direct access to all BAML compiler functionality
- Full control over compilation pipeline and runtime
- Access to internal representations (HIR, THIR, IR)
- Best performance with minimal overhead
- Similar implementation pattern to existing Go and TypeScript clients
- Complete feature parity with other language clients

**Technical Requirements:**
- Native library distribution for each platform
- P/Invoke declarations for FFI functions
- Protocol Buffers for data serialization
- Memory management for cross-boundary calls

**Implementation Pattern:**
```csharp
// P/Invoke declarations for native BAML functions
[DllImport("baml_cffi")]
public static extern IntPtr create_baml_runtime(
    string root_path,
    string src_files_json,
    string env_vars_json
);

[DllImport("baml_cffi")]
public static extern int invoke_runtime_cli(string[] args);

[DllImport("baml_cffi")]
public static extern void destroy_baml_runtime(IntPtr runtime);

// High-level wrapper providing idiomatic .NET API
public class BamlRuntime : IDisposable
{
    // Implementation details follow...
}
```

## TypeScript Parity Features

The TypeScript client implementation (via NAPI-RS) provides a comprehensive set of features that should be mirrored in the .NET implementation using C FFI with P/Invoke. This section identifies features from the TypeScript client and their natural .NET equivalents.

### Core Runtime Operations (.NET Mapping)

#### 1. Runtime Creation and Lifecycle

**TypeScript Pattern:**
```typescript
BamlRuntime.fromDirectory(directory: string, envVars: Record<string, string>)
BamlRuntime.fromFiles(rootPath: string, files: Record<string, string>, envVars: Record<string, string>)
runtime.reset(rootPath: string, files: Record<string, string>, envVars: Record<string, string>)
```

**.NET Equivalent:**
```csharp
public static BamlRuntime FromDirectory(string directory, Dictionary<string, string> envVars)
public static BamlRuntime FromFiles(string rootPath, Dictionary<string, string> files, Dictionary<string, string> envVars)
public void Reset(string rootPath, Dictionary<string, string> files, Dictionary<string, string> envVars)
```

#### 2. Function Execution Modes

**TypeScript:**
- `callFunction()` - Async execution
- `callFunctionSync()` - Synchronous execution
- `streamFunction()` - Async streaming
- `streamFunctionSync()` - Synchronous streaming initialization

**.NET Mapping:**
```csharp
public Task<FunctionResult> CallFunctionAsync(string functionName, Dictionary<string, object> args, RuntimeContext ctx, ...)
public FunctionResult CallFunction(string functionName, Dictionary<string, object> args, RuntimeContext ctx, ...)
public IAsyncEnumerable<TPartial> StreamFunctionAsync<TPartial, TFinal>(string functionName, Dictionary<string, object> args, RuntimeContext ctx, ...)
```

**Note:** .NET's `IAsyncEnumerable<T>` provides idiomatic streaming that maps naturally to TypeScript's async iterators.

#### 3. Streaming Infrastructure

**TypeScript Pattern:**
```typescript
class BamlStream<PartialOutputType, FinalOutputType> implements AsyncIterable {
  async *[Symbol.asyncIterator]() { ... }
  async getFinalResponse(): Promise<FinalOutputType>
  toStreamable(): ReadableStream<Uint8Array>  // Next.js integration
}
```

**.NET Mapping:**
```csharp
public class BamlStream<TPartial, TFinal> : IAsyncEnumerable<TPartial>
{
    public async IAsyncEnumerator<TPartial> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    public Task<TFinal> GetFinalResponseAsync()
    public Stream ToStream()  // For ASP.NET Core streaming
}
```

**Key Differences:**
- .NET uses `IAsyncEnumerable<T>` instead of Symbol.asyncIterator
- CancellationToken instead of AbortSignal
- Native Stream class for HTTP streaming

#### 4. Abort/Cancellation Mechanism

**TypeScript:**
```typescript
signal?: AbortSignal
```

**.NET Equivalent:**
```csharp
CancellationToken cancellationToken = default
```

**Implementation:** Map CancellationToken to Rust's Tripwire pattern through FFI.

#### 5. Context Management and Tracing

**TypeScript:**
```typescript
class BamlCtxManager {
  ctx: AsyncLocalStorage<RuntimeContextManager>
  cloneContext(): RuntimeContextManager
  startTrace(): BamlSpan
  endTrace(span: BamlSpan, response, error)
  traceFnAsync<T>(fn: () => Promise<T>): Promise<T>
}
```

**.NET Mapping:**
```csharp
public class BamlContextManager
{
    private static readonly AsyncLocal<RuntimeContext> _context = new();

    public RuntimeContext CloneContext()
    public BamlSpan StartTrace(string functionName, object args)
    public void EndTrace(BamlSpan span, object response, Exception error)
    public async Task<T> TraceFunctionAsync<T>(Func<Task<T>> fn)
}
```

**Key Point:** .NET's `AsyncLocal<T>` provides the same ambient context propagation as JavaScript's AsyncLocalStorage.

### Type System Features (.NET Mapping)

#### 6. Dynamic Type Builder

**TypeScript:**
```typescript
class TypeBuilder {
  string(): FieldType
  int(): FieldType
  float(): FieldType
  bool(): FieldType
  null(): FieldType
  list(inner: FieldType): FieldType
  optional(inner: FieldType): FieldType
  map(key: FieldType, value: FieldType): FieldType
  union(types: FieldType[]): FieldType
  literalString(value: string): FieldType
  literalInt(value: number): FieldType
  literalBool(value: boolean): FieldType
  getClass(name: string): ClassBuilder
  getEnum(name: string): EnumBuilder
}
```

**.NET Equivalent:**
```csharp
public class TypeBuilder
{
    public FieldType String()
    public FieldType Int()
    public FieldType Float()
    public FieldType Bool()
    public FieldType Null()
    public FieldType List(FieldType inner)
    public FieldType Optional(FieldType inner)
    public FieldType Map(FieldType key, FieldType value)
    public FieldType Union(params FieldType[] types)
    public FieldType LiteralString(string value)
    public FieldType LiteralInt(int value)
    public FieldType LiteralBool(bool value)
    public ClassBuilder GetClass(string name)
    public EnumBuilder GetEnum(string name)
}
```

#### 7. Class and Property Builders

**TypeScript:**
```typescript
class ClassBuilder {
  field(): FieldType
  property(name: string): ClassPropertyBuilder
  listProperties(): unknown[]
  removeProperty(name: string): void
}

class ClassPropertyBuilder {
  setType(fieldType: FieldType): ClassPropertyBuilder
  alias(alias?: string): ClassPropertyBuilder
  description(description?: string): ClassPropertyBuilder
}
```

**.NET Mapping:**
```csharp
public class ClassBuilder
{
    public FieldType Field()
    public ClassPropertyBuilder Property(string name)
    public IEnumerable<string> ListProperties()
    public void RemoveProperty(string name)
}

public class ClassPropertyBuilder
{
    public ClassPropertyBuilder SetType(FieldType fieldType)
    public ClassPropertyBuilder Alias(string alias)
    public ClassPropertyBuilder Description(string description)
}
```

#### 8. Enum Builders

**TypeScript:**
```typescript
class EnumBuilder {
  value(name: string): EnumValueBuilder
  alias(alias?: string): EnumBuilder
  field(): FieldType
}

class EnumValueBuilder {
  alias(alias?: string): EnumValueBuilder
  skip(skip?: boolean): EnumValueBuilder
  description(description?: string): EnumValueBuilder
}
```

**.NET Equivalent:**
```csharp
public class EnumBuilder
{
    public EnumValueBuilder Value(string name)
    public EnumBuilder Alias(string alias)
    public FieldType Field()
}

public class EnumValueBuilder
{
    public EnumValueBuilder Alias(string alias)
    public EnumValueBuilder Skip(bool skip = true)
    public EnumValueBuilder Description(string description)
}
```

### Media Type Handling (.NET Mapping)

#### 9. Media Type Classes

**TypeScript:**
```typescript
class BamlImage {
  static fromUrl(url: string, mediaType?: string): BamlImage
  static fromBase64(mediaType: string, base64: string): BamlImage
  isUrl(): boolean
  asUrl(): string
  asBase64(): [string, string]
  toJSON(): any
}
```

**.NET Equivalent:**
```csharp
public class BamlImage
{
    public static BamlImage FromUrl(string url, string mediaType = null)
    public static BamlImage FromBase64(string mediaType, string base64)
    public bool IsUrl()
    public string AsUrl()
    public (string base64, string mimeType) AsBase64()
    public object ToJson()
}
```

**Similar patterns for:** `BamlAudio`, `BamlPdf`, `BamlVideo`

### Logging and Observability (.NET Mapping)

#### 10. Log Collection

**TypeScript:**
```typescript
class Collector {
  constructor(name?: string)
  clear(): void
  get logs(): FunctionLog[]
  get last(): FunctionLog | null
  id(functionLogId: string): FunctionLog | null
  get usage(): Usage
}

class FunctionLog {
  get id(): string
  get functionName(): string
  get logType(): string
  get timing(): Timing
  get usage(): Usage
  get calls(): (LLMCall | LLMStreamCall)[]
  get rawLlmResponse(): string | null
  get tags(): unknown
  get selectedCall(): unknown
}
```

**.NET Mapping:**
```csharp
public class Collector
{
    public Collector(string name = null)
    public void Clear()
    public IReadOnlyList<FunctionLog> Logs { get; }
    public FunctionLog Last { get; }
    public FunctionLog GetById(string functionLogId)
    public Usage Usage { get; }
}

public class FunctionLog
{
    public string Id { get; }
    public string FunctionName { get; }
    public string LogType { get; }
    public Timing Timing { get; }
    public Usage Usage { get; }
    public IReadOnlyList<ILLMCall> Calls { get; }
    public string RawLlmResponse { get; }
    public Dictionary<string, object> Tags { get; }
    public ILLMCall SelectedCall { get; }
}
```

#### 11. LLM Call Details

**TypeScript:**
```typescript
interface LLMCall {
  get selected(): boolean
  get httpRequest(): HTTPRequest | null
  get httpResponse(): HTTPResponse | null
  get usage(): Usage | null
  get timing(): Timing
  get provider(): string
  get clientName(): string
}

interface LLMStreamCall extends LLMCall {
  sseResponses(): SSEResponse[] | null
  get timing(): StreamTiming
}
```

**.NET Mapping:**
```csharp
public interface ILLMCall
{
    bool Selected { get; }
    HttpRequest HttpRequest { get; }
    HttpResponse HttpResponse { get; }
    Usage Usage { get; }
    Timing Timing { get; }
    string Provider { get; }
    string ClientName { get; }
}

public class LLMStreamCall : ILLMCall
{
    public IReadOnlyList<SSEResponse> SseResponses { get; }
    public new StreamTiming Timing { get; }
}
```

#### 12. HTTP Request/Response Details

**TypeScript:**
```typescript
class HTTPRequest {
  get id(): string
  get body(): HTTPBody
  get url(): string
  get method(): string
  get headers(): object
}

class HTTPBody {
  raw(): ArrayBuffer
  text(): string
  json(): any
}
```

**.NET Mapping:**
```csharp
public class HttpRequest
{
    public string Id { get; }
    public HttpBody Body { get; }
    public string Url { get; }
    public string Method { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
}

public class HttpBody
{
    public byte[] Raw()
    public string Text()
    public T Json<T>()
}
```

### Client Configuration (.NET Mapping)

#### 13. Client Registry

**TypeScript:**
```typescript
class ClientRegistry {
  addLlmClient(name: string, provider: string, options: object, retryPolicy?: string): void
  setPrimary(primary: string): void
}
```

**.NET Equivalent:**
```csharp
public class ClientRegistry
{
    public void AddLlmClient(string name, string provider, Dictionary<string, object> options, string retryPolicy = null)
    public void SetPrimary(string primary)
}
```

### Event System (.NET Mapping)

#### 14. Event Watchers

**TypeScript:**
```typescript
// Passed to function calls
watchers?: {
  onVariable?: (event: VarEvent) => void
  onStream?: (event: StreamEvent) => void
  onBlock?: (event: BlockEvent) => void
  [functionName: string]: {
    onVariable?: (event: VarEvent) => void
    onStream?: (event: StreamEvent) => void
  }
}

interface VarEvent {
  variableName: string
  value: any
  timestamp: string
  functionName: string
}

interface StreamEvent {
  streamId: string
  eventType: string  // "start" | "update" | "end"
  value?: any
}

interface BlockEvent {
  blockLabel: string
  eventType: string
}
```

**.NET Mapping:**
```csharp
public class EventWatchers
{
    public event EventHandler<VarEvent> OnVariable;
    public event EventHandler<StreamEvent> OnStream;
    public event EventHandler<BlockEvent> OnBlock;

    public EventWatchers ForFunction(string functionName);
}

public class VarEvent : EventArgs
{
    public string VariableName { get; }
    public object Value { get; }
    public DateTime Timestamp { get; }
    public string FunctionName { get; }
}

public class StreamEvent : EventArgs
{
    public string StreamId { get; }
    public StreamEventType EventType { get; }  // enum: Start, Update, End
    public object Value { get; }
}

public class BlockEvent : EventArgs
{
    public string BlockLabel { get; }
    public string EventType { get; }
}
```

**Alternative:** Use `IObservable<T>` for reactive patterns:
```csharp
public class EventWatchers
{
    public IObservable<VarEvent> Variables { get; }
    public IObservable<StreamEvent> Streams { get; }
    public IObservable<BlockEvent> Blocks { get; }
}
```

### Runtime Information (.NET Mapping)

#### 15. Version and Diagnostics

**TypeScript:**
```typescript
get_version(): string
setLogLevel(level: string): void
getLogLevel(): string
setLogJsonMode(useJson: boolean): void
setLogMaxChunkLength(length: number): void
runtime.flush(): void
runtime.drainStats(): TraceStats
```

**.NET Equivalent:**
```csharp
public static class BamlRuntime
{
    public static string GetVersion()
    public static void SetLogLevel(string level)
    public static string GetLogLevel()
    public static void SetLogJsonMode(bool useJson)
    public static void SetLogMaxChunkLength(int length)

    public void Flush()
    public TraceStats DrainStats()
}

public class TraceStats
{
    public int Failed { get; }
    public int Started { get; }
    public int Finalized { get; }
    public int Submitted { get; }
    public int Sent { get; }
    public int Done { get; }
    public string ToJson()
}
```

### Advanced Features (.NET Mapping)

#### 16. Request Building (For Custom HTTP Clients)

**TypeScript:**
```typescript
runtime.buildRequest(functionName: string, args: object, ctx: RuntimeContextManager,
                     tb: TypeBuilder, cb: ClientRegistry, stream: boolean, envVars: object): Promise<HTTPRequest>
runtime.buildRequestSync(...): HTTPRequest
```

**.NET Equivalent:**
```csharp
public Task<HttpRequest> BuildRequestAsync(string functionName, Dictionary<string, object> args,
                                           RuntimeContext ctx, TypeBuilder tb, ClientRegistry cb,
                                           bool stream, Dictionary<string, string> envVars)
public HttpRequest BuildRequest(...) // Sync version
```

#### 17. LLM Response Parsing

**TypeScript:**
```typescript
runtime.parseLlmResponse(functionName: string, llmResponse: string, allowPartials: boolean,
                        ctx: RuntimeContextManager, tb: TypeBuilder, cb: ClientRegistry, envVars: object): any
```

**.NET Equivalent:**
```csharp
public T ParseLlmResponse<T>(string functionName, string llmResponse, bool allowPartials,
                             RuntimeContext ctx, TypeBuilder tb, ClientRegistry cb,
                             Dictionary<string, string> envVars)
```

### Features NOT Mapped (Too JavaScript-Specific)

The following TypeScript features are **NOT** included as they don't map naturally to .NET:

1. **WASI Fallback**: TypeScript loads WebAssembly when native bindings unavailable - .NET will always use native binaries
2. **Platform-Specific Module Loading**: TypeScript has complex NPM package resolution - .NET uses standard RID-based native library loading
3. **process.platform Detection**: .NET uses `RuntimeInformation` class
4. **AsyncLocalStorage Implementation Details**: .NET's `AsyncLocal<T>` is built-in
5. **Symbol.asyncIterator**: Replaced with `IAsyncEnumerable<T>`
6. **Threadsafe Function Pattern**: .NET uses Task-based async and SynchronizationContext
7. **External<T> Wrapping**: C# doesn't need this pattern; P/Invoke handles memory management differently

### Summary of Natural .NET Mappings

| TypeScript Pattern | .NET Equivalent | Notes |
|-------------------|-----------------|-------|
| `Promise<T>` | `Task<T>` | Direct mapping |
| `async/await` | `async/await` | Identical syntax |
| `AsyncLocalStorage<T>` | `AsyncLocal<T>` | Built-in ambient context |
| `AbortSignal` | `CancellationToken` | Standard cancellation |
| `async *[Symbol.asyncIterator]` | `IAsyncEnumerable<T>` | Async streams |
| `Record<K, V>` | `Dictionary<K, V>` | Hash table |
| `Array<T>` | `List<T>` or `T[]` | Collections |
| `{ [key: string]: T }` | `Dictionary<string, T>` | Index signature |
| Optional parameters `?` | `= null` default | Nullable types |
| Union types `A \| B` | `object` or discriminated unions | Type system difference |
| Callbacks | `Action<T>` / `Func<T>` / Events | Delegates/events |
| `ReadableStream` | `Stream` or `IAsyncEnumerable<T>` | Streaming data |
| Error classes | Exception classes | Error handling |

## Detailed Implementation Guide

#### Step 1: Native Library Setup

The BAML C FFI library needs to be built and distributed with your .NET application.

**Library Names by Platform:**
- Windows: `baml_cffi-x86_64-pc-windows-msvc.dll`
- macOS Intel: `libbaml_cffi-x86_64-apple-darwin.dylib`
- macOS ARM: `libbaml_cffi-aarch64-apple-darwin.dylib`
- Linux: `libbaml_cffi-x86_64-unknown-linux-gnu.so`

**Building the Native Library:**
```bash
cd baml/engine/language_client_cffi
cargo build --release
# Output in target/release/
```

#### Step 2: P/Invoke Declarations

Create C# bindings for the BAML FFI functions:

```csharp
using System;
using System.Runtime.InteropServices;

public static class BamlNative
{
    private const string LibraryName = "baml_cffi";

    // Runtime Management
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr create_baml_runtime(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string root_path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string src_files_json,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string env_vars_json
    );

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void destroy_baml_runtime(IntPtr runtime);

    // CLI Invocation
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int invoke_runtime_cli(
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPUTF8Str)]
        string[] args,
        int argc
    );

    // Function Execution
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr call_function_from_c(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string function_name,
        IntPtr args,
        int args_len,
        uint callback_id
    );

    // Memory Management
    [StructLayout(LayoutKind.Sequential)]
    public struct Buffer
    {
        public IntPtr data;
        public UIntPtr len;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_buffer(Buffer buffer);

    // Version Info
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr version();
}
```

#### Step 3: Create Runtime Wrapper

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

public class BamlRuntime : IDisposable
{
    private IntPtr _runtimePtr;
    private bool _disposed;

    public BamlRuntime(string rootPath, Dictionary<string, string> sourceFiles, Dictionary<string, string> envVars = null)
    {
        var filesJson = JsonSerializer.Serialize(sourceFiles);
        var envJson = JsonSerializer.Serialize(envVars ?? new Dictionary<string, string>());

        _runtimePtr = BamlNative.create_baml_runtime(rootPath, filesJson, envJson);
        if (_runtimePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create BAML runtime");
        }
    }

    public static BamlRuntime FromDirectory(string directory, Dictionary<string, string> envVars = null)
    {
        var files = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(directory, "*.baml", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(directory, file);
            files[relativePath] = File.ReadAllText(file);
        }
        return new BamlRuntime(directory, files, envVars);
    }

    public int InvokeCli(params string[] args)
    {
        return BamlNative.invoke_runtime_cli(args, args.Length);
    }

    public void Dispose()
    {
        if (!_disposed && _runtimePtr != IntPtr.Zero)
        {
            BamlNative.destroy_baml_runtime(_runtimePtr);
            _runtimePtr = IntPtr.Zero;
            _disposed = true;
        }
    }
}
```

#### Step 4: Protocol Buffers Integration (Foundation of Type System)

**Key Insight:** The C FFI interface uses Protocol Buffers (`cffi.proto`) to encode the entire BAML type system. By generating C# classes from this proto file, we get the complete type system automatically.

**Setup Steps:**

1. **Copy the proto file:**
   ```bash
   cp baml/engine/language_client_cffi/types/cffi.proto ./Baml.Cffi/
   ```

2. **Generate C# classes:**
   ```bash
   protoc --csharp_out=./Baml.Cffi/ cffi.proto
   ```

   This generates classes for:
   - `CFFIValueHolder` - Container for all BAML values
   - `CFFIFieldTypeHolder` - Type definitions
   - `CFFIValueClass`, `CFFIValueEnum`, `CFFIValueList`, `CFFIValueMap` - Structured types
   - `CFFIFunctionArguments` - Function parameter container
   - `CFFIClientRegistry` - Client configuration
   - All supporting messages

3. **Install NuGet packages:**
   ```bash
   # Check latest versions at https://www.nuget.org/packages before installing
   dotnet add package Google.Protobuf --version 3.28.3
   dotnet add package Grpc.Tools --version 2.66.0  # For build-time code generation
   ```

**Using Generated Types:**

The generated protobuf classes form the **foundation** of the type system. You interact with them directly:

```csharp
using Google.Protobuf;
using Baml.Cffi;  // Generated from cffi.proto

// Creating a BAML value
var holder = new CFFIValueHolder
{
    StringValue = "Hello"  // Auto-generated property
};

// Complex types
var classValue = new CFFIValueHolder
{
    ClassValue = new CFFIValueClass
    {
        Name = new CFFITypeName { Name = "User", Namespace = CFFITypeNamespace.Types },
        Fields =
        {
            new CFFIMapEntry
            {
                Key = "name",
                Value = new CFFIValueHolder { StringValue = "Alice" }
            }
        }
    }
};

// Serialization to bytes for FFI
byte[] data = holder.ToByteArray();

// Deserialization from FFI
var result = CFFIValueHolder.Parser.ParseFrom(data);
```

**Protobuf-First Architecture:**

```
┌─────────────────────────────────────┐
│  High-Level .NET API                │
│  (BamlRuntime, BamlImage, etc.)     │
└────────────┬────────────────────────┘
             │
             │ Uses/Wraps
             ↓
┌─────────────────────────────────────┐
│  Generated Protobuf Classes         │
│  (CFFIValueHolder, CFFIFieldType)   │  ← Generated from cffi.proto
└────────────┬────────────────────────┘
             │
             │ Serializes to bytes
             ↓
┌─────────────────────────────────────┐
│  P/Invoke FFI Boundary              │
│  (IntPtr, byte[], size_t)           │
└────────────┬────────────────────────┘
             │
             ↓
       Rust C FFI Layer
```

**Benefits of Protobuf-First Approach:**

1. **Type Safety**: All BAML types are strongly typed in C#
2. **Automatic Updates**: Regenerate from proto when BAML updates
3. **Zero Manual Marshalling**: Protobuf handles serialization
4. **Performance**: Binary format is compact and fast
5. **Versioning**: Protobuf handles forward/backward compatibility

**Type Conversion Helper:**

Build a thin wrapper layer for convenience:

```csharp
public static class BamlValueExtensions
{
    // Convert C# object → CFFIValueHolder
    public static CFFIValueHolder ToCFFI(this object value)
    {
        return value switch
        {
            string s => new CFFIValueHolder { StringValue = s },
            int i => new CFFIValueHolder { IntValue = i },
            long l => new CFFIValueHolder { IntValue = l },
            double d => new CFFIValueHolder { FloatValue = d },
            bool b => new CFFIValueHolder { BoolValue = b },
            null => new CFFIValueHolder { NullValue = new CFFIValueNull() },
            Dictionary<string, object> dict => new CFFIValueHolder
            {
                MapValue = CreateMapValue(dict)
            },
            IEnumerable<object> list => new CFFIValueHolder
            {
                ListValue = CreateListValue(list)
            },
            _ => throw new NotSupportedException($"Type {value.GetType()} not supported")
        };
    }

    // Convert CFFIValueHolder → C# object
    public static object ToObject(this CFFIValueHolder holder)
    {
        return holder.ValueCase switch
        {
            CFFIValueHolder.ValueOneofCase.StringValue => holder.StringValue,
            CFFIValueHolder.ValueOneofCase.IntValue => holder.IntValue,
            CFFIValueHolder.ValueOneofCase.FloatValue => holder.FloatValue,
            CFFIValueHolder.ValueOneofCase.BoolValue => holder.BoolValue,
            CFFIValueHolder.ValueOneofCase.NullValue => null,
            CFFIValueHolder.ValueOneofCase.MapValue => ConvertMapValue(holder.MapValue),
            CFFIValueHolder.ValueOneofCase.ListValue => ConvertListValue(holder.ListValue),
            CFFIValueHolder.ValueOneofCase.ClassValue => ConvertClassValue(holder.ClassValue),
            CFFIValueHolder.ValueOneofCase.EnumValue => ConvertEnumValue(holder.EnumValue),
            _ => throw new NotSupportedException($"Value case {holder.ValueCase} not supported")
        };
    }
}
```

## Internal Representations and Data Structures

### Abstract Syntax Tree (AST)

The AST is the first representation after parsing:

```rust
pub struct Ast {
    pub tops: Vec<Top>,  // All top-level declarations
}

pub enum Top {
    Enum(TypeExpressionBlock),
    Class(TypeExpressionBlock),
    Function(ValueExprBlock),
    Client(ValueExprBlock),
    // ... other variants
}
```

### High-level Intermediate Representation (HIR)

HIR is a desugared representation suitable for compilation:

```rust
pub struct Hir {
    pub expr_functions: Vec<ExprFunction>,
    pub llm_functions: Vec<LlmFunction>,
    pub classes: Vec<Class>,
    pub enums: Vec<Enum>,
    pub global_assignments: BamlMap<String, GlobalAssignment>,
}
```

Key characteristics:
- For loops are converted to while loops
- Syntactic sugar is removed
- Source spans preserved for error reporting

### Typed HIR (THIR)

THIR adds type information to HIR:

```rust
pub struct THir<T> {
    pub expr_functions: Vec<ExprFunction<T>>,
    pub llm_functions: Vec<LlmFunction>,
    pub global_assignments: BamlMap<String, GlobalAssignment<T>>,
    pub classes: BamlMap<String, Class<T>>,
    pub enums: BamlMap<String, Enum>,
}

// T is typically (Span, Option<TypeIR>) for type metadata
```

### IntermediateRepr (IR)

The IR is the final validated representation used for code generation:

```rust
pub struct IntermediateRepr {
    pub enums: Vec<Node<Enum>>,
    pub classes: Vec<Node<Class>>,
    pub type_aliases: Vec<Node<TypeAlias>>,
    pub functions: Vec<Node<Function>>,
    pub expr_fns: Vec<Node<ExprFunction>>,
    pub clients: Vec<Node<Client>>,
    pub retry_policies: Vec<Node<RetryPolicy>>,
    pub template_strings: Vec<Node<TemplateString>>,
    pub configuration: Configuration,
}
```

## Code Generation Pipeline

### Creating a .NET Code Generator

To implement a .NET code generator, you would need to:

1. **Implement the LanguageFeatures trait** (in Rust):

```rust
use generators_utils::LanguageFeatures;

#[derive(Default)]
pub struct DotNetLanguageFeatures;

impl LanguageFeatures for DotNetLanguageFeatures {
    const CONTENT_PREFIX: &'static str = r#"
// This file was generated by BAML: please do not edit it.
// Install the BAML NuGet package to use this generated code.
    "#;

    fn name() -> &'static str {
        "dotnet"
    }

    fn generate_sdk_files(
        &self,
        collector: &mut FileCollector<Self>,
        ir: Arc<IntermediateRepr>,
        args: &GeneratorArgs,
    ) -> Result<(), anyhow::Error> {
        // Generate C# files
        collector.add_file("BamlClient.cs", generate_client(&ir)?)?;
        collector.add_file("Types.cs", generate_types(&ir)?)?;
        collector.add_file("Runtime.cs", generate_runtime(&ir)?)?;

        Ok(())
    }
}
```

2. **Create code generation templates**:

```csharp
// Template for generated types
namespace Baml.Generated
{
    public class {{ class.name }}
    {
        {% for field in class.fields %}
        public {{ field.type | to_csharp_type }} {{ field.name | to_pascal_case }} { get; set; }
        {% endfor %}
    }

    public enum {{ enum.name }}
    {
        {% for variant in enum.variants %}
        {{ variant.name }},
        {% endfor %}
    }
}
```

3. **Register the generator** in the main dispatch function:

```rust
// In generators_lib/src/lib.rs
GeneratorOutputType::DotNet => {
    use generators_dotnet::DotNetLanguageFeatures;
    let features = DotNetLanguageFeatures;
    features.generate_sdk(ir, gen)?
}
```

## C FFI Interface Reference

### Core Functions

#### Runtime Management

```c
// Create a new BAML runtime
const void* create_baml_runtime(
    const char* root_path,
    const char* src_files_json,  // JSON map of path -> content
    const char* env_vars_json    // JSON map of env vars
);

// Destroy a runtime instance
void destroy_baml_runtime(const void* runtime);

// Get version information
const char* version(void);
```

#### CLI Invocation

```c
// Invoke the BAML CLI with arguments
int invoke_runtime_cli(const char* const* args);
```

#### Function Execution

```c
// Execute a BAML function
const void* call_function_from_c(
    const void* runtime,
    const char* function_name,
    const uint8_t* args,        // Protobuf-encoded arguments
    size_t args_len,
    uint32_t callback_id
);

// Execute with streaming
const void* call_function_stream_from_c(
    const void* runtime,
    const char* function_name,
    const uint8_t* args,
    size_t args_len,
    uint32_t callback_id,
    OnTickCallbackFn on_tick
);
```

#### Memory Management

```c
struct Buffer {
    uint8_t* data;
    size_t len;
};

void free_buffer(struct Buffer buf);
```

### Data Serialization Format

**All data exchange uses Protocol Buffers** defined in `baml/engine/language_client_cffi/types/cffi.proto`.

**Key Message Types** (generate C# classes via protoc):

#### CFFIValueHolder - Universal Value Container
```protobuf
message CFFIValueHolder {
  oneof value {
    CFFIValueNull null_value = 2;
    string string_value = 3;
    int64 int_value = 4;
    double float_value = 5;
    bool bool_value = 6;
    CFFIValueList list_value = 7;
    CFFIValueMap map_value = 8;
    CFFIValueClass class_value = 9;
    CFFIValueEnum enum_value = 10;
    CFFIValueRawObject object_value = 11;
    CFFIValueTuple tuple_value = 12;
    CFFIValueUnionVariant union_variant_value = 13;
    CFFIValueChecked checked_value = 14;
    CFFIValueStreamingState streaming_state_value = 15;
  }
  CFFIFieldTypeHolder type = 16;
}
```

**.NET Usage:**
```csharp
// Protoc generates this as:
public class CFFIValueHolder : IMessage<CFFIValueHolder>
{
    public enum ValueOneofCase { None, NullValue, StringValue, IntValue, ... }

    public ValueOneofCase ValueCase { get; }
    public string StringValue { get; set; }
    public long IntValue { get; set; }
    public double FloatValue { get; set; }
    // ... other properties
}
```

#### CFFIFunctionArguments - Function Call Parameters
```protobuf
message CFFIFunctionArguments {
  map<string, CFFIValueHolder> kwargs = 1;
  CFFIClientRegistry client_registry = 2;
  map<string, string> env_vars = 3;
}
```

**.NET Usage:**
```csharp
var args = new CFFIFunctionArguments();
args.Kwargs.Add("prompt", new CFFIValueHolder { StringValue = "Hello" });
args.EnvVars.Add("OPENAI_API_KEY", apiKey);

byte[] serialized = args.ToByteArray();  // Send to FFI
```

#### CFFIFieldTypeHolder - Type Definitions
```protobuf
message CFFIFieldTypeHolder {
  oneof type {
    CFFIFieldTypeString string_type = 1;
    CFFIFieldTypeInt int_type = 2;
    CFFIFieldTypeFloat float_type = 3;
    CFFIFieldTypeBool bool_type = 4;
    CFFIFieldTypeNull null_type = 5;
    CFFIFieldTypeLiteral literal_type = 6;
    CFFIFieldTypeMedia media_type = 7;
    CFFIFieldTypeEnum enum_type = 8;
    CFFIFieldTypeClass class_type = 9;
    CFFIFieldTypeList list_type = 11;
    CFFIFieldTypeMap map_type = 12;
    CFFIFieldTypeOptional optional_type = 15;
    // ... more types
  }
}
```

**.NET Usage:**
```csharp
// String type
var stringType = new CFFIFieldTypeHolder
{
    StringType = new CFFIFieldTypeString()
};

// List of strings
var listType = new CFFIFieldTypeHolder
{
    ListType = new CFFIFieldTypeList
    {
        InnerType = stringType
    }
};
```

#### Media Types
```protobuf
enum MediaTypeEnum {
  IMAGE = 0;
  AUDIO = 1;
  PDF = 2;
  VIDEO = 3;
}

message CFFIRawObject {
  string media_type = 1;
  MediaTypeEnum base_type = 2;
  oneof content {
    string url = 3;
    bytes base64 = 4;
  }
}
```

**.NET Usage:**
```csharp
var imageValue = new CFFIValueHolder
{
    ObjectValue = new CFFIValueRawObject
    {
        Object = new CFFIRawObject.ObjectOneofCase.Media
        {
            Media = new CFFIRawObject
            {
                BaseType = MediaTypeEnum.Image,
                MediaType = "image/png",
                Url = "https://example.com/image.png"
            }
        }
    }
};
```

**Complete Proto File Location:**
- Source: `baml/engine/language_client_cffi/types/cffi.proto`
- All 50+ message types defined there
- Generate C# with: `protoc --csharp_out=. cffi.proto`

## Example Implementation Patterns

### Pattern 1: Basic Runtime Usage

```csharp
public class BamlBasicUsage
{
    public async Task<string> ExecuteBamlFunctionAsync()
    {
        // Load BAML runtime from directory
        using var runtime = BamlRuntime.FromDirectory("./baml_src",
            new Dictionary<string, string> { ["OPENAI_API_KEY"] = apiKey });

        // Create context manager
        var ctx = runtime.CreateContextManager();

        // Prepare arguments
        var args = new Dictionary<string, object>
        {
            ["prompt"] = "Generate a creative story",
            ["max_tokens"] = 500
        };

        // Call BAML function
        var result = await runtime.CallFunctionAsync(
            "GenerateStory",    // Function name defined in BAML
            args,
            ctx,
            null,              // TypeBuilder (optional)
            null,              // ClientRegistry (optional)
            new[] { new Collector() },  // Log collectors
            new Dictionary<string, string>(),  // Tags
            new Dictionary<string, string>()   // Environment variables
        );

        // Parse result
        return result.Parsed<string>();
    }
}
```

### Pattern 2: Streaming Responses

```csharp
public class BamlStreamingExample
{
    public async IAsyncEnumerable<string> StreamResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var runtime = BamlRuntime.FromDirectory("./baml_src", envVars);
        var ctx = runtime.CreateContextManager();

        // Create stream
        var stream = runtime.StreamFunctionAsync<string, string>(
            "StreamingFunction",
            args,
            ctx,
            cancellationToken: cancellationToken
        );

        // Iterate over partial results
        await foreach (var partial in stream.WithCancellation(cancellationToken))
        {
            yield return partial;
        }

        // Get final result if needed
        var final = await stream.GetFinalResponseAsync();
    }
}
```

### Pattern 3: Dynamic Type Building

```csharp
public class BamlDynamicTypes
{
    private readonly TypeBuilder _typeBuilder;
    private readonly BamlRuntime _runtime;

    public BamlDynamicTypes()
    {
        _runtime = BamlRuntime.FromDirectory("./baml_src", envVars);
        _typeBuilder = new TypeBuilder();

        // Define a dynamic class
        var userClass = _typeBuilder.GetClass("User");
        userClass.Property("name").SetType(_typeBuilder.String());
        userClass.Property("age").SetType(_typeBuilder.Int());
        userClass.Property("email").SetType(_typeBuilder.String());

        // Define a dynamic enum
        var statusEnum = _typeBuilder.GetEnum("Status");
        statusEnum.Value("ACTIVE");
        statusEnum.Value("INACTIVE");
        statusEnum.Value("PENDING");
    }

    public async Task<T> CallWithDynamicTypesAsync<T>(string functionName, object args)
    {
        var ctx = _runtime.CreateContextManager();

        var result = await _runtime.CallFunctionAsync(
            functionName,
            args,
            ctx,
            _typeBuilder,  // Pass the type builder
            null,
            new[] { new Collector() },
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        return result.Parsed<T>();
    }
}
```

## Solution Structure and Package Architecture

### Overview

The .NET BAML integration is implemented as a **separate solution** outside the BAML source code, following the standard .NET pattern for native dependencies with n+1 NuGet packages:

- **Target Framework**: .NET 9.0 only
- **1 Main Package**: Contains all managed C# code (Baml.Net)
- **N Native Packages**: Platform-specific packages containing native BAML binaries (Baml.Net.Native.{rid})

**NuGet Package Versions:**
Always use the latest stable versions. Verify at https://www.nuget.org/packages:
- Google.Protobuf: 3.28.3+ (check for latest)
- Grpc.Tools: 2.66.0+ (check for latest)

### Complete Solution Structure

```
Baml.Net/                                    # Root of separate .NET solution
├── Baml.Net.sln                           # Solution file
├── README.md
├── LICENSE
├── Directory.Build.props                   # Global MSBuild properties
├── NuGet.config                           # NuGet configuration
├── .gitignore
│
├── src/
│   ├── Baml.Net/                         # Main managed library project
│   │   ├── Baml.Net.csproj               # Main package project
│   │   ├── Baml.Net.nuspec               # NuGet spec (optional, can use csproj)
│   │   │
│   │   ├── Core/                         # Core runtime implementation
│   │   │   ├── BamlRuntime.cs
│   │   │   ├── RuntimeContext.cs
│   │   │   ├── BamlContextManager.cs
│   │   │   └── FunctionResult.cs
│   │   │
│   │   ├── FFI/                          # P/Invoke declarations
│   │   │   ├── NativeMethods.cs          # All P/Invoke signatures
│   │   │   ├── NativeLibrary.cs          # Platform detection & loading
│   │   │   └── MemoryManagement.cs       # Buffer management
│   │   │
│   │   ├── Protobuf/                     # Generated protobuf classes
│   │   │   ├── cffi.proto                # Copy of BAML proto definition
│   │   │   ├── Cffi.cs                   # Generated by protoc
│   │   │   └── generate.sh               # Script to regenerate
│   │   │
│   │   ├── Types/                        # High-level type wrappers
│   │   │   ├── BamlImage.cs
│   │   │   ├── BamlAudio.cs
│   │   │   ├── BamlPdf.cs
│   │   │   ├── BamlVideo.cs
│   │   │   ├── TypeBuilder.cs
│   │   │   ├── ClassBuilder.cs
│   │   │   ├── EnumBuilder.cs
│   │   │   └── FieldType.cs
│   │   │
│   │   ├── Streaming/                    # Streaming implementation
│   │   │   ├── BamlStream.cs             # IAsyncEnumerable implementation
│   │   │   ├── StreamEvent.cs
│   │   │   └── StreamEventHandler.cs
│   │   │
│   │   ├── Events/                       # Event system
│   │   │   ├── EventWatchers.cs
│   │   │   ├── VarEvent.cs
│   │   │   ├── BlockEvent.cs
│   │   │   └── StreamEvent.cs
│   │   │
│   │   ├── Client/                       # Client management
│   │   │   ├── ClientRegistry.cs
│   │   │   └── ClientProperty.cs
│   │   │
│   │   ├── Logging/                      # Logging and observability
│   │   │   ├── Collector.cs
│   │   │   ├── FunctionLog.cs
│   │   │   ├── LLMCall.cs
│   │   │   ├── HttpRequest.cs
│   │   │   ├── HttpResponse.cs
│   │   │   ├── Usage.cs
│   │   │   └── Timing.cs
│   │   │
│   │   ├── Exceptions/                   # Custom exceptions
│   │   │   ├── BamlException.cs
│   │   │   ├── BamlValidationError.cs
│   │   │   ├── BamlClientError.cs
│   │   │   ├── BamlTimeoutError.cs
│   │   │   └── BamlAbortError.cs
│   │   │
│   │   └── Extensions/                   # Extension methods
│   │       ├── BamlValueExtensions.cs    # Protobuf conversions
│   │       ├── CancellationTokenExt.cs   # Tripwire mapping
│   │       └── DictionaryExtensions.cs
│   │
│   └── Native/                           # Native package projects
│       ├── Baml.Net.Native.win-x64/
│       │   ├── Baml.Net.Native.win-x64.csproj
│       │   ├── build/
│       │   │   └── Baml.Net.Native.win-x64.targets
│       │   └── runtimes/
│       │       └── win-x64/
│       │           └── native/
│       │               └── baml_cffi.dll
│       │
│       ├── Baml.Net.Native.win-arm64/
│       │   ├── Baml.Net.Native.win-arm64.csproj
│       │   └── runtimes/win-arm64/native/baml_cffi.dll
│       │
│       ├── Baml.Net.Native.linux-x64/
│       │   ├── Baml.Net.Native.linux-x64.csproj
│       │   └── runtimes/linux-x64/native/libbaml_cffi.so
│       │
│       ├── Baml.Net.Native.linux-arm64/
│       │   ├── Baml.Net.Native.linux-arm64.csproj
│       │   └── runtimes/linux-arm64/native/libbaml_cffi.so
│       │
│       ├── Baml.Net.Native.osx-x64/
│       │   ├── Baml.Net.Native.osx-x64.csproj
│       │   └── runtimes/osx-x64/native/libbaml_cffi.dylib
│       │
│       └── Baml.Net.Native.osx-arm64/
│           ├── Baml.Net.Native.osx-arm64.csproj
│           └── runtimes/osx-arm64/native/libbaml_cffi.dylib
│
├── tests/
│   ├── Baml.Net.Tests/
│   │   ├── Baml.Net.Tests.csproj           # .NET 9.0 test project
│   │   ├── RuntimeTests.cs
│   │   ├── StreamingTests.cs
│   │   ├── TypeBuilderTests.cs
│   │   ├── ProtobufTests.cs
│   │   └── IntegrationTests.cs
│   │
│   └── TestAssets/                      # BAML test files
│       ├── simple.baml
│       ├── complex.baml
│       └── generators.baml
│
├── samples/
│   ├── ConsoleApp/                      # Simple console example (.NET 9.0)
│   │   ├── ConsoleApp.csproj
│   │   └── Program.cs
│   │
│   ├── WebApi/                          # ASP.NET Core 9.0 example
│   │   ├── WebApi.csproj
│   │   ├── Program.cs
│   │   └── Controllers/
│   │       └── BamlController.cs
│   │
│   └── BlazorApp/                       # Blazor .NET 9.0 example
│       ├── BlazorApp.csproj
│       └── Pages/
│           └── BamlDemo.razor
│
├── build/
│   ├── build.ps1                        # PowerShell build script
│   ├── build.sh                         # Bash build script
│   ├── pack.ps1                         # Package creation script
│   ├── download-natives.ps1             # Download pre-built natives from GitHub
│   └── NativePackageVersion.props       # Shared version for native packages
│
└── .github/
    └── workflows/
        ├── build.yml                    # CI/CD pipeline
        ├── release.yml                  # Release pipeline
        └── nuget-publish.yml            # NuGet publishing

```

### Package Structure Details

#### 1. Main Package: Baml.Net

**Project File (Baml.Net.csproj):**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <PackageId>Baml.Net</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>BAML runtime for .NET 9.0 - AI function orchestration</Description>
    <PackageTags>baml;ai;llm;orchestration;net9</PackageTags>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/yourusername/Baml.Net</RepositoryUrl>

    <!-- Don't include native binaries in main package -->
    <IncludeBuildOutput>true</IncludeBuildOutput>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>

  <ItemGroup>
    <!-- Protocol Buffers - Check https://nuget.org/packages/Google.Protobuf for latest -->
    <PackageReference Include="Google.Protobuf" Version="3.28.3" />
    <PackageReference Include="Grpc.Tools" Version="2.66.0" PrivateAssets="All" />

    <!-- Note: Platform detection using built-in RuntimeInformation (no package needed) -->
  </ItemGroup>

  <!-- Protobuf compilation -->
  <ItemGroup>
    <Protobuf Include="Protobuf\cffi.proto" GrpcServices="None" />
  </ItemGroup>

  <!--
    IMPORTANT: Native packages are NOT referenced here.
    They are pulled in automatically at runtime based on RID.
  -->
</Project>
```

**Note**: Always verify latest package versions at:
- Google.Protobuf: https://www.nuget.org/packages/Google.Protobuf
- Grpc.Tools: https://www.nuget.org/packages/Grpc.Tools

**NuGet Package Structure:**
```
Baml.Net.nupkg
├── lib/
│   └── net9.0/
│       ├── Baml.Net.dll
│       └── Baml.Net.xml
├── build/
│   └── Baml.Net.targets      # Import native packages conditionally
└── Baml.Net.nuspec
```

**Baml.Net.targets (Auto-reference native packages):**
```xml
<Project>
  <PropertyGroup>
    <BamlNetNativePackageVersion>1.0.0</BamlNetNativePackageVersion>
  </PropertyGroup>

  <!-- Auto-reference the appropriate native package based on RuntimeIdentifier -->
  <ItemGroup Condition="'$(RuntimeIdentifier)' != ''">
    <PackageReference Include="Baml.Net.Native.$(RuntimeIdentifier)"
                      Version="$(BamlNetNativePackageVersion)"
                      Condition="Exists('$(NuGetPackageRoot)Baml.Net.Native.$(RuntimeIdentifier)/$(BamlNetNativePackageVersion)')"
                      PrivateAssets="All" />
  </ItemGroup>

  <!-- For projects without explicit RID, reference all common platforms -->
  <ItemGroup Condition="'$(RuntimeIdentifier)' == ''">
    <PackageReference Include="Baml.Net.Native.win-x64" Version="$(BamlNetNativePackageVersion)" />
    <PackageReference Include="Baml.Net.Native.linux-x64" Version="$(BamlNetNativePackageVersion)" />
    <PackageReference Include="Baml.Net.Native.osx-x64" Version="$(BamlNetNativePackageVersion)" />
    <PackageReference Include="Baml.Net.Native.osx-arm64" Version="$(BamlNetNativePackageVersion)"
                      Condition="$([MSBuild]::IsOSPlatform('OSX'))" />
  </ItemGroup>
</Project>
```

#### 2. Native Packages: Baml.Net.Native.{RID}

**Example: Baml.Net.Native.win-x64.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Baml.Net.Native.win-x64</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>Windows x64 native binaries for Baml.Net</Description>
    <PackageTags>baml;native;win-x64</PackageTags>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>

    <!-- This is a runtime package -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>

    <!-- Don't generate lib folder -->
    <NoPackageAnalysis>true</NoPackageAnalysis>
    <DevelopmentDependency>true</DevelopmentDependency>
  </PropertyGroup>

  <!-- Include native binary -->
  <ItemGroup>
    <Content Include="runtimes\win-x64\native\baml_cffi.dll">
      <PackagePath>runtimes/win-x64/native/</PackagePath>
      <Pack>true</Pack>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

  <!-- MSBuild targets for copying native library -->
  <ItemGroup>
    <Content Include="build\Baml.Net.Native.win-x64.targets">
      <PackagePath>build/</PackagePath>
      <Pack>true</Pack>
    </Content>
  </ItemGroup>
</Project>
```

**Baml.Net.Native.win-x64.targets:**
```xml
<Project>
  <!-- Copy native library to output directory -->
  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)..\runtimes\win-x64\native\baml_cffi.dll">
      <Link>baml_cffi.dll</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Visible>false</Visible>
    </None>
  </ItemGroup>
</Project>
```

### Usage Pattern for Consumers

**Consumer Project (.csproj):**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- Only reference the main package -->
    <PackageReference Include="Baml.Net" Version="1.0.0" />
    <!-- Native packages are auto-referenced based on platform -->
  </ItemGroup>
</Project>
```

**Consumer Code:**
```csharp
using Baml.Net;

// Native library is automatically loaded from the correct package
var runtime = BamlRuntime.FromDirectory("./baml_src", new Dictionary<string, string>());

// Use the runtime normally
var result = await runtime.CallFunctionAsync("MyFunction", args, ctx);
```

### Platform Detection and Loading (NativeLibrary.cs)

```csharp
internal static class NativeLibrary
{
    private const string LibraryName = "baml_cffi";

    static NativeLibrary()
    {
        // .NET Core automatically finds the library in runtimes/{rid}/native/
        // But we can be explicit if needed
        if (!TryLoadLibrary())
        {
            throw new DllNotFoundException($"Failed to load {LibraryName}. " +
                "Ensure the appropriate Baml.Net.Native.{rid} package is installed.");
        }
    }

    private static bool TryLoadLibrary()
    {
        try
        {
            // Try default resolution first (works with runtime packages)
            var handle = NativeLibrary.Load(LibraryName,
                Assembly.GetExecutingAssembly(),
                DllImportSearchPath.SafeDirectories);
            return handle != IntPtr.Zero;
        }
        catch
        {
            // Fallback to explicit paths if needed
            return TryLoadExplicitPath();
        }
    }

    private static bool TryLoadExplicitPath()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        var possiblePaths = new[]
        {
            // Direct output directory
            Path.Combine(assemblyDir, GetPlatformLibraryName()),
            // NuGet package structure
            Path.Combine(assemblyDir, "runtimes", rid, "native", GetPlatformLibraryName()),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    NativeLibrary.Load(path);
                    return true;
                }
                catch { }
            }
        }

        return false;
    }

    private static string GetPlatformLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "baml_cffi.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "libbaml_cffi.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libbaml_cffi.dylib";

        throw new PlatformNotSupportedException();
    }
}
```

### Build and Release Process

**build.ps1:**
```powershell
param(
    [string]$Configuration = "Release",
    [switch]$Pack,
    [switch]$DownloadNatives
)

# Download pre-built native libraries if requested
if ($DownloadNatives) {
    & "$PSScriptRoot/download-natives.ps1"
}

# Build managed library
dotnet build src/Baml.Net/Baml.Net.csproj -c $Configuration

# Build native packages
$rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
foreach ($rid in $rids) {
    $project = "src/Native/Baml.Net.Native.$rid/Baml.Net.Native.$rid.csproj"
    if (Test-Path $project) {
        dotnet build $project -c $Configuration
    }
}

# Pack if requested
if ($Pack) {
    & "$PSScriptRoot/pack.ps1" -Configuration $Configuration
}
```

**download-natives.ps1 (Advanced GitHub Release Downloader):**
```powershell
<#
.SYNOPSIS
    Downloads BAML CFFI native binaries from GitHub releases
.DESCRIPTION
    Downloads pre-built native libraries from BAML GitHub releases and updates version info
.PARAMETER Version
    Specific version to download (e.g., "0.212.0" or "v0.212.0"). If not specified, downloads latest.
.PARAMETER PreRelease
    Include pre-release versions when finding latest
.PARAMETER OutputPath
    Base output path for native packages (default: src/Native)
.EXAMPLE
    ./download-natives.ps1
    Downloads the latest release
.EXAMPLE
    ./download-natives.ps1 -Version "0.212.0"
    Downloads specific version
.EXAMPLE
    ./download-natives.ps1 -PreRelease
    Downloads latest including pre-releases
#>
param(
    [string]$Version = "",
    [switch]$PreRelease,
    [string]$OutputPath = "src/Native",
    [switch]$UpdateVersionProps = $true
)

$ErrorActionPreference = "Stop"

# GitHub API configuration
$owner = "BoundaryML"
$repo = "baml"
$githubApi = "https://api.github.com/repos/$owner/$repo/releases"

# Platform mapping
$platformMappings = @{
    "win-x64"     = @{
        file = "baml-windows-x64.exe.zip"
        extract = "baml_cffi-x86_64-pc-windows-msvc.dll"
        target = "baml_cffi.dll"
    }
    "win-arm64"   = @{
        file = "baml-windows-arm64.exe.zip"
        extract = "baml_cffi-aarch64-pc-windows-msvc.dll"
        target = "baml_cffi.dll"
    }
    "linux-x64"   = @{
        file = "baml-linux-x64.tar.gz"
        extract = "libbaml_cffi-x86_64-unknown-linux-gnu.so"
        target = "libbaml_cffi.so"
    }
    "linux-arm64" = @{
        file = "baml-linux-arm64.tar.gz"
        extract = "libbaml_cffi-aarch64-unknown-linux-gnu.so"
        target = "libbaml_cffi.so"
    }
    "osx-x64"     = @{
        file = "baml-macos-x64.tar.gz"
        extract = "libbaml_cffi-x86_64-apple-darwin.dylib"
        target = "libbaml_cffi.dylib"
    }
    "osx-arm64"   = @{
        file = "baml-macos-arm64.tar.gz"
        extract = "libbaml_cffi-aarch64-apple-darwin.dylib"
        target = "libbaml_cffi.dylib"
    }
}

function Get-LatestRelease {
    param([bool]$IncludePreRelease)

    Write-Host "Fetching release information from GitHub..." -ForegroundColor Cyan

    try {
        $headers = @{}
        if ($env:GITHUB_TOKEN) {
            $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN"
            Write-Host "Using GitHub token for authentication" -ForegroundColor Green
        }

        if ($IncludePreRelease) {
            $releases = Invoke-RestMethod -Uri $githubApi -Headers $headers
            $release = $releases | Select-Object -First 1
        } else {
            $release = Invoke-RestMethod -Uri "$githubApi/latest" -Headers $headers
        }

        return $release
    }
    catch {
        Write-Error "Failed to fetch release information: $_"
        throw
    }
}

function Get-SpecificRelease {
    param([string]$Version)

    # Clean version (remove 'v' prefix if present)
    $cleanVersion = $Version -replace '^v', ''

    Write-Host "Fetching release v$cleanVersion from GitHub..." -ForegroundColor Cyan

    try {
        $headers = @{}
        if ($env:GITHUB_TOKEN) {
            $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN"
        }

        $release = Invoke-RestMethod -Uri "$githubApi/tags/v$cleanVersion" -Headers $headers
        return $release
    }
    catch {
        # Try without 'v' prefix
        try {
            $release = Invoke-RestMethod -Uri "$githubApi/tags/$cleanVersion" -Headers $headers
            return $release
        }
        catch {
            Write-Error "Failed to fetch release $Version : $_"
            throw
        }
    }
}

function Download-Asset {
    param(
        [string]$Url,
        [string]$OutputFile,
        [string]$AssetName
    )

    Write-Host "  Downloading $AssetName..." -ForegroundColor Gray

    try {
        $headers = @{}
        if ($env:GITHUB_TOKEN) {
            $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN"
        }

        # GitHub API requires Accept header for asset downloads
        $headers["Accept"] = "application/octet-stream"

        # Download with progress
        $ProgressPreference = 'SilentlyContinue'  # Faster download
        Invoke-WebRequest -Uri $Url -OutFile $OutputFile -Headers $headers
        $ProgressPreference = 'Continue'

        Write-Host "  ✓ Downloaded $AssetName" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to download $AssetName : $_"
        throw
    }
}

function Extract-Binary {
    param(
        [string]$ArchivePath,
        [string]$FileName,
        [string]$TargetDir
    )

    Write-Host "  Extracting $FileName..." -ForegroundColor Gray

    $extension = [System.IO.Path]::GetExtension($ArchivePath)

    try {
        # Create temp directory for extraction
        $tempDir = Join-Path $env:TEMP "baml_extract_$(Get-Random)"
        New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

        if ($extension -eq ".zip") {
            Expand-Archive -Path $ArchivePath -DestinationPath $tempDir -Force
        }
        elseif ($extension -eq ".gz") {
            # For .tar.gz files, need tar command (available in Windows 10+)
            tar -xzf $ArchivePath -C $tempDir
        }
        else {
            throw "Unsupported archive format: $extension"
        }

        # Find the binary (might be in subdirectory)
        $binary = Get-ChildItem -Path $tempDir -Filter $FileName -Recurse | Select-Object -First 1

        if (-not $binary) {
            throw "Binary $FileName not found in archive"
        }

        # Copy to target
        Copy-Item -Path $binary.FullName -Destination $TargetDir -Force

        # Cleanup
        Remove-Item -Path $tempDir -Recurse -Force

        Write-Host "  ✓ Extracted $FileName" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to extract $FileName : $_"
        throw
    }
    finally {
        # Ensure cleanup even on error
        if (Test-Path $tempDir) {
            Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Update-NativePackageVersion {
    param([string]$Version)

    $versionPropsPath = Join-Path $PSScriptRoot "NativePackageVersion.props"

    Write-Host "Updating NativePackageVersion.props to version $Version..." -ForegroundColor Cyan

    $content = @"
<Project>
  <!-- Auto-generated by download-natives.ps1 -->
  <PropertyGroup>
    <BamlCffiVersion>$Version</BamlCffiVersion>
    <BamlNativePackageVersion>1.0.0-baml.$Version</BamlNativePackageVersion>
  </PropertyGroup>
</Project>
"@

    Set-Content -Path $versionPropsPath -Value $content -Force
    Write-Host "✓ Updated version props file" -ForegroundColor Green
}

# Main execution
Write-Host "BAML Native Binary Downloader" -ForegroundColor Yellow
Write-Host "==============================" -ForegroundColor Yellow

# Get release
if ($Version) {
    $release = Get-SpecificRelease -Version $Version
} else {
    $release = Get-LatestRelease -IncludePreRelease:$PreRelease
}

$releaseVersion = $release.tag_name -replace '^v', ''
Write-Host "Using BAML version: $releaseVersion" -ForegroundColor Green
Write-Host "Release: $($release.name)" -ForegroundColor Green
Write-Host ""

# Download and extract for each platform
foreach ($platform in $platformMappings.GetEnumerator()) {
    $rid = $platform.Key
    $mapping = $platform.Value

    Write-Host "Processing $rid..." -ForegroundColor Yellow

    # Find asset in release
    $asset = $release.assets | Where-Object { $_.name -eq $mapping.file }

    if (-not $asset) {
        Write-Warning "Asset $($mapping.file) not found for $rid, skipping..."
        continue
    }

    # Setup paths
    $nativeDir = Join-Path $OutputPath "Baml.Net.Native.$rid/runtimes/$rid/native"
    New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null

    $tempArchive = Join-Path $env:TEMP "$($mapping.file)"
    $targetBinary = Join-Path $nativeDir $mapping.target

    try {
        # Download archive
        Download-Asset -Url $asset.browser_download_url -OutputFile $tempArchive -AssetName $mapping.file

        # Extract binary
        Extract-Binary -ArchivePath $tempArchive -FileName $mapping.extract -TargetDir $targetBinary

        Write-Host "✓ Completed $rid" -ForegroundColor Green
        Write-Host ""
    }
    finally {
        # Cleanup temp file
        if (Test-Path $tempArchive) {
            Remove-Item -Path $tempArchive -Force -ErrorAction SilentlyContinue
        }
    }
}

# Update version props if requested
if ($UpdateVersionProps) {
    Update-NativePackageVersion -Version $releaseVersion
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "✓ Successfully downloaded all binaries!" -ForegroundColor Green
Write-Host "  Version: $releaseVersion" -ForegroundColor Green
Write-Host "  Location: $OutputPath" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
```

**download-natives.sh (Cross-platform Bash version):**
```bash
#!/bin/bash
set -e

# Configuration
OWNER="BoundaryML"
REPO="baml"
OUTPUT_PATH="${OUTPUT_PATH:-src/Native}"
VERSION="${1:-latest}"
UPDATE_VERSION_PROPS="${UPDATE_VERSION_PROPS:-true}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${YELLOW}BAML Native Binary Downloader${NC}"
echo "=============================="

# Platform mappings
declare -A PLATFORMS=(
    ["win-x64"]="baml-windows-x64.exe.zip|baml_cffi-x86_64-pc-windows-msvc.dll|baml_cffi.dll"
    ["linux-x64"]="baml-linux-x64.tar.gz|libbaml_cffi-x86_64-unknown-linux-gnu.so|libbaml_cffi.so"
    ["osx-x64"]="baml-macos-x64.tar.gz|libbaml_cffi-x86_64-apple-darwin.dylib|libbaml_cffi.dylib"
    ["osx-arm64"]="baml-macos-arm64.tar.gz|libbaml_cffi-aarch64-apple-darwin.dylib|libbaml_cffi.dylib"
)

# Get release info
if [ "$VERSION" == "latest" ]; then
    echo -e "${CYAN}Fetching latest release...${NC}"
    RELEASE_INFO=$(curl -s "https://api.github.com/repos/$OWNER/$REPO/releases/latest")
else
    echo -e "${CYAN}Fetching release $VERSION...${NC}"
    RELEASE_INFO=$(curl -s "https://api.github.com/repos/$OWNER/$REPO/releases/tags/v$VERSION")
fi

RELEASE_VERSION=$(echo "$RELEASE_INFO" | grep -Po '"tag_name": "\K[^"]+' | sed 's/^v//')
echo -e "${GREEN}Using BAML version: $RELEASE_VERSION${NC}\n"

# Download and extract for each platform
for RID in "${!PLATFORMS[@]}"; do
    IFS='|' read -r ARCHIVE_NAME EXTRACT_NAME TARGET_NAME <<< "${PLATFORMS[$RID]}"

    echo -e "${YELLOW}Processing $RID...${NC}"

    # Get download URL
    DOWNLOAD_URL=$(echo "$RELEASE_INFO" | grep -Po "\"browser_download_url\": \"[^\"]*${ARCHIVE_NAME}\"" | cut -d'"' -f4)

    if [ -z "$DOWNLOAD_URL" ]; then
        echo -e "${RED}Warning: Asset $ARCHIVE_NAME not found for $RID, skipping...${NC}"
        continue
    fi

    # Setup paths
    NATIVE_DIR="$OUTPUT_PATH/Baml.Net.Native.$RID/runtimes/$RID/native"
    mkdir -p "$NATIVE_DIR"

    # Download and extract
    TEMP_FILE="/tmp/$ARCHIVE_NAME"
    echo "  Downloading $ARCHIVE_NAME..."
    curl -L -o "$TEMP_FILE" "$DOWNLOAD_URL"

    echo "  Extracting $EXTRACT_NAME..."
    if [[ "$ARCHIVE_NAME" == *.zip ]]; then
        unzip -q "$TEMP_FILE" -d "/tmp/baml_extract"
        find "/tmp/baml_extract" -name "$EXTRACT_NAME" -exec cp {} "$NATIVE_DIR/$TARGET_NAME" \;
        rm -rf "/tmp/baml_extract"
    else
        tar -xzf "$TEMP_FILE" -C "/tmp"
        find "/tmp" -name "$EXTRACT_NAME" -exec cp {} "$NATIVE_DIR/$TARGET_NAME" \;
    fi

    rm -f "$TEMP_FILE"
    echo -e "${GREEN}✓ Completed $RID${NC}\n"
done

# Update version props
if [ "$UPDATE_VERSION_PROPS" == "true" ]; then
    echo -e "${CYAN}Updating NativePackageVersion.props...${NC}"
    cat > "$(dirname "$0")/NativePackageVersion.props" <<EOF
<Project>
  <!-- Auto-generated by download-natives.sh -->
  <PropertyGroup>
    <BamlCffiVersion>$RELEASE_VERSION</BamlCffiVersion>
    <BamlNativePackageVersion>1.0.0-baml.$RELEASE_VERSION</BamlNativePackageVersion>
  </PropertyGroup>
</Project>
EOF
    echo -e "${GREEN}✓ Updated version props file${NC}"
fi

echo -e "${GREEN}======================================${NC}"
echo -e "${GREEN}✓ Successfully downloaded all binaries!${NC}"
echo -e "${GREEN}  Version: $RELEASE_VERSION${NC}"
echo -e "${GREEN}  Location: $OUTPUT_PATH${NC}"
echo -e "${GREEN}======================================${NC}"
```

### Advantages of This Structure

1. **Clean Separation**: .NET integration is completely separate from BAML source
2. **Standard Pattern**: Follows established .NET conventions (e.g., SkiaSharp, SQLitePCLRaw)
3. **Platform Independence**: Consumers only reference main package
4. **Automatic RID Resolution**: .NET automatically selects correct native package
5. **Reduced Download Size**: Only downloads needed platform binaries
6. **Easy Updates**: Can update native packages independently
7. **NuGet Compatibility**: Works with PackageReference and packages.config
8. **Clear Versioning**: Native packages can version independently if needed

## Building and Deployment

### Building the Native Library

1. **Install Rust toolchain**:
```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

2. **Build for target platforms**:
```bash
# Windows
cargo build --release --target x86_64-pc-windows-msvc

# macOS Intel
cargo build --release --target x86_64-apple-darwin

# macOS ARM
cargo build --release --target aarch64-apple-darwin

# Linux
cargo build --release --target x86_64-unknown-linux-gnu
```

3. **Cross-compilation** (using cross-rs):
```bash
cargo install cross
cross build --release --target x86_64-pc-windows-msvc
```

### Publishing to NuGet

With the n+1 package structure, publishing follows this pattern:

**1. Publish Native Packages First:**
```bash
# Build and pack all native packages
foreach ($rid in @("win-x64", "linux-x64", "osx-x64", "osx-arm64")) {
    dotnet pack src/Native/Baml.Net.Native.$rid -c Release
    dotnet nuget push src/Native/Baml.Net.Native.$rid/bin/Release/*.nupkg `
        --source https://api.nuget.org/v3/index.json `
        --api-key $env:NUGET_API_KEY
}
```

**2. Publish Main Package:**
```bash
# Pack and push main package (references native packages)
dotnet pack src/Baml.Net -c Release
dotnet nuget push src/Baml.Net/bin/Release/Baml.Net.*.nupkg `
    --source https://api.nuget.org/v3/index.json `
    --api-key $env:NUGET_API_KEY
```

**3. Version Synchronization:**
```xml
<!-- Directory.Build.props - Shared version and settings across all packages -->
<Project>
  <PropertyGroup>
    <Version>1.0.0</Version>
    <Authors>Your Organization</Authors>
    <Copyright>Copyright (c) 2025</Copyright>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/yourusername/Baml.Net</RepositoryUrl>

    <!-- .NET 9.0 settings -->
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- Global NuGet package versions -->
  <ItemGroup>
    <PackageReference Update="Google.Protobuf" Version="3.28.3" />
    <PackageReference Update="Grpc.Tools" Version="2.66.0" />
  </ItemGroup>
</Project>
```

**Note**: Before building, always verify latest package versions at https://www.nuget.org/packages

### Consumer Installation

**Simple Installation (Main package auto-references natives):**
```bash
dotnet add package Baml.Net
```

**Specific Platform Only:**
```bash
# Install main package without dependencies
dotnet add package Baml.Net --no-dependencies

# Add specific native package
dotnet add package Baml.Net.Native.win-x64
```

**Package.config (legacy projects):**
```xml
<packages>
  <package id="Baml.Net" version="1.0.0" />
  <!-- Native packages auto-referenced via .targets file -->
</packages>
```

### CI/CD Pipeline (GitHub Actions)

**.github/workflows/release.yml:**
```yaml
name: Release to NuGet

on:
  push:
    tags:
      - 'v*'

jobs:
  build-natives:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: ubuntu-latest
            rid: linux-x64
          - os: macos-latest
            rid: osx-x64
          - os: macos-latest
            rid: osx-arm64

    runs-on: ${{ matrix.os }}

    steps:
    - uses: actions/checkout@v3

    - name: Download BAML Native
      run: |
        # Download pre-built native from BAML releases
        ./build/download-native.ps1 -Platform ${{ matrix.rid }}

    - name: Pack Native Package
      run: |
        dotnet pack src/Native/Baml.Net.Native.${{ matrix.rid }} -c Release

    - name: Upload Package
      uses: actions/upload-artifact@v3
      with:
        name: packages
        path: '**/*.nupkg'

  publish:
    needs: build-natives
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Download Packages
      uses: actions/download-artifact@v3
      with:
        name: packages

    - name: Pack Main Package
      run: dotnet pack src/Baml.Net -c Release

    - name: Push to NuGet
      run: |
        # Push native packages first
        dotnet nuget push "**/Baml.Net.Native.*.nupkg" \
          --source https://api.nuget.org/v3/index.json \
          --api-key ${{ secrets.NUGET_API_KEY }} \
          --skip-duplicate

        # Then push main package
        dotnet nuget push "**/Baml.Net.*.nupkg" \
          --source https://api.nuget.org/v3/index.json \
          --api-key ${{ secrets.NUGET_API_KEY }} \
          --skip-duplicate
```

### Alternative: Single Meta-Package Pattern

For simpler consumption, create an additional meta-package:

**Baml.Net.All.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Baml.Net.All</PackageId>
    <Description>BAML.NET with all platform native libraries</Description>
    <IncludeBuildOutput>false</IncludeBuildOutput>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference main package and all natives -->
    <PackageReference Include="Baml.Net" Version="$(Version)" />
    <PackageReference Include="Baml.Net.Native.win-x64" Version="$(Version)" />
    <PackageReference Include="Baml.Net.Native.win-arm64" Version="$(Version)" />
    <PackageReference Include="Baml.Net.Native.linux-x64" Version="$(Version)" />
    <PackageReference Include="Baml.Net.Native.linux-arm64" Version="$(Version)" />
    <PackageReference Include="Baml.Net.Native.osx-x64" Version="$(Version)" />
    <PackageReference Include="Baml.Net.Native.osx-arm64" Version="$(Version)" />
  </ItemGroup>
</Project>
```

This allows users to install everything with:
```bash
dotnet add package Baml.Net.All
```

### NuGet Package Structure

_(Original content about embedded native libraries - now replaced with n+1 package approach above)_

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>Baml.Compiler</PackageId>
  </PropertyGroup>

  <ItemGroup>
    <!-- Windows x64 -->
    <Content Include="runtimes\win-x64\native\baml_cffi.dll">
      <PackagePath>runtimes\win-x64\native\</PackagePath>
      <Pack>true</Pack>
    </Content>

    <!-- macOS x64 -->
    <Content Include="runtimes\osx-x64\native\libbaml_cffi.dylib">
      <PackagePath>runtimes\osx-x64\native\</PackagePath>
      <Pack>true</Pack>
    </Content>

    <!-- Linux x64 -->
    <Content Include="runtimes\linux-x64\native\libbaml_cffi.so">
      <PackagePath>runtimes\linux-x64\native\</PackagePath>
      <Pack>true</Pack>
    </Content>
  </ItemGroup>
</Project>
```

### Runtime Library Loading

.NET will automatically load the correct native library based on the runtime identifier. Alternatively, implement custom loading:

```csharp
public static class NativeLibrary
{
    static NativeLibrary()
    {
        var libraryPath = GetPlatformLibraryPath();
        NativeLibrary.Load(libraryPath);
    }

    private static string GetPlatformLibraryPath()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var directory = Path.GetDirectoryName(assemblyLocation);

        return rid switch
        {
            var r when r.StartsWith("win") => Path.Combine(directory, "runtimes", rid, "native", "baml_cffi.dll"),
            var r when r.StartsWith("osx") => Path.Combine(directory, "runtimes", rid, "native", "libbaml_cffi.dylib"),
            var r when r.StartsWith("linux") => Path.Combine(directory, "runtimes", rid, "native", "libbaml_cffi.so"),
            _ => throw new PlatformNotSupportedException($"Platform {rid} is not supported")
        };
    }
}
```

## Recommendations

### For Initial Implementation

1. **Start with CLI subprocess approach** for quick prototyping
2. **Use C FFI for production** implementation
3. **Consider implementing a custom .NET generator** in Rust for optimal integration

### For Production Use

1. **Build native libraries** for all target platforms
2. **Create NuGet package** with embedded native libraries
3. **Implement robust error handling** for FFI calls
4. **Add comprehensive logging** for debugging
5. **Create abstraction layer** over raw FFI calls
6. **Implement async/await patterns** for BAML function calls

### Next Steps

1. **Prototype CLI integration** to validate approach
2. **Build minimal C FFI wrapper** in C#
3. **Test Protocol Buffers serialization**
4. **Implement streaming support** for real-time responses
5. **Create code generation templates** for .NET types
6. **Package as NuGet** for distribution

## Conclusion

Integrating BAML with .NET is achievable through multiple approaches. The C FFI approach provides the most comprehensive access to BAML's capabilities while maintaining good performance. The CLI approach offers simplicity for basic use cases. For full integration, implementing a custom .NET code generator in the BAML codebase would provide the best developer experience.

The key insight is that BAML's architecture is modular and extensible, with clear separation between parsing, compilation, and code generation phases. This makes it straightforward to add .NET as a new target language alongside the existing TypeScript, Python, Ruby, and Go implementations.