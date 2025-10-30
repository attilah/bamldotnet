using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Baml.Net.FFI;

/// <summary>
/// Helper methods for working with BAML native FFI functions.
/// Provides managed wrappers around raw P/Invoke calls.
/// </summary>
internal static class BamlNativeHelpers
{
    /// <summary>
    /// Creates a BAML runtime instance.
    /// </summary>
    /// <param name="rootPath">Root path for the BAML project.</param>
    /// <param name="srcFilesJson">JSON string containing source files map (path -> content).</param>
    /// <param name="envVarsJson">JSON string containing environment variables.</param>
    /// <returns>Handle to the runtime instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when runtime creation fails.</exception>
    public static IntPtr CreateRuntime(string rootPath, string srcFilesJson, string envVarsJson)
    {
        var runtime = BamlNative.CreateRuntime(rootPath, srcFilesJson, envVarsJson);

        if (runtime == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create BAML runtime - returned null handle");
        }

        return runtime;
    }

    /// <summary>
    /// Calls a BAML function asynchronously using callbacks.
    /// </summary>
    /// <param name="runtime">Handle to the runtime instance.</param>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="argsData">Protobuf-encoded arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that resolves to the result buffer containing protobuf-encoded response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when function call fails.</exception>
    public static async Task<byte[]> CallFunctionAsync(
        IntPtr runtime,
        string functionName,
        byte[] argsData,
        CancellationToken cancellationToken = default)
    {
        // Create callback and get task
        var (callbackId, task) = BamlCallbackManager.CreateCallback(cancellationToken);

        IntPtr functionNamePtr = IntPtr.Zero;
        IntPtr argsPtr = IntPtr.Zero;

        try
        {
            // Marshal function name as null-terminated C string
            // Rust uses CStr::from_ptr which expects null termination
            var functionNameBytes = Encoding.UTF8.GetBytes(functionName + '\0');
            functionNamePtr = Marshal.AllocHGlobal(functionNameBytes.Length);
            Marshal.Copy(functionNameBytes, 0, functionNamePtr, functionNameBytes.Length);

            // Marshal arguments
            argsPtr = Marshal.AllocHGlobal(argsData.Length);
            Marshal.Copy(argsData, 0, argsPtr, argsData.Length);

            // Call the native function (returns NULL on success, error string on failure)
            var errorPtr = BamlNative.CallFunction(
                runtime,
                functionNamePtr,
                argsPtr,
                (nuint)argsData.Length,
                callbackId);

            // The function returns NULL on success (async task spawned), error pointer on failure
            if (errorPtr != IntPtr.Zero)
            {
                var error = Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error";
                throw new InvalidOperationException($"Failed to call function '{functionName}': {error}");
            }

            // Wait for async result via callback
            return await task;
        }
        finally
        {
            if (functionNamePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(functionNamePtr);
            if (argsPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(argsPtr);
        }
    }

    /// <summary>
    /// Calls a BAML function with streaming support.
    /// The native function uses global registered callbacks for streaming chunks.
    /// </summary>
    /// <param name="runtime">Handle to the runtime instance.</param>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="argsData">Protobuf-encoded arguments.</param>
    /// <param name="callbackId">Callback identifier for async operations.</param>
    /// <returns>Result buffer containing protobuf-encoded response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when function call fails.</exception>
    public static BamlNative.Buffer CallFunctionStream(
        IntPtr runtime,
        string functionName,
        byte[] argsData,
        uint callbackId = 0)
    {
        IntPtr functionNamePtr = IntPtr.Zero;
        IntPtr argsPtr = IntPtr.Zero;

        try
        {
            // Marshal function name with null terminator (like CallFunction)
            var functionNameBytes = Encoding.UTF8.GetBytes(functionName + '\0');
            functionNamePtr = Marshal.AllocHGlobal(functionNameBytes.Length);
            Marshal.Copy(functionNameBytes, 0, functionNamePtr, functionNameBytes.Length);

            // Marshal arguments
            argsPtr = Marshal.AllocHGlobal(argsData.Length);
            Marshal.Copy(argsData, 0, argsPtr, argsData.Length);

            // Call the native function with the same signature as CallFunction
            // The native function uses global registered callbacks for streaming
            var resultPtr = BamlNative.CallFunctionStream(
                runtime,
                functionNamePtr,
                argsPtr,
                (nuint)argsData.Length,
                callbackId);

            // The function returns NULL on success (async task spawned), error pointer on failure
            if (resultPtr != IntPtr.Zero)
            {
                var error = Marshal.PtrToStringUTF8(resultPtr) ?? "Unknown error";
                throw new InvalidOperationException($"Failed to call streaming function '{functionName}': {error}");
            }

            // For streaming, return empty buffer as the actual data comes via callbacks
            return new BamlNative.Buffer { Data = IntPtr.Zero, Length = 0 };
        }
        finally
        {
            if (functionNamePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(functionNamePtr);
            if (argsPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(argsPtr);
        }
    }

    /// <summary>
    /// Reads data from a native buffer.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>Byte array containing the buffer data.</returns>
    public static byte[] ReadBuffer(BamlNative.Buffer buffer)
    {
        if (buffer.Data == IntPtr.Zero || buffer.Length == 0)
            return Array.Empty<byte>();

        var data = new byte[(int)buffer.Length];
        Marshal.Copy(buffer.Data, data, 0, data.Length);
        return data;
    }

    /// <summary>
    /// Invokes the BAML CLI with the provided arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code from the CLI invocation.</returns>
    public static int InvokeRuntimeCli(string[] args)
    {
        // Allocate array of string pointers
        var argPtrs = new IntPtr[args.Length];
        var argArrayPtr = IntPtr.Zero;

        try
        {
            // Marshal each string
            for (int i = 0; i < args.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(args[i]);
                argPtrs[i] = Marshal.AllocHGlobal(bytes.Length + 1); // +1 for null terminator
                Marshal.Copy(bytes, 0, argPtrs[i], bytes.Length);
                Marshal.WriteByte(argPtrs[i], bytes.Length, 0); // null terminator
            }

            // Marshal array of pointers
            argArrayPtr = Marshal.AllocHGlobal(IntPtr.Size * args.Length);
            Marshal.Copy(argPtrs, 0, argArrayPtr, args.Length);

            return BamlNative.InvokeRuntimeCli(argArrayPtr, args.Length);
        }
        finally
        {
            // Free all allocated memory
            foreach (var ptr in argPtrs)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }

            if (argArrayPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(argArrayPtr);
        }
    }
}
