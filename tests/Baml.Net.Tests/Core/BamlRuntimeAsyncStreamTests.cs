using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Core;

/// <summary>
/// Tests for streaming CallFunctionStream<T>(, GetEnvVars(), TestContext.Current.CancellationToken) method with IAsyncEnumerable.
/// Tests follow TDD approach for Phase 2.3: Streaming Support.
/// </summary>
public class BamlRuntimeAsyncStreamTests : BamlTestBase
{
    private BamlRuntime? _runtime;

    public override void Dispose()
    {
        _runtime?.Dispose();
        base.Dispose();
    }

    // Test model for streaming
    public class StreamChunk
    {
        public string? Content { get; set; }
        public int Index { get; set; }
    }

    [Fact]
    public void CallFunctionStream_Generic_AcceptsTypeParameter()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        // Act & Assert - This should compile, proving the streaming method exists
        Func<IAsyncEnumerable<StreamChunk>> action = () => asyncRuntime.CallFunctionStream<StreamChunk>(
            "TestFunction",
            new Dictionary<string, object>(),
            GetEnvVars(), TestContext.Current.CancellationToken);

        Assert.NotNull(action);
    }

    [Fact]
    public async Task CallFunctionStream_YieldsMultipleChunks()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "robots"
        };

        var chunks = new List<string>();

        // Act - Using PromptTestStreaming which returns streaming string
        await foreach (var chunk in asyncRuntime.CallFunctionStream<string>("PromptTestStreaming", args, GetEnvVars(), TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk);
        }

        // Assert
        Assert.NotEmpty(chunks);
        // Streaming should yield the content progressively
        Assert.All(chunks, c => Assert.NotNull(c));
    }

    [Fact]
    public async Task CallFunctionStream_CanBeCancelled()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "robots"
        };
        using var cts = new System.Threading.CancellationTokenSource();

        var chunkCount = 0;

        // Act & Assert
        // TaskCanceledException derives from OperationCanceledException
        // The implementation throws TaskCanceledException which is a more specific type
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await foreach (var chunk in asyncRuntime.CallFunctionStream<string>(
                "PromptTestStreaming",
                args,
                GetEnvVars(),
                cancellationToken: cts.Token))
            {
                chunkCount++;
                if (chunkCount >= 2)
                {
                    cts.Cancel(); // Cancel after receiving some chunks
                }
            }
        });

        Assert.True(chunkCount >= 2);
    }

    [Fact]
    public async Task CallFunctionStream_HandlesPartialJson()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "cats"
        };
        var receivedChunks = new List<string>();

        // Act - Stream should handle partial JSON chunks and yield strings progressively
        await foreach (var chunk in asyncRuntime.CallFunctionStream<string>("PromptTestStreaming", args, GetEnvVars(), TestContext.Current.CancellationToken))
        {
            receivedChunks.Add(chunk);
        }

        // Assert
        Assert.NotEmpty(receivedChunks);
        // Each chunk should be a valid string
        Assert.All(receivedChunks, content => Assert.NotNull(content));
    }

    [Fact]
    public async Task CallFunctionStream_ThrowsArgumentNullException_WhenFunctionNameIsNull()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in asyncRuntime.CallFunctionStream<StreamChunk>(
                null!,
                args,
                GetEnvVars(), TestContext.Current.CancellationToken))
            {
                // Should throw before yielding
            }
        });
    }

    [Fact]
    public async Task CallFunctionStream_WithBackpressure_HandlesSlowConsumer()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "dogs"
        };
        var processedCount = 0;

        // Act - Simulate slow consumer
        await foreach (var chunk in asyncRuntime.CallFunctionStream<string>("PromptTestStreaming", args, GetEnvVars(), TestContext.Current.CancellationToken))
        {
            await Task.Delay(100, TestContext.Current.CancellationToken); // Simulate slow processing
            processedCount++;
        }

        // Assert
        Assert.True(processedCount > 0);
    }

    [Fact]
    public void CallFunctionStream_Generic_HasCorrectSignature()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        // Assert - Verify method signature through reflection
        var methods = typeof(BamlRuntimeAsync).GetMethods()
            .Where(m => m.Name == "CallFunctionStream" && m.IsGenericMethod)
            .ToList();

        Assert.NotEmpty(methods);
        Assert.Single(methods); // Should have exactly one generic stream method
        Assert.Single(methods[0].GetGenericArguments()); // With one type parameter

        // Verify it returns IAsyncEnumerable<T>
        var returnType = methods[0].ReturnType;
        Assert.True(returnType.IsGenericType);
        Assert.Equal("IAsyncEnumerable`1", returnType.GetGenericTypeDefinition().Name);
    }

    [Fact]
    public async Task CallFunctionStream_PropagatesErrors()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test"
        };

        // Act & Assert - Should propagate errors from the stream
        // Using a non-existent model will cause an error during streaming
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in asyncRuntime.CallFunctionStream<string>("StreamAlwaysFails", args, GetEnvVars(), TestContext.Current.CancellationToken))
            {
                // Should throw during enumeration when trying to connect to non-existent model
            }
        });
    }
}
