using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baml.Net.FFI;

namespace Baml.Net.Core;

/// <summary>
/// Represents a BAML runtime instance for executing BAML functions.
/// </summary>
public sealed class BamlRuntime : IDisposable
{
    private IntPtr _runtimeHandle;
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>
    /// Gets whether the runtime has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    private BamlRuntime(IntPtr runtimeHandle)
    {
        _runtimeHandle = runtimeHandle;

        // Ensure callbacks are registered
        BamlCallbackManager.EnsureCallbacksRegistered();
    }

    /// <summary>
    /// Creates a BAML runtime from a directory containing BAML files.
    /// </summary>
    /// <param name="directory">Path to directory containing BAML files.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <returns>A new BAML runtime instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when directory is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when directory doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when runtime creation fails.</exception>
    public static BamlRuntime FromDirectory(string directory, Dictionary<string, string>? envVars = null)
    {
        if (directory == null)
            throw new ArgumentNullException(nameof(directory));

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        // Load all BAML files from directory
        var files = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(directory, "*.baml", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(directory, file);
            files[relativePath] = File.ReadAllText(file);
        }

        return FromFiles(directory, files, envVars);
    }

    /// <summary>
    /// Creates a BAML runtime from a collection of files.
    /// </summary>
    /// <param name="rootPath">Root path for the project.</param>
    /// <param name="files">Dictionary of file paths to content.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <returns>A new BAML runtime instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when rootPath or files is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when runtime creation fails.</exception>
    public static BamlRuntime FromFiles(
        string rootPath,
        Dictionary<string, string> files,
        Dictionary<string, string>? envVars = null)
    {
        if (rootPath == null)
            throw new ArgumentNullException(nameof(rootPath));
        if (files == null)
            throw new ArgumentNullException(nameof(files));

        // Serialize files and environment variables to JSON
        var srcFilesJson = JsonSerializer.Serialize(files);
        var envVarsJson = JsonSerializer.Serialize(envVars ?? new Dictionary<string, string>());

        // Create runtime using FFI helper
        var runtimeHandle = BamlNativeHelpers.CreateRuntime(rootPath, srcFilesJson, envVarsJson);

        return new BamlRuntime(runtimeHandle);
    }

    /// <summary>
    /// Invokes the BAML CLI with the provided arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code from the CLI invocation.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    public int InvokeCli(params string[] args)
    {
        ThrowIfDisposed();
        return BamlNativeHelpers.InvokeRuntimeCli(args);
    }

    /// <summary>
    /// Calls a BAML function synchronously.
    /// </summary>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="args">Function arguments (will be serialized to protobuf).</param>
    /// <returns>Raw result buffer from the function call.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when functionName is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when function call fails.</exception>
    public byte[] CallFunction(string functionName, byte[] args)
    {
        ThrowIfDisposed();

        if (functionName == null)
            throw new ArgumentNullException(nameof(functionName));

        // Use async version and wait synchronously
        // This is acceptable for the synchronous API
        return CallFunctionAsync(functionName, args, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Calls a BAML function asynchronously.
    /// </summary>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="args">Function arguments (will be serialized to protobuf).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that resolves to the raw result buffer from the function call.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when functionName is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when function call fails.</exception>
    public async Task<byte[]> CallFunctionAsync(string functionName, byte[] args, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (functionName == null)
            throw new ArgumentNullException(nameof(functionName));

        return await BamlNativeHelpers.CallFunctionAsync(_runtimeHandle, functionName, args, cancellationToken);
    }

    /// <summary>
    /// Calls a BAML function with streaming support.
    /// </summary>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="args">Function arguments (will be serialized to protobuf).</param>
    /// <param name="onChunk">Callback for receiving streaming chunks.</param>
    /// <returns>Final result buffer from the function call.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when functionName or onChunk is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when function call fails.</exception>
    public byte[] CallFunctionStream(string functionName, byte[] args, Action<byte[]> onChunk)
    {
        ThrowIfDisposed();

        if (functionName == null)
            throw new ArgumentNullException(nameof(functionName));
        if (onChunk == null)
            throw new ArgumentNullException(nameof(onChunk));

        // Use callback manager to handle streaming chunks
        var (callbackId, callbackTask) = BamlCallbackManager.CreateStreamingCallback(onChunk);

        // Call the native streaming function - it uses global registered callbacks
        var resultBuffer = BamlNativeHelpers.CallFunctionStream(
            _runtimeHandle,
            functionName,
            args,
            callbackId);

        // Wait for streaming to complete (synchronously)
        // The actual chunks come via the global registered callbacks
        var result = callbackTask.GetAwaiter().GetResult();

        // Return the final result
        return result;
    }

    /// <summary>
    /// Parses an LLM response without making an actual LLM call.
    /// Used for testing and replay scenarios.
    /// </summary>
    /// <param name="functionName">Name of the function to parse for.</param>
    /// <param name="args">Function arguments including the text to parse (will be serialized to protobuf).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that resolves to the parsed result as a raw buffer.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when functionName is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when parsing fails.</exception>
    public async Task<byte[]> ParseAsync(string functionName, byte[] args, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (functionName == null)
            throw new ArgumentNullException(nameof(functionName));

        return await BamlNativeHelpers.CallFunctionParseAsync(_runtimeHandle, functionName, args, cancellationToken);
    }

    /// <summary>
    /// Creates a BAML collector object for aggregating LLM responses.
    /// </summary>
    /// <param name="args">Constructor arguments (will be serialized to protobuf).</param>
    /// <returns>The constructed collector object as a raw buffer.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when object construction fails.</exception>
    public byte[] CreateCollector(byte[] args)
    {
        ThrowIfDisposed();
        return BamlNativeHelpers.CallObjectConstructor(args);
    }

    /// <summary>
    /// Creates a BAML type builder object for dynamic type construction.
    /// </summary>
    /// <param name="args">Constructor arguments (will be serialized to protobuf).</param>
    /// <returns>The constructed type builder object as a raw buffer.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when object construction fails.</exception>
    public byte[] CreateTypeBuilder(byte[] args)
    {
        ThrowIfDisposed();
        return BamlNativeHelpers.CallObjectConstructor(args);
    }

    /// <summary>
    /// Invokes a method on a BAML object (collector, type builder, etc.).
    /// </summary>
    /// <param name="methodArgs">Method arguments including the object reference (will be serialized to protobuf).</param>
    /// <returns>The method result as a raw buffer.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when runtime is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when method invocation fails.</exception>
    public byte[] InvokeObjectMethod(byte[] methodArgs)
    {
        ThrowIfDisposed();
        return BamlNativeHelpers.CallObjectMethod(_runtimeHandle, methodArgs);
    }

    /// <summary>
    /// Disposes the runtime and frees associated resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            if (_runtimeHandle != IntPtr.Zero)
            {
                BamlNative.DestroyRuntime(_runtimeHandle);
                _runtimeHandle = IntPtr.Zero;
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BamlRuntime));
    }

    /// <summary>
    /// Finalizer that ensures native resources are cleaned up if Dispose() was not called.
    /// </summary>
    ~BamlRuntime()
    {
        Dispose();
    }
}
