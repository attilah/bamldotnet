# BamlCallbackManager Improvement Implementation Plan

## Overview

Improve the BamlCallbackManager to follow .NET best practices for thread-safe callback ID generation and proper error propagation through TaskCompletionSource, based on feedback and reference implementation patterns.

## Current State Analysis

The current `BamlCallbackManager` in `src/Baml.Net/FFI/BamlCallbackManager.cs` has several areas that need improvement:

1. **Callback ID Generation**: Currently uses a lock statement for incrementing callback IDs (lines 16-17, 64-67)
2. **Error Handling**: Uses `Console.Error.WriteLine` for error logging instead of proper exception propagation (lines 152, 183, 202, 283)
3. **TaskCompletionSource Usage**: Not consistently using `Try*` methods for setting results/exceptions
4. **Cancellation Handling**: Could be improved with linked cancellation tokens for timeout support
5. **AOT Compatibility**: Missing `MonoPInvokeCallback` attribute for AOT scenarios

### Key Discoveries:
- The callback system bridges async/await .NET patterns with native FFI callbacks
- Supports both single-result and streaming operations
- Used by `BamlRuntime.cs:30` and `BamlRuntime.cs:163` for runtime operations
- Tests expect proper exception propagation (not console output)

## Desired End State

After implementation, the BamlCallbackManager should:
- Use `Interlocked.Increment` for thread-safe callback ID generation without locking
- Properly propagate all errors through `TaskCompletionSource.TrySetException`
- Remove all `Console.Error.WriteLine` calls
- Support optional timeout functionality with linked cancellation tokens
- Include AOT compatibility attributes for iOS/Unity scenarios
- Maintain full backward compatibility with existing code

### Verification:
- All existing tests pass without modification
- Error scenarios properly throw exceptions (no console output)
- Thread-safe callback ID generation under concurrent load
- Cancellation and timeout scenarios work correctly

## What We're NOT Doing

- Changing the public API surface of BamlCallbackManager
- Modifying the native FFI layer (BamlNative.cs)
- Altering the protobuf communication protocol
- Changing how streaming callbacks work fundamentally
- Adding new dependencies or NuGet packages

## Implementation Approach

We'll implement improvements in a single phase since the changes are interconnected and relatively straightforward. The modifications will be purely internal to the BamlCallbackManager class, ensuring no breaking changes to consumers.

## Phase 1: Core Improvements to BamlCallbackManager

### Overview
Replace lock-based callback ID generation with Interlocked operations, improve error handling throughout, and add timeout support following the reference implementation pattern.

### Changes Required:

#### 1. Update Callback ID Generation
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Replace lock-based increment with Interlocked.Increment

Current code (lines 16-17, 64-67):
```csharp
private static uint _nextCallbackId = 1;
private static readonly object _callbackIdLock = new object();

// In CreateCallback method:
uint callbackId;
lock (_callbackIdLock)
{
    callbackId = _nextCallbackId++;
}
```

New code:
```csharp
private static int _nextCallbackId;

// In CreateCallback method:
var callbackId = (uint)Interlocked.Increment(ref _nextCallbackId);
```

#### 2. Enhance CreateCallback with Timeout Support
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Add timeout parameter and linked cancellation token support

Update method signature and implementation:
```csharp
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
            // Extremely unlikely but handle gracefully
            throw new InvalidOperationException("Failed to track callback request.");
        }

        // Handle cancellation
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
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
```

#### 3. Fix Error Propagation in Callbacks
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Replace Console.WriteLine with proper exception handling

Update HandleResultCallback (lines 120-154):
```csharp
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
private static void HandleResultCallback(uint callbackId, int isDone, IntPtr content, nuint length)
{
    try
    {
        if (!_pendingCallbacks.TryGetValue(callbackId, out var context))
        {
            // Callback for unknown ID - might have been cancelled
            // Drop on floor - no console output
            return;
        }

        // Copy the data from native memory
        var data = new byte[(int)length];
        if (content != IntPtr.Zero && length > 0)
        {
            Marshal.Copy(content, data, 0, (int)length);
        }

        if (isDone != 0)
        {
            // Final result - complete the task
            _pendingCallbacks.TryRemove(callbackId, out _);
            context.TrySetResult(data);
        }
        else
        {
            // Streaming update
            context.AddStreamingChunk(data);
        }
    }
    catch (Exception ex)
    {
        // Try to propagate exception to the task if possible
        if (_pendingCallbacks.TryRemove(callbackId, out var context))
        {
            context.TrySetException(ex);
        }
        // Otherwise drop on floor - callback already completed
    }
}
```

Update HandleErrorCallback (lines 159-185):
```csharp
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
private static void HandleErrorCallback(uint callbackId, int isDone, IntPtr content, nuint length)
{
    if (!_pendingCallbacks.TryRemove(callbackId, out var context))
    {
        // Callback for unknown ID - might have been cancelled
        // Drop on floor - no console output
        return;
    }

    // Extract error message
    string errorMessage = "Unknown error";
    if (content != IntPtr.Zero && length > 0)
    {
        try
        {
            var errorBytes = new byte[(int)length];
            Marshal.Copy(content, errorBytes, 0, (int)length);
            errorMessage = Encoding.UTF8.GetString(errorBytes);
        }
        catch
        {
            // Keep default error message if marshaling fails
        }
    }

    context.TrySetException(new InvalidOperationException($"BAML function call failed: {errorMessage}"));
}
```

Update HandleOnTickCallback (lines 190-204):
```csharp
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
private static void HandleOnTickCallback(uint callbackId)
{
    if (_pendingCallbacks.TryGetValue(callbackId, out var context))
    {
        try
        {
            context.OnTick();
        }
        catch (Exception ex)
        {
            // If OnTick throws, fail the entire operation
            if (_pendingCallbacks.TryRemove(callbackId, out var ctx))
            {
                ctx.TrySetException(ex);
            }
        }
    }
    // else: late or duplicate callback - drop on floor
}
```

#### 4. Update StreamingCallbackContext
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Remove Console.WriteLine from streaming chunk handler

Update AddStreamingChunk override (lines 271-285):
```csharp
public override void AddStreamingChunk(byte[] chunk)
{
    base.AddStreamingChunk(chunk);

    // Invoke the callback with the chunk
    try
    {
        _onChunk(chunk);
    }
    catch (Exception ex)
    {
        // If the chunk handler throws, propagate to task
        TrySetException(new InvalidOperationException(
            "Streaming chunk handler threw an exception", ex));
    }
}
```

#### 5. Update CreateStreamingCallback
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Use Interlocked for callback ID

Update method (lines 90-104):
```csharp
public static (uint callbackId, Task<byte[]> task) CreateStreamingCallback(Action<byte[]> onChunk)
{
    EnsureCallbacksRegistered();

    var callbackId = (uint)Interlocked.Increment(ref _nextCallbackId);
    var context = new StreamingCallbackContext(callbackId, onChunk);

    if (!_pendingCallbacks.TryAdd(callbackId, context))
    {
        throw new InvalidOperationException("Failed to track streaming callback request.");
    }

    return (callbackId, context.Task);
}
```

#### 6. Update CallbackContext Constructor
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Improve cancellation token registration

Update constructor (lines 215-224):
```csharp
public CallbackContext(uint callbackId, CancellationToken cancellationToken)
{
    _tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

    // No need to register here - already handled in CreateCallback
}

public bool TrySetCanceled(CancellationToken cancellationToken)
{
    return _tcs.TrySetCanceled(cancellationToken);
}
```

#### 7. Add Using Statement for AOT
**File**: `src/Baml.Net/FFI/BamlCallbackManager.cs`
**Changes**: Add using statement at the top of the file

Add after existing using statements:
```csharp
using System.Runtime.CompilerServices;
```

### Success Criteria:

#### Automated Verification:
- [x] Solution builds successfully: `dotnet build`
- [x] All unit tests pass: `dotnet test`
- [x] No compiler warnings about obsolete methods
- [ ] Thread safety test passes (if added)

#### Manual Verification:
- [ ] Error scenarios properly throw exceptions (no console output)
- [ ] Streaming callbacks continue to work correctly
- [ ] Cancellation tokens properly cancel operations
- [ ] Timeout functionality works as expected
- [ ] No memory leaks under stress testing

---

## Testing Strategy

### Unit Tests:
- Verify Interlocked.Increment produces unique IDs under concurrent access
- Test cancellation token behavior
- Test timeout functionality
- Verify proper exception propagation
- Test streaming callback error handling

### Integration Tests:
- Verify existing BAML function calls still work
- Test error scenarios with invalid function names
- Test streaming operations with various data sizes
- Verify proper cleanup on cancellation

### Manual Testing Steps:
1. Run existing test suite to ensure no regressions
2. Test concurrent callback creation under load
3. Verify error messages are properly propagated (not logged to console)
4. Test timeout scenarios with slow operations
5. Verify streaming operations continue to work

## Performance Considerations

- **Interlocked.Increment** is more efficient than lock-based increment
- **TaskCreationOptions.RunContinuationsAsynchronously** prevents deadlocks
- Linked cancellation token sources are properly disposed to prevent leaks
- No additional allocations in hot paths

## Migration Notes

No migration needed - all changes are internal and maintain backward compatibility. Existing code will benefit from improvements without modification.

## References

- Original file: `src/Baml.Net/FFI/BamlCallbackManager.cs`
- Native interop: `src/Baml.Net/FFI/BamlNative.cs`
- Runtime usage: `src/Baml.Net/Core/BamlRuntime.cs:30,163`
- Test coverage: `tests/Baml.Net.Tests/Core/BamlRuntimeAsyncTests.cs`
