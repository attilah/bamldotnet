using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Baml.Net.Extensions;
using Google.Protobuf;

namespace Baml.Net.Core;

/// <summary>
/// Provides async/await wrappers for BamlRuntime operations.
/// This class wraps a synchronous BamlRuntime instance and provides
/// Task-based asynchronous methods for better integration with modern C# async patterns.
/// </summary>
public sealed class BamlRuntimeAsync : IDisposable
{
    private readonly BamlRuntime _runtime;
    private readonly bool _ownsRuntime;

    /// <summary>
    /// Creates a new BamlRuntimeAsync wrapper around an existing BamlRuntime.
    /// </summary>
    /// <param name="runtime">The BamlRuntime instance to wrap.</param>
    /// <param name="ownsRuntime">Whether this instance should dispose the runtime when disposed.</param>
    /// <exception cref="ArgumentNullException">Thrown when runtime is null.</exception>
    public BamlRuntimeAsync(BamlRuntime runtime, bool ownsRuntime = false)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ownsRuntime = ownsRuntime;
    }

    /// <summary>
    /// Gets the underlying BamlRuntime instance.
    /// </summary>
    public BamlRuntime Runtime => _runtime;

    /// <summary>
    /// Calls a BAML function asynchronously.
    /// </summary>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="args">Dictionary of argument name to value.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Task that resolves to the raw result buffer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when functionName is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the underlying runtime is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the function call fails.</exception>
    public Task<byte[]> CallFunctionAsync(
        string functionName,
        Dictionary<string, object>? args = null,
        Dictionary<string, string>? envVars = null,
        CancellationToken cancellationToken = default)
    {
        if (functionName == null)
            throw new ArgumentNullException(nameof(functionName));

        // Check cancellation before starting work
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<byte[]>(cancellationToken);
        }

        // Run the synchronous operation on a background thread
        return Task.Run(async () =>
        {
            // Check cancellation again before doing work
            cancellationToken.ThrowIfCancellationRequested();

            // Invoke request interceptors
            var argsDict = args ?? new Dictionary<string, object>();
            await BamlContext.Current.InvokeRequestInterceptors(functionName, argsDict);

            // Convert args to protobuf
            var functionArgs = argsDict.ToFunctionArguments(envVars);

            // Serialize to protobuf bytes
            byte[] argsBytes;
            using (var stream = new System.IO.MemoryStream())
            {
                functionArgs.WriteTo(stream);
                argsBytes = stream.ToArray();
            }

            // Call the synchronous function
            var result = _runtime.CallFunction(functionName, argsBytes);

            // Invoke response interceptors
            await BamlContext.Current.InvokeResponseInterceptors(functionName, result);

            // Check cancellation before returning
            cancellationToken.ThrowIfCancellationRequested();

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Calls a BAML function asynchronously and deserializes the result to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to.</typeparam>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="args">Dictionary of argument name to value.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Task that resolves to the deserialized result of type T.</returns>
    /// <exception cref="ArgumentNullException">Thrown when functionName is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the underlying runtime is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the function call fails.</exception>
    /// <exception cref="JsonException">Thrown when JSON deserialization fails.</exception>
    public async Task<T> CallFunctionAsync<T>(
        string functionName,
        Dictionary<string, object>? args = null,
        Dictionary<string, string>? envVars = null,
        CancellationToken cancellationToken = default)
    {
        // Call the non-generic version to get raw bytes
        var resultBytes = await CallFunctionAsync(functionName, args, envVars, cancellationToken);

        // Parse the protobuf response
        var valueHolder = Baml.Cffi.CFFIValueHolder.Parser.ParseFrom(resultBytes);

        // Convert the protobuf value to a C# object
        var resultObject = valueHolder.ToObject();

        // Special handling for primitive types
        if (typeof(T) == typeof(string) && resultObject is string stringResult)
        {
            return (T)(object)stringResult;
        }
        if (typeof(T) == typeof(int) && resultObject is long longResult)
        {
            return (T)(object)(int)longResult;
        }
        if (typeof(T) == typeof(bool) && resultObject is bool boolResult)
        {
            return (T)(object)boolResult;
        }
        if (typeof(T) == typeof(double) && resultObject is double doubleResult)
        {
            return (T)(object)doubleResult;
        }
        if (typeof(T) == typeof(float) && resultObject is double floatSourceResult)
        {
            return (T)(object)(float)floatSourceResult;
        }

        // For complex types, serialize the object to JSON then deserialize to target type
        // This allows us to leverage JSON property mapping attributes
        var jsonString = JsonSerializer.Serialize(resultObject);

        // Use System.Text.Json with options that support JsonPropertyName attributes
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var result = JsonSerializer.Deserialize<T>(jsonString, options);

        if (result == null)
        {
            throw new InvalidOperationException($"Failed to deserialize response to type {typeof(T).Name}. Response was null or could not be converted.");
        }

        return result;
    }

    /// <summary>
    /// Calls a BAML function with streaming support and yields deserialized chunks.
    /// </summary>
    /// <typeparam name="T">The type to deserialize each chunk to.</typeparam>
    /// <param name="functionName">Name of the function to call.</param>
    /// <param name="args">Dictionary of argument name to value.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>IAsyncEnumerable that yields deserialized chunks of type T.</returns>
    /// <exception cref="ArgumentNullException">Thrown when functionName is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the underlying runtime is disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the function call fails.</exception>
    /// <exception cref="JsonException">Thrown when JSON deserialization fails.</exception>
    public async IAsyncEnumerable<T> CallFunctionStream<T>(
        string functionName,
        Dictionary<string, object>? args = null,
        Dictionary<string, string>? envVars = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (functionName == null)
            throw new ArgumentNullException(nameof(functionName));

        // Create a channel to communicate between the callback and the async enumerable
        var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        // JSON serializer options (used for complex type conversion)
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Start the streaming operation on a background thread
        var streamTask = Task.Run(async () =>
        {
            try
            {
                // WORKAROUND: Since CallFunctionStream doesn't accept envVars yet,
                // we'll temporarily set them as environment variables
                var originalValues = new Dictionary<string, string?>();
                if (envVars != null)
                {
                    foreach (var kvp in envVars)
                    {
                        originalValues[kvp.Key] = Environment.GetEnvironmentVariable(kvp.Key);
                        Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                    }
                }

                try
                {
                    // Convert args to protobuf
                    var functionArgs = (args ?? new Dictionary<string, object>())
                        .ToFunctionArguments(envVars);

                    // Serialize to protobuf bytes
                    byte[] argsBytes;
                    using (var stream = new System.IO.MemoryStream())
                    {
                        functionArgs.WriteTo(stream);
                        argsBytes = stream.ToArray();
                    }

                    // Call the synchronous streaming function with callback
                    var finalResult = _runtime.CallFunctionStream(functionName, argsBytes, chunkBytes =>
                    {
                        // Write each chunk to the channel
                        channel.Writer.TryWrite(chunkBytes);
                    });

                    // Signal completion
                    channel.Writer.Complete();
                }
                finally
                {
                    // Restore original environment variables
                    if (envVars != null)
                    {
                        foreach (var kvp in originalValues)
                        {
                            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Signal error
                channel.Writer.Complete(ex);
            }
        }, cancellationToken);

        // Read from the channel and yield deserialized chunks
        await foreach (var chunkBytes in channel.Reader.ReadAllAsync(cancellationToken))
        {
            // Parse the protobuf response
            Baml.Cffi.CFFIValueHolder valueHolder;
            try
            {
                valueHolder = Baml.Cffi.CFFIValueHolder.Parser.ParseFrom(chunkBytes);
            }
            catch (Exception ex)
            {
                // Log parsing error but continue processing other chunks
                Console.Error.WriteLine($"Failed to parse streaming chunk: {ex.Message}");
                continue;
            }

            // Convert the protobuf value to a C# object
            // The ToObject() extension now handles StreamingStateValue
            var chunkObject = valueHolder.ToObject();

            // Handle primitive types with direct casting
            if (typeof(T) == typeof(string) && chunkObject is string stringChunk)
            {
                yield return (T)(object)stringChunk;
            }
            else if (typeof(T) == typeof(int) && chunkObject is long longChunk)
            {
                yield return (T)(object)(int)longChunk;
            }
            else if (typeof(T) == typeof(bool) && chunkObject is bool boolChunk)
            {
                yield return (T)(object)boolChunk;
            }
            else if (typeof(T) == typeof(double) && chunkObject is double doubleChunk)
            {
                yield return (T)(object)doubleChunk;
            }
            else if (typeof(T) == typeof(float) && chunkObject is double floatSourceChunk)
            {
                yield return (T)(object)(float)floatSourceChunk;
            }
            else if (chunkObject != null)
            {
                // For complex types, use JSON as intermediate format
                T? item = default;
                bool hasError = false;

                try
                {
                    var jsonString = JsonSerializer.Serialize(chunkObject);
                    item = JsonSerializer.Deserialize<T>(jsonString, jsonOptions);
                }
                catch (JsonException ex)
                {
                    // Log deserialization error but continue processing
                    Console.Error.WriteLine($"Failed to deserialize chunk to type {typeof(T).Name}: {ex.Message}");
                    hasError = true;
                }

                if (!hasError && item != null)
                {
                    yield return item;
                }
            }
        }

        // Wait for the streaming task to complete
        await streamTask;
    }

    /// <summary>
    /// Creates a BamlRuntimeAsync from a directory containing BAML files.
    /// </summary>
    /// <param name="directory">Path to directory containing BAML files.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <returns>A new BamlRuntimeAsync instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when directory is null.</exception>
    /// <exception cref="System.IO.DirectoryNotFoundException">Thrown when directory doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when runtime creation fails.</exception>
    public static BamlRuntimeAsync FromDirectory(string directory, Dictionary<string, string>? envVars = null)
    {
        var runtime = BamlRuntime.FromDirectory(directory, envVars);
        return new BamlRuntimeAsync(runtime, ownsRuntime: true);
    }

    /// <summary>
    /// Creates a BamlRuntimeAsync from a collection of files.
    /// </summary>
    /// <param name="rootPath">Root path for the project.</param>
    /// <param name="files">Dictionary of file paths to content.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <returns>A new BamlRuntimeAsync instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when rootPath or files is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when runtime creation fails.</exception>
    public static BamlRuntimeAsync FromFiles(
        string rootPath,
        Dictionary<string, string> files,
        Dictionary<string, string>? envVars = null)
    {
        var runtime = BamlRuntime.FromFiles(rootPath, files, envVars);
        return new BamlRuntimeAsync(runtime, ownsRuntime: true);
    }

    /// <summary>
    /// Disposes the async runtime wrapper and optionally the underlying runtime.
    /// </summary>
    public void Dispose()
    {
        if (_ownsRuntime)
        {
            _runtime?.Dispose();
        }
    }
}
