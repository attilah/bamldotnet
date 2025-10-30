using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Baml.Net.FFI;

/// <summary>
/// Manages callbacks from the native BAML library.
/// Provides async/await support for callback-based FFI calls.
/// </summary>
internal static class BamlCallbackManager
{
    private static int _nextCallbackId;
    private static readonly ConcurrentDictionary<uint, CallbackContext> _pendingCallbacks = new();
    private static bool _callbacksRegistered = false;
    private static readonly object _registrationLock = new object();

    // Keep delegates alive to prevent GC collection
    private static BamlNative.ResultCallback? _resultCallbackDelegate;
    private static BamlNative.ErrorCallback? _errorCallbackDelegate;
    private static BamlNative.OnTickCallback? _onTickCallbackDelegate;

    /// <summary>
    /// Registers callbacks with the native library.
    /// Must be called once before making any function calls.
    /// </summary>
    public static void EnsureCallbacksRegistered()
    {
        if (_callbacksRegistered)
            return;

        lock (_registrationLock)
        {
            if (_callbacksRegistered)
                return;

            // Create delegates and keep them alive
            _resultCallbackDelegate = HandleResultCallback;
            _errorCallbackDelegate = HandleErrorCallback;
            _onTickCallbackDelegate = HandleOnTickCallback;

            // Register with native library
            BamlNative.RegisterCallbacks(
                _resultCallbackDelegate,
                _errorCallbackDelegate,
                _onTickCallbackDelegate);

            _callbacksRegistered = true;
        }
    }

    /// <summary>
    /// Creates a new callback ID and context for an async operation.
    /// </summary>
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
                    // Call native cancellation function
                    BamlNative.CancelFunctionCall(callbackId);

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

    /// <summary>
    /// Creates a new callback ID and context for a streaming operation.
    /// </summary>
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

    /// <summary>
    /// Handles on-tick events for a specific callback.
    /// </summary>
    public static void HandleOnTick(uint callbackId)
    {
        if (_pendingCallbacks.TryGetValue(callbackId, out var context))
        {
            context.OnTick();
        }
    }

    /// <summary>
    /// Cancels a pending callback operation.
    /// This calls the native cancellation function and cleans up the callback.
    /// </summary>
    /// <param name="callbackId">The callback ID to cancel.</param>
    /// <returns>True if the callback was found and cancelled, false otherwise.</returns>
    public static bool CancelCallback(uint callbackId)
    {
        // Call native cancellation function
        BamlNative.CancelFunctionCall(callbackId);

        // Remove and cancel the pending callback
        if (_pendingCallbacks.TryRemove(callbackId, out var context))
        {
            context.TrySetCanceled(CancellationToken.None);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles result callbacks from the native library.
    /// </summary>
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

    /// <summary>
    /// Handles error callbacks from the native library.
    /// </summary>
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

    /// <summary>
    /// Handles on-tick callbacks for streaming operations.
    /// </summary>
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

    /// <summary>
    /// Context for a pending callback operation.
    /// </summary>
    private class CallbackContext
    {
        private readonly TaskCompletionSource<byte[]> _tcs;
        private readonly List<byte[]> _streamingChunks = new();
        private readonly object _lock = new object();

        public CallbackContext(uint callbackId, CancellationToken cancellationToken)
        {
            _tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            // No need to register here - already handled in CreateCallback
        }

        public Task<byte[]> Task => _tcs.Task;

        public virtual void AddStreamingChunk(byte[] chunk)
        {
            lock (_lock)
            {
                _streamingChunks.Add(chunk);
            }
        }

        public virtual void OnTick()
        {
            // Hook for streaming progress notifications
            // Could emit events here if needed
        }

        public bool TrySetResult(byte[] result)
        {
            return _tcs.TrySetResult(result);
        }

        public bool TrySetException(Exception exception)
        {
            return _tcs.TrySetException(exception);
        }

        public bool TrySetCanceled(CancellationToken cancellationToken)
        {
            return _tcs.TrySetCanceled(cancellationToken);
        }
    }

    /// <summary>
    /// Context for a streaming callback operation.
    /// </summary>
    private class StreamingCallbackContext : CallbackContext
    {
        private readonly Action<byte[]> _onChunk;

        public StreamingCallbackContext(uint callbackId, Action<byte[]> onChunk)
            : base(callbackId, CancellationToken.None)
        {
            _onChunk = onChunk ?? throw new ArgumentNullException(nameof(onChunk));
        }

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
    }
}
