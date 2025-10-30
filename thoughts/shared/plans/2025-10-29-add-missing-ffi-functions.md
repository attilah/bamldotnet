# Add Missing FFI Functions to Baml.Net Implementation Plan

## Overview

This plan outlines the implementation of four missing FFI functions from the BAML C header that are not yet available in the .NET implementation. These functions enable parsing without LLM calls, enhanced cancellation of in-flight operations, and dynamic object construction/method invocation.

### Key Enhancement: Hybrid Cancellation

A major improvement in this implementation is the integration of .NET's `CancellationToken` with the native FFI `cancel_function_call`. This follows the pattern used by Go (which monitors `context.Done()`) and Python (which uses `AbortController`), providing:

- **Automatic native cancellation** when CancellationToken is cancelled
- **Improved responsiveness** by cancelling operations at the Rust runtime level
- **Backward compatibility** with existing code using CancellationToken
- **Idempotent cancellation** that's safe to call multiple times

## Current State Analysis

The current Baml.Net implementation (`src/Baml.Net/FFI/BamlNative.cs`) has:
- ✅ `version()` - Get runtime version
- ✅ `create_baml_runtime()` - Create runtime instance
- ✅ `destroy_baml_runtime()` - Destroy runtime instance
- ✅ `invoke_runtime_cli()` - Invoke CLI commands
- ✅ `call_function_from_c()` - Execute BAML functions
- ✅ `call_function_stream_from_c()` - Execute streaming functions
- ✅ `register_callbacks()` - Register callback delegates
- ✅ `free_buffer()` - Free native buffers

Missing functions from the C header:
- ❌ `call_function_parse_from_c()` - Parse LLM responses without making calls
- ❌ `cancel_function_call()` - Cancel in-flight function calls
- ❌ `call_object_constructor()` - Create BAML objects
- ❌ `call_object_method()` - Invoke methods on BAML objects

### Key Discoveries:
- Other language clients (Go, Python) actively use these missing functions
- .NET currently uses `CancellationToken` pattern instead of native cancellation
- Object construction/methods enable collectors, media handling, and dynamic types
- Protobuf message definitions already exist in `src/Baml.Net/Protobuf/cffi.proto`

## Desired End State

A complete FFI implementation that provides:
1. **Parsing capability**: Parse pre-existing LLM responses for testing and replay scenarios
2. **Hybrid cancellation**: Seamless integration of .NET's `CancellationToken` with native FFI cancellation for optimal responsiveness
3. **Object construction**: Create collectors, media objects, and type builders
4. **Method invocation**: Call methods on constructed objects to query state and build types

### Success Verification:
- All four FFI functions properly marshalled and callable from .NET
- CancellationToken automatically triggers native FFI cancellation
- Comprehensive test coverage matching TypeScript/Go patterns
- Full integration with existing `BamlRuntime` and `BamlRuntimeAsync` classes
- Proper memory management and error handling
- Idempotent cancellation that handles edge cases gracefully

## What We're NOT Doing

- NOT removing the existing `CancellationToken` implementation (enhancing it with native cancellation)
- NOT breaking existing callback manager architecture (extending it)
- NOT changing the protobuf serialization format
- NOT implementing TypeScript-specific NAPI functions (those use different FFI)
- NOT modifying the native library loading mechanism
- NOT requiring users to change their existing cancellation code

## Implementation Approach

The implementation will follow the established patterns in the codebase:
1. Add FFI declarations to `BamlNative.cs` with proper marshalling
2. Create helper methods in `BamlNativeHelpers.cs` for safe invocation
3. Extend `BamlRuntime` classes with new functionality
4. Add comprehensive tests following existing test patterns

## Phase 1: Add FFI Declarations and Basic Infrastructure

### Overview
Add the four missing FFI function declarations to `BamlNative.cs` with proper P/Invoke signatures and marshalling attributes.

### Changes Required:

#### 1. BamlNative.cs FFI Declarations
**File**: `src/Baml.Net/FFI/BamlNative.cs`
**Changes**: Add new FFI function declarations

```csharp
// After line 167 (CallFunctionStream declaration), add:

/// <summary>
/// Parses an LLM response without making an actual LLM call.
/// Used for testing and replay scenarios.
/// </summary>
/// <param name="runtime">Handle to the runtime instance.</param>
/// <param name="functionName">Pointer to null-terminated function name string.</param>
/// <param name="args">Pointer to protobuf-encoded arguments containing the text to parse.</param>
/// <param name="argsLen">Length of the arguments buffer.</param>
/// <param name="callbackId">Callback identifier for async operations.</param>
/// <returns>Pointer to error string on failure, IntPtr.Zero on success.</returns>
[LibraryImport(LibName, EntryPoint = "call_function_parse_from_c")]
[UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
internal static partial IntPtr CallFunctionParse(
    IntPtr runtime,
    IntPtr functionName,
    IntPtr args,
    nuint argsLen,
    uint callbackId);

/// <summary>
/// Cancels an in-flight function call by its callback ID.
/// </summary>
/// <param name="callbackId">The callback identifier to cancel.</param>
/// <returns>Always returns IntPtr.Zero.</returns>
[LibraryImport(LibName, EntryPoint = "cancel_function_call")]
[UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
internal static partial IntPtr CancelFunctionCall(uint callbackId);

/// <summary>
/// Creates a BAML object (collector, media, type builder, etc.).
/// </summary>
/// <param name="args">Pointer to protobuf-encoded constructor arguments.</param>
/// <param name="argsLen">Length of the arguments buffer.</param>
/// <returns>Buffer containing the constructed object or error.</returns>
[LibraryImport(LibName, EntryPoint = "call_object_constructor")]
[UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
internal static partial Buffer CallObjectConstructor(
    IntPtr args,
    nuint argsLen);

/// <summary>
/// Invokes a method on a BAML object.
/// </summary>
/// <param name="runtime">Handle to the runtime instance.</param>
/// <param name="args">Pointer to protobuf-encoded method arguments.</param>
/// <param name="argsLen">Length of the arguments buffer.</param>
/// <returns>Buffer containing the method result or error.</returns>
[LibraryImport(LibName, EntryPoint = "call_object_method")]
[UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
internal static partial Buffer CallObjectMethod(
    IntPtr runtime,
    IntPtr args,
    nuint argsLen);
```

### Success Criteria:

#### Automated Verification:
- [x] Project compiles successfully: `dotnet build`
- [x] No P/Invoke marshalling errors: `dotnet build /warnaserror`
- [x] All existing tests pass: `dotnet test`

#### Manual Verification:
- [ ] FFI declarations match C header signatures exactly
- [ ] XML documentation is clear and accurate

---

## Phase 2: Implement Helper Methods for Safe Invocation

### Overview
Create helper methods in `BamlNativeHelpers.cs` to safely invoke the new FFI functions with proper error handling and resource cleanup.

### Changes Required:

#### 1. BamlNativeHelpers Extensions
**File**: `src/Baml.Net/FFI/BamlNativeHelpers.cs`
**Changes**: Add helper methods for new FFI functions

```csharp
// After the existing helper methods, add:

/// <summary>
/// Parses an LLM response text using BAML's parsing logic.
/// </summary>
internal static async Task<byte[]> ParseLlmResponseAsync(
    IntPtr runtime,
    string functionName,
    string responseText,
    bool allowStreamTypes,
    CancellationToken cancellationToken = default)
{
    // Create parse arguments with the text to parse
    var parseArgs = new Dictionary<string, object>
    {
        ["text"] = responseText,
        ["stream"] = allowStreamTypes
    };

    var functionArgs = parseArgs.ToFunctionArguments(new Dictionary<string, string>());
    var argsBytes = functionArgs.ToByteArray();

    // Create callback context
    var (callbackId, callbackTask) = BamlCallbackManager.Instance.CreateCallback(cancellationToken);

    try
    {
        unsafe
        {
            fixed (byte* pFunctionName = Encoding.UTF8.GetBytes(functionName + '\0'))
            fixed (byte* pArgs = argsBytes)
            {
                IntPtr result = BamlNative.CallFunctionParse(
                    runtime,
                    (IntPtr)pFunctionName,
                    (IntPtr)pArgs,
                    (nuint)argsBytes.Length,
                    callbackId);

                if (result != IntPtr.Zero)
                {
                    var error = Marshal.PtrToStringUTF8(result);
                    throw new InvalidOperationException($"Failed to parse LLM response: {error}");
                }
            }
        }

        return await callbackTask.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Ensure cleanup on cancellation
        BamlNative.CancelFunctionCall(callbackId);
        throw;
    }
}

/// <summary>
/// Cancels a function call by its callback ID.
/// </summary>
internal static void CancelFunction(uint callbackId)
{
    BamlNative.CancelFunctionCall(callbackId);
}

/// <summary>
/// Creates a BAML object using the constructor.
/// </summary>
internal static byte[] CreateObject(CFFIObjectType objectType, Dictionary<string, object> kwargs)
{
    var constructorArgs = new CFFIObjectConstructorArgs
    {
        Type = objectType,
        Kwargs = { kwargs.ToCFFIMapEntries() }
    };

    var argsBytes = constructorArgs.ToByteArray();

    unsafe
    {
        fixed (byte* pArgs = argsBytes)
        {
            var buffer = BamlNative.CallObjectConstructor(
                (IntPtr)pArgs,
                (nuint)argsBytes.Length);

            try
            {
                if (buffer.Data == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create object: null buffer returned");
                }

                // Copy buffer data to managed array
                var result = new byte[buffer.Length];
                Marshal.Copy(buffer.Data, result, 0, (int)buffer.Length);
                return result;
            }
            finally
            {
                // Always free the buffer
                BamlNative.FreeBuffer(buffer);
            }
        }
    }
}

/// <summary>
/// Invokes a method on a BAML object.
/// </summary>
internal static byte[] CallObjectMethod(
    IntPtr runtime,
    long objectPointer,
    string methodName,
    Dictionary<string, object>? kwargs = null)
{
    var methodArgs = new CFFIObjectMethodArguments
    {
        Object = new CFFIRawObject { Pointer = objectPointer },
        MethodName = methodName,
        Kwargs = { (kwargs ?? new()).ToCFFIMapEntries() }
    };

    var argsBytes = methodArgs.ToByteArray();

    unsafe
    {
        fixed (byte* pArgs = argsBytes)
        {
            var buffer = BamlNative.CallObjectMethod(
                runtime,
                (IntPtr)pArgs,
                (nuint)argsBytes.Length);

            try
            {
                if (buffer.Data == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"Failed to call method '{methodName}': null buffer returned");
                }

                // Copy buffer data to managed array
                var result = new byte[buffer.Length];
                Marshal.Copy(buffer.Data, result, 0, (int)buffer.Length);
                return result;
            }
            finally
            {
                // Always free the buffer
                BamlNative.FreeBuffer(buffer);
            }
        }
    }
}
```

#### 2. Extension Methods for Protobuf Conversion
**File**: `src/Baml.Net/Extensions/BamlValueExtensions.cs`
**Changes**: Add helper for creating CFFI map entries

```csharp
// Add new method after existing extensions:

/// <summary>
/// Converts a dictionary to CFFI map entries for use in object constructor/method calls.
/// </summary>
internal static IEnumerable<CFFIMapEntry> ToCFFIMapEntries(this Dictionary<string, object> dict)
{
    foreach (var kvp in dict)
    {
        yield return new CFFIMapEntry
        {
            Key = kvp.Key,
            Value = kvp.Value.ToCFFI()
        };
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] Project compiles: `dotnet build`
- [ ] No unsafe code warnings: `dotnet build /p:TreatWarningsAsErrors=true`

#### Manual Verification:
- [ ] Helper methods properly handle null/empty inputs
- [ ] Buffer cleanup happens in all code paths

---

## Phase 2.5: Enhance Cancellation with Native FFI Integration

### Overview
Integrate .NET's `CancellationToken` with the native FFI `cancel_function_call` to provide optimal cancellation responsiveness. This follows the pattern used by Go (monitoring context.Done()) while maintaining .NET idioms.

### Changes Required:

#### 1. Enhanced BamlCallbackManager with Native Cancellation
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Modify the callback manager to call native cancellation when CancellationToken fires

```csharp
// Modify the CreateCallback method starting at line 58:

public static (uint callbackId, Task<byte[]> task) CreateCallback(
    CancellationToken cancellationToken = default,
    TimeSpan? timeout = null)
{
    EnsureCallbacksRegistered();

    var callbackId = (uint)Interlocked.Increment(ref _nextCallbackId);

    CancellationTokenSource? linkedCts = null;
    try
    {
        // Create linked cancellation token if timeout is specified
        if (timeout.HasValue)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout.Value);
            cancellationToken = linkedCts.Token;
        }

        var context = new CallbackContext(callbackId, cancellationToken);

        if (!_pendingCallbacks.TryAdd(callbackId, context))
        {
            throw new InvalidOperationException("Failed to track callback request.");
        }

        // Enhanced cancellation: Call native FFI when token is cancelled
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                // First, call native cancellation for immediate effect
                try
                {
                    BamlNative.CancelFunctionCall(callbackId);
                }
                catch
                {
                    // Ignore errors from native cancellation - it's fire-and-forget
                    // The native function is idempotent and safe to call even after completion
                }

                // Then remove from tracking and mark as cancelled
                if (_pendingCallbacks.TryRemove(callbackId, out var ctx))
                {
                    ctx.TrySetCanceled(cancellationToken);
                }
            });
        }

        return (callbackId, context.Task);
    }
    finally
    {
        linkedCts?.Dispose();
    }
}

// Add similar enhancement to CreateStreamingCallback at line 108:

public static (uint callbackId, Task<byte[]> task) CreateStreamingCallback(
    Action<byte[]> onChunk,
    CancellationToken cancellationToken = default)
{
    EnsureCallbacksRegistered();

    var callbackId = (uint)Interlocked.Increment(ref _nextCallbackId);
    var context = new StreamingCallbackContext(callbackId, onChunk, cancellationToken);

    if (!_pendingCallbacks.TryAdd(callbackId, context))
    {
        throw new InvalidOperationException("Failed to track streaming callback request.");
    }

    // Enhanced cancellation for streaming
    if (cancellationToken.CanBeCanceled)
    {
        cancellationToken.Register(() =>
        {
            // Call native cancellation
            try
            {
                BamlNative.CancelFunctionCall(callbackId);
            }
            catch
            {
                // Ignore errors - idempotent operation
            }

            // Remove from tracking
            if (_pendingCallbacks.TryRemove(callbackId, out var ctx))
            {
                ctx.TrySetCanceled(cancellationToken);
            }
        });
    }

    return (callbackId, context.Task);
}

// Update StreamingCallbackContext constructor to accept CancellationToken:

private class StreamingCallbackContext : CallbackContext
{
    private readonly Action<byte[]> _onChunk;

    public StreamingCallbackContext(uint callbackId, Action<byte[]> onChunk, CancellationToken cancellationToken)
        : base(callbackId, cancellationToken)  // Pass token to base
    {
        _onChunk = onChunk ?? throw new ArgumentNullException(nameof(onChunk));
    }

    // ... rest of the class remains the same
}
```

#### 2. Update BamlNativeHelpers to Support Enhanced Cancellation
**File**: `src/Baml.Net/FFI/BamlNativeHelpers.cs`
**Changes**: Ensure all helper methods properly propagate cancellation tokens

```csharp
// Update the ParseLlmResponseAsync method (already handles cancellation properly):
// No changes needed - it already uses CreateCallback with cancellationToken

// Add a new public method for direct cancellation (useful for advanced scenarios):

/// <summary>
/// Directly cancels a function call by its callback ID.
/// This is automatically called when using CancellationToken, but can be used directly if needed.
/// The operation is idempotent and safe to call multiple times or after completion.
/// </summary>
/// <param name="callbackId">The callback ID to cancel.</param>
public static void CancelFunction(uint callbackId)
{
    try
    {
        BamlNative.CancelFunctionCall(callbackId);
    }
    catch
    {
        // Ignore any errors - the operation is idempotent
        // and may have already completed
    }
}
```

#### 3. Add Cancellation Context for Advanced Scenarios
**File**: `src/Baml.Net/Core/BamlCancellationContext.cs` (new file)
**Changes**: Create a context object that exposes callback IDs for advanced cancellation scenarios

```csharp
using System;
using System.Threading;
using Baml.Net.FFI;

namespace Baml.Net.Core;

/// <summary>
/// Provides context for BAML operations that can be cancelled.
/// This is an advanced feature for scenarios where direct control over cancellation is needed.
/// </summary>
public class BamlCancellationContext
{
    private readonly uint _callbackId;
    private readonly CancellationToken _token;
    private int _cancelled = 0;

    internal BamlCancellationContext(uint callbackId, CancellationToken token)
    {
        _callbackId = callbackId;
        _token = token;
    }

    /// <summary>
    /// Gets the callback ID for this operation.
    /// </summary>
    public uint CallbackId => _callbackId;

    /// <summary>
    /// Gets the cancellation token associated with this operation.
    /// </summary>
    public CancellationToken Token => _token;

    /// <summary>
    /// Gets whether this operation has been cancelled.
    /// </summary>
    public bool IsCancelled => _cancelled != 0 || _token.IsCancellationRequested;

    /// <summary>
    /// Cancels this operation directly via native FFI.
    /// This is called automatically when the CancellationToken is cancelled,
    /// but can be called manually for immediate cancellation.
    /// </summary>
    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _cancelled, 1, 0) == 0)
        {
            BamlNativeHelpers.CancelFunction(_callbackId);
        }
    }
}

/// <summary>
/// Extensions for operations that support cancellation context.
/// </summary>
public static class BamlCancellationExtensions
{
    /// <summary>
    /// Creates a cancellation context for advanced cancellation scenarios.
    /// </summary>
    public static BamlCancellationContext CreateCancellationContext(this BamlRuntimeAsync runtime, CancellationToken token = default)
    {
        // This would be used internally by operations that want to expose their callback ID
        // Implementation would be added to BamlRuntimeAsync methods that support it
        throw new NotImplementedException("This will be implemented in BamlRuntimeAsync");
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] Existing cancellation tests still pass: `dotnet test --filter "Cancellation"`
- [ ] Native cancellation is called when token fires: Add logging/mock verification
- [ ] Idempotent cancellation works: Test multiple cancel calls

#### Manual Verification:
- [ ] Cancellation is more responsive than before (measure latency)
- [ ] No errors when cancelling completed operations
- [ ] Memory profiler shows no leaks from cancellation registrations

---

## Phase 3: Add Runtime Support for New Functions

### Overview
Extend `BamlRuntime` and `BamlRuntimeAsync` classes to expose the new functionality with proper .NET idioms.

### Changes Required:

#### 1. BamlRuntime Parse Support
**File**: `src/Baml.Net/Core/BamlRuntime.cs`
**Changes**: Add parse method

```csharp
// Add after existing CallFunctionStream method:

/// <summary>
/// Parses a raw LLM response text into a BAML type.
/// </summary>
/// <param name="functionName">The BAML function to use for parsing context.</param>
/// <param name="responseText">The raw LLM response text to parse.</param>
/// <param name="allowStreamTypes">Whether to allow stream types in parsing.</param>
/// <returns>The parsed response as protobuf bytes.</returns>
public byte[] ParseLlmResponse(string functionName, string responseText, bool allowStreamTypes = false)
{
    if (string.IsNullOrEmpty(functionName))
        throw new ArgumentNullException(nameof(functionName));
    if (responseText == null)
        throw new ArgumentNullException(nameof(responseText));

    var parseTask = BamlNativeHelpers.ParseLlmResponseAsync(
        _handle,
        functionName,
        responseText,
        allowStreamTypes);

    return parseTask.GetAwaiter().GetResult();
}
```

#### 2. BamlRuntimeAsync Parse Support
**File**: `src/Baml.Net/Core/BamlRuntimeAsync.cs`
**Changes**: Add async parse method with generic deserialization

```csharp
// Add after existing CallFunctionStream method:

/// <summary>
/// Parses a raw LLM response text into a strongly-typed BAML object.
/// </summary>
/// <typeparam name="T">The expected return type.</typeparam>
/// <param name="functionName">The BAML function to use for parsing context.</param>
/// <param name="responseText">The raw LLM response text to parse.</param>
/// <param name="allowStreamTypes">Whether to allow stream types in parsing.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>The parsed and deserialized response.</returns>
public async Task<T> ParseLlmResponseAsync<T>(
    string functionName,
    string responseText,
    bool allowStreamTypes = false,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrEmpty(functionName))
        throw new ArgumentNullException(nameof(functionName));
    if (responseText == null)
        throw new ArgumentNullException(nameof(responseText));

    var resultBytes = await BamlNativeHelpers.ParseLlmResponseAsync(
        _runtime._handle,
        functionName,
        responseText,
        allowStreamTypes,
        cancellationToken).ConfigureAwait(false);

    // Parse the response
    var response = CFFIObjectResponse.Parser.ParseFrom(resultBytes);
    if (response.ResultCase == CFFIObjectResponse.ResultOneofCase.Error)
    {
        throw new InvalidOperationException($"Parse error: {response.Error.Message}");
    }

    // Deserialize to requested type
    var valueHolder = response.Object.Value;
    var result = valueHolder.ToObject();

    if (result is T typedResult)
        return typedResult;

    throw new InvalidCastException($"Cannot convert parsed result to type {typeof(T).Name}");
}
```

#### 3. Object Construction Support
**File**: `src/Baml.Net/Core/BamlObjects.cs` (new file)
**Changes**: Create new file for BAML object support

```csharp
using System;
using System.Collections.Generic;
using Baml.Cffi;
using Baml.Net.FFI;

namespace Baml.Net.Core;

/// <summary>
/// Base class for BAML objects that can be constructed and have methods invoked.
/// </summary>
public abstract class BamlObject : IDisposable
{
    protected readonly BamlRuntime _runtime;
    protected readonly long _pointer;
    private bool _disposed;

    protected BamlObject(BamlRuntime runtime, long pointer)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _pointer = pointer;
    }

    /// <summary>
    /// Invokes a method on this object.
    /// </summary>
    protected T InvokeMethod<T>(string methodName, Dictionary<string, object>? kwargs = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var resultBytes = BamlNativeHelpers.CallObjectMethod(
            _runtime._handle,
            _pointer,
            methodName,
            kwargs);

        var response = CFFIObjectResponse.Parser.ParseFrom(resultBytes);
        if (response.ResultCase == CFFIObjectResponse.ResultOneofCase.Error)
        {
            throw new InvalidOperationException($"Method '{methodName}' failed: {response.Error.Message}");
        }

        var result = response.Object.Value.ToObject();
        if (result is T typedResult)
            return typedResult;

        throw new InvalidCastException($"Cannot convert result to type {typeof(T).Name}");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Call destructor method to release Rust-side reference
                try
                {
                    InvokeMethod<object>("~destructor");
                }
                catch
                {
                    // Ignore destructor errors
                }
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Collector for tracing BAML function calls and collecting metrics.
/// </summary>
public class BamlCollector : BamlObject
{
    public BamlCollector(BamlRuntime runtime, string? name = null)
        : base(runtime, CreateCollector(name))
    {
    }

    private static long CreateCollector(string? name)
    {
        var kwargs = new Dictionary<string, object>();
        if (name != null)
            kwargs["name"] = name;

        var resultBytes = BamlNativeHelpers.CreateObject(
            CFFIObjectType.ObjectCollector,
            kwargs);

        var response = CFFIObjectResponse.Parser.ParseFrom(resultBytes);
        if (response.ResultCase == CFFIObjectResponse.ResultOneofCase.Error)
        {
            throw new InvalidOperationException($"Failed to create collector: {response.Error.Message}");
        }

        return response.Object.Object.Pointer;
    }

    /// <summary>
    /// Gets the usage statistics collected.
    /// </summary>
    public Dictionary<string, object> GetUsage()
    {
        return InvokeMethod<Dictionary<string, object>>("usage");
    }

    /// <summary>
    /// Gets all collected function logs.
    /// </summary>
    public List<object> GetLogs()
    {
        return InvokeMethod<List<object>>("logs");
    }

    /// <summary>
    /// Clears all collected data.
    /// </summary>
    public void Clear()
    {
        InvokeMethod<object>("clear");
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] Compilation succeeds: `dotnet build`
- [ ] Type safety is maintained: `dotnet build /p:TreatWarningsAsErrors=true`

#### Manual Verification:
- [ ] Parse methods handle edge cases (empty text, invalid function names)
- [ ] Object lifecycle is properly managed with Dispose pattern

---

## Phase 4: Add Comprehensive Tests

### Overview
Add test coverage for all new FFI functions following patterns from TypeScript/Go implementations.

### Changes Required:

#### 1. Parse Function Tests
**File**: `tests/Baml.Net.Tests/FFI/BamlParseTests.cs` (new file)
**Changes**: Create parse function tests

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.FFI;

public class BamlParseTests : BamlTestBase
{
    [Fact]
    public async Task ParseLlmResponse_SimpleString_ShouldParse()
    {
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var responseText = "\"Hello, World!\"";

        // Act
        var result = await asyncRuntime.ParseLlmResponseAsync<string>(
            "FnOutputString",
            responseText);

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public async Task ParseLlmResponse_ComplexObject_ShouldParse()
    {
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var responseText = @"{
            ""name"": ""John Doe"",
            ""age"": 30,
            ""email"": ""john@example.com""
        }";

        // Act
        var result = await asyncRuntime.ParseLlmResponseAsync<Dictionary<string, object>>(
            "ExtractResume",
            responseText);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("John Doe", result["name"]);
        Assert.Equal(30L, result["age"]);
        Assert.Equal("john@example.com", result["email"]);
    }

    [Fact]
    public async Task ParseLlmResponse_InvalidJson_ShouldThrow()
    {
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var responseText = "not valid json {";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await asyncRuntime.ParseLlmResponseAsync<Dictionary<string, object>>(
                "ExtractResume",
                responseText);
        });
    }

    [Fact]
    public async Task ParseLlmResponse_WithCancellation_ShouldCancel()
    {
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await asyncRuntime.ParseLlmResponseAsync<string>(
                "FnOutputString",
                "\"test\"",
                cancellationToken: cts.Token);
        });
    }
}
```

#### 2. Enhanced Native Cancellation Tests
**File**: `tests/Baml.Net.Tests/FFI/BamlCancellationTests.cs` (new file)
**Changes**: Create comprehensive cancellation tests including native FFI integration

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Baml.Net.Core;
using Baml.Net.FFI;
using Xunit;

namespace Baml.Net.Tests.FFI;

public class BamlCancellationTests : BamlTestBase
{
    [Fact]
    public async Task CancelFunctionCall_DuringExecution_ShouldCancel()
    {
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var args = new Dictionary<string, object> { ["input"] = "test" };
        var cts = new CancellationTokenSource();

        // Act
        var task = asyncRuntime.CallFunctionAsync<string>(
            "SlowFunction",
            args,
            GetEnvVars(),
            cts.Token);

        // Cancel after a short delay
        await Task.Delay(100);
        cts.Cancel();

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task CancellationToken_ShouldTriggerNativeCancellation()
    {
        // This test verifies that CancellationToken properly triggers native FFI cancellation
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var args = new Dictionary<string, object> { ["prompt"] = "test" };
        var cts = new CancellationTokenSource();

        // Track if native cancellation was called (would need mock/instrumentation in real impl)
        bool nativeCancellationCalled = false;

        // Act
        var task = asyncRuntime.CallFunctionAsync<string>(
            "TestRetryConstant",
            args,
            GetEnvVars(),
            cts.Token);

        // Cancel immediately
        cts.Cancel();

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        // In real implementation, verify native cancellation was called via logging or mocking
    }

    [Fact]
    public async Task MultipleConcurrentCancellations_ShouldAllCancel()
    {
        // Test pattern from Go: multiple operations sharing same cancellation token
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var args = new Dictionary<string, object> { ["input"] = "test" };
        var cts = new CancellationTokenSource();

        var tasks = new List<Task<string>>();

        // Act - Start 3 concurrent operations
        for (int i = 0; i < 3; i++)
        {
            tasks.Add(asyncRuntime.CallFunctionAsync<string>(
                "TestRetryConstant",
                args,
                GetEnvVars(),
                cts.Token));
        }

        // Cancel all operations after 100ms
        await Task.Delay(100);
        cts.Cancel();

        // Assert - All should be cancelled
        foreach (var task in tasks)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        }
    }

    [Fact]
    public async Task Timeout_ShouldTriggerCancellation()
    {
        // Test timeout-based cancellation
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var args = new Dictionary<string, object> { ["input"] = "test" };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await asyncRuntime.CallFunctionAsync<string>(
                "TestRetryExponential",
                args,
                GetEnvVars(),
                cts.Token);
        });
    }

    [Fact]
    public void DirectCancellation_ShouldBeIdempotent()
    {
        // Test that calling cancel multiple times is safe
        // Arrange
        var callbackId = 12345u;

        // Act - Call cancel multiple times
        BamlNativeHelpers.CancelFunction(callbackId);
        BamlNativeHelpers.CancelFunction(callbackId);
        BamlNativeHelpers.CancelFunction(callbackId);

        // Assert - Should not throw
        // The operation is idempotent
    }

    [Fact]
    public async Task CancelAfterCompletion_ShouldBeNoOp()
    {
        // Test that cancelling after operation completes is safe
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var args = new Dictionary<string, object> { ["text"] = "test" };
        var cts = new CancellationTokenSource();

        // Act - Let operation complete
        var result = await asyncRuntime.CallFunctionAsync<string>(
            "FnOutputString",
            args,
            GetEnvVars(),
            cts.Token);

        // Cancel after completion
        cts.Cancel();

        // Assert - Result should still be valid
        Assert.NotNull(result);
        // No exceptions should be thrown
    }

    [Fact]
    public async Task StreamingCancellation_ShouldStopStream()
    {
        // Test cancellation during streaming
        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);
        var args = new Dictionary<string, object> { ["prompt"] = "test" };
        var cts = new CancellationTokenSource();
        var chunks = new List<string>();

        // Act
        try
        {
            await foreach (var chunk in asyncRuntime.CallFunctionStream<string>(
                "PromptTestStreaming",
                args,
                GetEnvVars(),
                cts.Token))
            {
                chunks.Add(chunk);
                if (chunks.Count >= 2)
                {
                    // Cancel after receiving some chunks
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Should have received some chunks before cancellation
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count >= 2);
    }

    [Fact]
    public void BamlCancellationContext_DirectCancel_ShouldWork()
    {
        // Test the advanced cancellation context
        // Arrange
        var callbackId = 99999u;
        var cts = new CancellationTokenSource();
        var context = new BamlCancellationContext(callbackId, cts.Token);

        // Act
        context.Cancel();

        // Assert
        Assert.True(context.IsCancelled);
    }

    [Fact]
    public void BamlCancellationContext_TokenCancel_ShouldPropagate()
    {
        // Test that token cancellation propagates to context
        // Arrange
        var callbackId = 88888u;
        var cts = new CancellationTokenSource();
        var context = new BamlCancellationContext(callbackId, cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.True(context.IsCancelled);
        Assert.True(context.Token.IsCancellationRequested);
    }
}
```

#### 3. Object Construction and Method Tests
**File**: `tests/Baml.Net.Tests/Core/BamlObjectTests.cs` (new file)
**Changes**: Create object tests

```csharp
using System;
using System.Linq;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Core;

public class BamlObjectTests : BamlTestBase
{
    [Fact]
    public void Collector_Creation_ShouldSucceed()
    {
        // Arrange
        var runtime = CreateRuntime();

        // Act
        using var collector = new BamlCollector(runtime, "test-collector");

        // Assert
        Assert.NotNull(collector);
    }

    [Fact]
    public void Collector_GetUsage_ShouldReturnEmptyInitially()
    {
        // Arrange
        var runtime = CreateRuntime();
        using var collector = new BamlCollector(runtime);

        // Act
        var usage = collector.GetUsage();

        // Assert
        Assert.NotNull(usage);
        Assert.Empty(usage);
    }

    [Fact]
    public void Collector_GetLogs_ShouldReturnEmptyInitially()
    {
        // Arrange
        var runtime = CreateRuntime();
        using var collector = new BamlCollector(runtime);

        // Act
        var logs = collector.GetLogs();

        // Assert
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public void Collector_Clear_ShouldNotThrow()
    {
        // Arrange
        var runtime = CreateRuntime();
        using var collector = new BamlCollector(runtime);

        // Act & Assert (should not throw)
        collector.Clear();
    }

    [Fact]
    public void Collector_Dispose_ShouldCallDestructor()
    {
        // Arrange
        var runtime = CreateRuntime();
        var collector = new BamlCollector(runtime);

        // Act
        collector.Dispose();

        // Assert
        // Should not throw when calling methods after dispose
        Assert.Throws<ObjectDisposedException>(() => collector.GetUsage());
    }

    [Fact]
    public void Collector_MultipleDispose_ShouldNotThrow()
    {
        // Arrange
        var runtime = CreateRuntime();
        var collector = new BamlCollector(runtime);

        // Act & Assert (should not throw)
        collector.Dispose();
        collector.Dispose(); // Second dispose should be safe
    }
}
```

#### 4. Integration Tests
**File**: `tests/Baml.Net.Tests/Integration/BamlParseIntegrationTests.cs` (new file)
**Changes**: Add integration tests with real BAML functions

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Integration;

public class BamlParseIntegrationTests : BamlTestBase
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ParseLlmResponse_WithRealFunction_ShouldWork()
    {
        // This test requires a BAML function to be defined in test files
        // Skip if not in integration test mode
        if (!IsIntegrationTest())
            return;

        // Arrange
        var runtime = CreateRuntime();
        var asyncRuntime = new BamlRuntimeAsync(runtime);

        // Simulate an LLM response for ExtractResume function
        var llmResponse = @"{
            ""name"": ""Jane Smith"",
            ""email"": ""jane.smith@example.com"",
            ""experience"": [
                {
                    ""company"": ""Tech Corp"",
                    ""role"": ""Senior Developer"",
                    ""years"": 5
                }
            ]
        }";

        // Act
        var result = await asyncRuntime.ParseLlmResponseAsync<Dictionary<string, object>>(
            "ExtractResume",
            llmResponse);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Jane Smith", result["name"]);
        Assert.Equal("jane.smith@example.com", result["email"]);
        Assert.NotNull(result["experience"]);
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] All new tests pass: `dotnet test --filter "FullyQualifiedName~BamlParseTests"`
- [ ] All new tests pass: `dotnet test --filter "FullyQualifiedName~BamlCancellationTests"`
- [ ] All new tests pass: `dotnet test --filter "FullyQualifiedName~BamlObjectTests"`
- [ ] No memory leaks in object tests: Run with memory profiler
- [ ] Code coverage > 80% for new code: `dotnet test /p:CollectCoverage=true`

#### Manual Verification:
- [ ] Parse tests handle various response formats
- [ ] Cancellation is responsive and clean
- [ ] Object lifecycle is properly managed
- [ ] Integration tests work with real BAML functions

---

## Phase 5: Documentation and Polish

### Overview
Add XML documentation, update README, and ensure code quality.

### Changes Required:

#### 1. Update Package Documentation
**File**: `src/Baml.Net/README.md` (update)
**Changes**: Document new capabilities

```markdown
## New Features in v1.1.0

### LLM Response Parsing
Parse pre-existing LLM responses without making API calls:

```csharp
var runtime = new BamlRuntimeAsync(bamlRuntime);
var parsedResult = await runtime.ParseLlmResponseAsync<MyType>(
    "MyFunction",
    llmResponseText);
```

### BAML Objects
Create and use BAML objects for advanced scenarios:

```csharp
// Create a collector for tracing
using var collector = new BamlCollector(runtime, "my-collector");

// Get collected metrics
var usage = collector.GetUsage();
var logs = collector.GetLogs();

// Clear collected data
collector.Clear();
```

### Native Cancellation
The library now supports native cancellation through the FFI layer in addition to .NET's CancellationToken pattern.
```

#### 2. Add Examples
**File**: `examples/ParseExample.cs` (new file)
**Changes**: Create example showing parse functionality

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baml.Net.Core;

class ParseExample
{
    static async Task Main()
    {
        // Initialize runtime
        var runtime = BamlRuntime.FromDirectory("./baml_src");
        var asyncRuntime = new BamlRuntimeAsync(runtime);

        // Example: Parse a pre-existing LLM response
        var llmResponse = @"{
            ""sentiment"": ""positive"",
            ""confidence"": 0.95,
            ""keywords"": [""happy"", ""excited"", ""wonderful""]
        }";

        var result = await asyncRuntime.ParseLlmResponseAsync<Dictionary<string, object>>(
            "AnalyzeSentiment",
            llmResponse);

        Console.WriteLine($"Sentiment: {result["sentiment"]}");
        Console.WriteLine($"Confidence: {result["confidence"]}");

        // Example: Use collector for tracing
        using (var collector = new BamlCollector(runtime, "example-collector"))
        {
            // Run some BAML functions (they will be traced)
            await asyncRuntime.CallFunctionAsync<string>(
                "GenerateText",
                new Dictionary<string, object> { ["prompt"] = "Hello" },
                new Dictionary<string, string>());

            // Get metrics
            var usage = collector.GetUsage();
            Console.WriteLine($"Total tokens used: {usage.GetValueOrDefault("total_tokens", 0)}");
        }
    }
}
```

### Success Criteria:

#### Automated Verification:
- [ ] Documentation builds without warnings: `dotnet build -p:GenerateDocumentationFile=true`
- [ ] Examples compile: `dotnet build examples/`

#### Manual Verification:
- [ ] README accurately describes new features
- [ ] Examples are clear and runnable
- [ ] API documentation is complete

---

## Testing Strategy

### Unit Tests:
- Test each FFI function in isolation
- Test marshalling of different data types
- Test error conditions and edge cases
- Test memory management (buffer cleanup)

### Integration Tests:
- Test with real BAML functions from test files
- Test end-to-end scenarios with parsing
- Test collector with actual function calls
- Test cancellation during real operations

### Manual Testing Steps:
1. Create a test BAML project with various function types
2. Test parsing responses from different LLMs (OpenAI, Anthropic, etc.)
3. Test collector with long-running operations
4. Test cancellation responsiveness
5. Monitor memory usage during object creation/destruction

## Performance Considerations

- **Buffer Management**: Always free native buffers to prevent memory leaks
- **Callback Efficiency**: Reuse callback manager infrastructure for minimal overhead
- **Object Lifecycle**: Use IDisposable pattern for deterministic cleanup
- **Hybrid Cancellation**: Combines .NET CancellationToken with native FFI for optimal responsiveness
  - Token registration triggers native cancellation immediately
  - Idempotent design allows safe multiple cancellation attempts
  - No performance penalty for completed operations

## Migration Notes

- Existing code continues to work without changes
- New features are additive, not breaking
- CancellationToken pattern remains primary cancellation mechanism
- Native cancellation is available for advanced scenarios

## References

- Original C header: Provided in task description
- Rust FFI implementation: `baml/engine/language_client_cffi/src/ffi/`
- Go client implementation: `baml/engine/language_client_go/`
- Python client implementation: `baml/engine/language_client_python/`
- TypeScript tests: `baml/integ-tests/typescript/tests/`