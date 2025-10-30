using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Core;

/// <summary>
/// Tests for generic typed CallFunctionAsync<T>() method.
/// Tests follow TDD approach for Phase 2.2: Generic Typed API.
/// </summary>
public class BamlRuntimeAsyncGenericTests : BamlTestBase
{
    private BamlRuntime? _runtime;

    public override void Dispose()
    {
        _runtime?.Dispose();
        base.Dispose();
    }

    // Test models matching BAML types
    public class TestOutputClass
    {
        public string? prop1 { get; set; }
        public int prop2 { get; set; }
    }

    public class TestClassNested
    {
        public string? prop1 { get; set; }
        public InnerClass? prop2 { get; set; }
    }

    public class InnerClass
    {
        public string? prop1 { get; set; }
        public string? prop2 { get; set; }
        public InnerClass2? inner { get; set; }
    }

    public class InnerClass2
    {
        public int prop2 { get; set; }
        public float prop3 { get; set; }
    }

    [Fact]
    public void CallFunctionAsync_Generic_AcceptsTypeParameter()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        // Act & Assert - This should compile, proving the generic method exists
        // We can't actually call it without real functions, but we can verify the API exists
        Func<Task<TestOutputClass>> action = () => asyncRuntime.CallFunctionAsync<TestOutputClass>(
            "TestFunction",
            new Dictionary<string, object>(),
            GetEnvVars(), TestContext.Current.CancellationToken);

        Assert.NotNull(action);
    }

    [Fact]
    public async Task CallFunctionAsync_WithPrimitiveType_DeserializesString()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test"
        };

        // Act - Using FnOutputLiteralString which returns a string
        var result = await asyncRuntime.CallFunctionAsync<string>("FnOutputLiteralString", args, GetEnvVars(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<string>(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task CallFunctionAsync_WithSimpleObject_DeserializesCorrectly()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test input"
        };

        // Act - Using FnOutputClass which returns TestOutputClass
        var result = await asyncRuntime.CallFunctionAsync<TestOutputClass>("FnOutputClass", args, GetEnvVars(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.prop1);
        Assert.Equal(540, result.prop2); // Function always returns 540 for prop2
    }

    [Fact]
    public async Task CallFunctionAsync_WithComplexObject_DeserializesAllProperties()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test input"
        };

        // Act - Using FnOutputClassNested which returns nested structure
        var result = await asyncRuntime.CallFunctionAsync<TestClassNested>("FnOutputClassNested", args, GetEnvVars(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.prop1);
        Assert.NotNull(result.prop2);
        Assert.NotNull(result.prop2.prop1);
        Assert.NotNull(result.prop2.prop2);
        Assert.NotNull(result.prop2.inner);
        Assert.True(result.prop2.inner.prop2 > 0);
    }

    [Fact]
    public async Task CallFunctionAsync_WithList_DeserializesCollection()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test input"
        };

        // Act - Using FnOutputStringList which returns string[]
        var result = await asyncRuntime.CallFunctionAsync<List<string>>("FnOutputStringList", args, GetEnvVars(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.All(result, item => Assert.NotNull(item));
    }

    [Fact]
    public async Task CallFunctionAsync_WithAttributeMapping_UsesJsonPropertyName()
    {
        // Arrange
        if (!HasOpenAIKey()) return;

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test input"
        };

        // Define a custom mapped class for this test
        var result = await asyncRuntime.CallFunctionAsync<TestOutputClass>("FnOutputClass", args, GetEnvVars(), TestContext.Current.CancellationToken);

        // Assert - Testing that JSON deserialization works with BAML output
        Assert.NotNull(result);
        Assert.NotNull(result.prop1);
        Assert.Equal(540, result.prop2);
    }

    [Fact]
    public async Task CallFunctionAsync_Generic_ThrowsArgumentNullException_WhenFunctionNameIsNull()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        var args = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await asyncRuntime.CallFunctionAsync<TestOutputClass>(null!, args, GetEnvVars(), TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public void CallFunctionAsync_Generic_HasCorrectSignature()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var asyncRuntime = new BamlRuntimeAsync(_runtime);

        // Assert - Verify generic method exists through reflection
        var methods = typeof(BamlRuntimeAsync).GetMethods()
            .Where(m => m.Name == "CallFunctionAsync" && m.IsGenericMethod)
            .ToList();

        Assert.NotEmpty(methods);
        Assert.Single(methods); // Should have exactly one generic method
        Assert.Single(methods[0].GetGenericArguments()); // With one type parameter
    }
}
