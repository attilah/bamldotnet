using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Core;

/// <summary>
/// Minimal test to diagnose runtime loading issue
/// </summary>
public class BamlRuntimeMinimalTest : BamlTestBase
{
    [Fact]
    public async Task MinimalTest_LoadsRuntime_Successfully()
    {
        // Skip if no OpenAI key
        if (!HasOpenAIKey())
        {
            return;
        }

        var minimalPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "MinimalTestBaml");

        Console.WriteLine($"=== Minimal BAML Test ===");
        Console.WriteLine($"Path: {minimalPath}");
        Console.WriteLine($"Exists: {Directory.Exists(minimalPath)}");

        if (!Directory.Exists(minimalPath))
        {
            throw new DirectoryNotFoundException($"MinimalTestBaml directory not found: {minimalPath}");
        }

        Console.WriteLine("Creating runtime...");
        using var runtime = BamlRuntime.FromDirectory(minimalPath, GetEnvVars());
        Console.WriteLine("✓ Runtime created!");

        var asyncRuntime = new BamlRuntimeAsync(runtime);
        Console.WriteLine("✓ Async runtime created!");

        var args = new Dictionary<string, object>
        {
            ["input"] = "hello world"
        };

        Console.WriteLine("Calling function SimpleTest...");
        var result = await asyncRuntime.CallFunctionAsync(
            "SimpleTest",
            args,
            GetEnvVars(),
            TestContext.Current.CancellationToken);

        Console.WriteLine($"✓ Got result: {result?.Length ?? 0} bytes");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }
}
