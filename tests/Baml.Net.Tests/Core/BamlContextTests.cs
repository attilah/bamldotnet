using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Core;

/// <summary>
/// Tests for BamlContext and context propagation with AsyncLocal.
/// Tests follow TDD approach for Phase 2.4: Context Management.
/// </summary>
public class BamlContextTests : BamlTestBase
{
    private BamlRuntime? _runtime;

    public override void Dispose()
    {
        _runtime?.Dispose();
        base.Dispose();
    }

    [Fact]
    public void BamlContext_Current_ReturnsContextInstance()
    {
        // Act
        var context = BamlContext.Current;

        // Assert
        Assert.NotNull(context);
    }

    [Fact]
    public void BamlContext_SetValue_StoresValue()
    {
        // Arrange
        var context = BamlContext.Current;

        // Act
        context.SetValue("testKey", "testValue");
        var value = context.GetValue<string>("testKey");

        // Assert
        Assert.Equal("testValue", value);
    }

    [Fact]
    public void BamlContext_GetValue_ReturnsDefaultIfNotSet()
    {
        // Arrange
        var context = BamlContext.Current;

        // Act
        var value = context.GetValue<string>("nonExistentKey");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public async Task BamlContext_PropagatesAcrossAsyncCalls()
    {
        // Arrange
        var context = BamlContext.Current;
        context.SetValue("asyncKey", "asyncValue");

        // Act
        var retrievedValue = await Task.Run(() =>
        {
            // Context should flow to this async operation
            return BamlContext.Current.GetValue<string>("asyncKey");
        });

        // Assert
        Assert.Equal("asyncValue", retrievedValue);
    }

    [Fact]
    public async Task BamlContext_IsolatedBetweenParallelTasks()
    {
        // Arrange & Act
        var task1 = Task.Run(async () =>
        {
            BamlContext.Current.SetValue("taskId", "task1");
            await Task.Delay(50, TestContext.Current.CancellationToken); // Simulate work
            return BamlContext.Current.GetValue<string>("taskId");
        }, TestContext.Current.CancellationToken);

        var task2 = Task.Run(async () =>
        {
            BamlContext.Current.SetValue("taskId", "task2");
            await Task.Delay(50, TestContext.Current.CancellationToken); // Simulate work
            return BamlContext.Current.GetValue<string>("taskId");
        }, TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(task1, task2);

        // Assert - Each task should see its own value
        Assert.Contains("task1", results);
        Assert.Contains("task2", results);
    }

    [Fact]
    public void BamlContext_Clear_RemovesAllValues()
    {
        // Arrange
        var context = BamlContext.Current;
        context.SetValue("key1", "value1");
        context.SetValue("key2", "value2");

        // Act
        context.Clear();

        // Assert
        Assert.Null(context.GetValue<string>("key1"));
        Assert.Null(context.GetValue<string>("key2"));
    }

    [Fact]
    public void BamlContext_ContainsKey_ReturnsCorrectValue()
    {
        // Arrange
        var context = BamlContext.Current;
        context.SetValue("existingKey", "value");

        // Act & Assert
        Assert.True(context.ContainsKey("existingKey"));
        Assert.False(context.ContainsKey("nonExistentKey"));
    }

    [Fact]
    public async Task BamlContext_AddRequestInterceptor_StoresInterceptor()
    {
        // Arrange
        var context = BamlContext.Current;
        var interceptorCalled = false;

        // Act
        context.AddRequestInterceptor((functionName, args) =>
        {
            interceptorCalled = true;
            return Task.CompletedTask;
        });

        // Trigger interceptor
        var interceptors = context.GetRequestInterceptors();
        await interceptors[0]("TestFunc", new Dictionary<string, object>());

        // Assert
        Assert.True(interceptorCalled);
    }

    [Fact]
    public async Task BamlContext_AddResponseInterceptor_StoresInterceptor()
    {
        // Arrange
        var context = BamlContext.Current;
        var interceptorCalled = false;

        // Act
        context.AddResponseInterceptor((functionName, result) =>
        {
            interceptorCalled = true;
            return Task.CompletedTask;
        });

        // Trigger interceptor
        var interceptors = context.GetResponseInterceptors();
        await interceptors[0]("TestFunc", new byte[0]);

        // Assert
        Assert.True(interceptorCalled);
    }

    [Fact]
    public async Task CallFunctionAsync_InvokesRequestInterceptor()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var interceptorCalled = false;
        var capturedFunctionName = "";

        BamlContext.Current.AddRequestInterceptor((functionName, args) =>
        {
            interceptorCalled = true;
            capturedFunctionName = functionName;
            return Task.CompletedTask;
        });

        // Act - Using FnOutputBool which is a real BAML function
        try
        {
            await asyncRuntime.CallFunctionAsync(
                "FnOutputBool",
                new Dictionary<string, object> { ["input"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken);
        }
        catch
        {
            // Ignore function execution errors - we're just testing the interceptor
        }

        // Assert
        Assert.True(interceptorCalled);
        Assert.Equal("FnOutputBool", capturedFunctionName);
    }

    [Fact]
    public async Task CallFunctionAsync_InvokesResponseInterceptor()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var interceptorCalled = false;
        byte[]? capturedResult = null;

        BamlContext.Current.AddResponseInterceptor((functionName, result) =>
        {
            interceptorCalled = true;
            capturedResult = result;
            return Task.CompletedTask;
        });

        // Act - Using FnOutputBool which is a real BAML function
        try
        {
            await asyncRuntime.CallFunctionAsync(
                "FnOutputBool",
                new Dictionary<string, object> { ["input"] = "test" },
                GetEnvVars(),
                TestContext.Current.CancellationToken);
        }
        catch
        {
            // Ignore function execution errors - we're just testing the interceptor
        }

        // Assert
        Assert.True(interceptorCalled);
        Assert.NotNull(capturedResult);
    }

    [Fact]
    public async Task BamlContext_NestedCalls_MaintainContext()
    {
        // Arrange
        BamlContext.Current.SetValue("outerValue", "outer");

        // Act
        var result = await Task.Run(async () =>
        {
            // Outer context should be accessible
            var outer = BamlContext.Current.GetValue<string>("outerValue");

            // Set inner value
            BamlContext.Current.SetValue("innerValue", "inner");

            await Task.Delay(10, TestContext.Current.CancellationToken);

            // Both should be accessible
            return new
            {
                Outer = BamlContext.Current.GetValue<string>("outerValue"),
                Inner = BamlContext.Current.GetValue<string>("innerValue")
            };
        });

        // Assert
        Assert.Equal("outer", result.Outer);
        Assert.Equal("inner", result.Inner);
    }

    [Fact]
    public void BamlContext_TryGetValue_ReturnsCorrectResult()
    {
        // Arrange
        var context = BamlContext.Current;
        context.SetValue("existingKey", "value");

        // Act
        var exists = context.TryGetValue<string>("existingKey", out var value);
        var notExists = context.TryGetValue<string>("nonExistentKey", out var missingValue);

        // Assert
        Assert.True(exists);
        Assert.Equal("value", value);
        Assert.False(notExists);
        Assert.Null(missingValue);
    }
}
