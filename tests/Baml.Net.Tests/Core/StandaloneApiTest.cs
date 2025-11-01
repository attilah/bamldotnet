using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Baml.Net.Core;
using Xunit;
using DotNetEnv;

namespace Baml.Net.Tests.Core;

/// <summary>
/// Standalone test without BamlTestBase to diagnose issues
/// </summary>
public class StandaloneApiTest
{
    private static string? FindProjectRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, ".env")) ||
                File.Exists(Path.Combine(current, "Baml.Net.sln")) ||
                Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    [Fact]
    public async Task Standalone_MinimalBAML_CallsAPI_Successfully()
    {
        // Load .env manually from project root
        var projectRoot = FindProjectRoot();
        if (projectRoot != null)
        {
            var envPath = Path.Combine(projectRoot, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
            }
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if no API key
            return;
        }

        // Use minimal BAML directory (relative to test assembly)
        var testDir = Path.GetDirectoryName(typeof(StandaloneApiTest).Assembly.Location)!;
        var minimalPath = Path.Combine(testDir, "MinimalTestBaml");

        Console.WriteLine("=== Standalone API Test ===");
        Console.WriteLine($"BAML Path: {minimalPath}");
        Console.WriteLine($"API Key Set: {!string.IsNullOrEmpty(apiKey)}");

        // Create environment variables
        var envVars = new Dictionary<string, string>
        {
            ["OPENAI_API_KEY"] = apiKey
        };

        // Create runtime
        Console.WriteLine("Creating BamlRuntime...");
        using var runtime = BamlRuntime.FromDirectory(minimalPath, envVars);
        Console.WriteLine("✓ Runtime created!");

        var asyncRuntime = new BamlRuntimeAsync(runtime);

        var args = new Dictionary<string, object>
        {
            ["input"] = "test input"
        };

        Console.WriteLine("Calling SimpleTest function...");
        Console.WriteLine($"Input: test input");
        Console.WriteLine($"BAML File: {minimalPath}/test.baml");
        Console.WriteLine("Function: SimpleTest");

        var result = await asyncRuntime.CallFunctionAsync(
            "SimpleTest",
            args,
            envVars,
            TestContext.Current.CancellationToken);

        Console.WriteLine($"✓ Result received: {result?.Length ?? 0} bytes");
        if (result != null && result.Length > 0)
        {
            Console.WriteLine($"Result preview: {System.Text.Encoding.UTF8.GetString(result.Take(100).ToArray())}...");
        }

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }
}
