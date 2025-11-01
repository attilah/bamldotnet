using Xunit;
using Baml.Net.Core;
using System.Threading.Tasks;
using System.IO;
using System;

namespace Baml.Net.CodeGenTests;

public class CodeGenerationTests
{
    [Fact]
    public async Task GeneratedClient_ShouldExist()
    {
        // This test verifies that the BamlClient class was generated
        // If this compiles, the source generator worked!

        var bamlDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "BamlFiles");
        var runtime = BamlRuntimeAsync.FromDirectory(bamlDir);

        // The BamlClient should be generated from the BAML files in the project's root namespace
        var client = new BamlClient(runtime);

        Assert.NotNull(client);
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires real metadata generation from BAML CLI - currently disabled due to CLI crash in MSBuild context")]
    public async Task ExtractResume_ShouldGenerateTypedMethod()
    {
        // This test will work once we fix the BAML CLI invocation in the MSBuild task
        // Currently the metadata is placeholder JSON, so no methods are generated

        var bamlDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "BamlFiles");
        var runtime = BamlRuntimeAsync.FromDirectory(bamlDir);
        var client = new BamlClient(runtime);

        // Once metadata is real, this should be available:
        // var result = await client.ExtractResumeAsync(sampleResume);
        // Assert.NotNull(result);
        // Assert.NotEmpty(result.Name);
        // Assert.NotEmpty(result.Email);

        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires OPENAI_API_KEY environment variable and real metadata generation")]
    public async Task ExtractResume_EndToEnd_ShouldExtractData()
    {
        // End-to-end test with actual LLM call
        // Requires:
        // 1. BAML CLI invocation fixed to generate real metadata
        // 2. OPENAI_API_KEY environment variable set

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip if no API key
            return;
        }

        var sampleResume = @"
            Vaibhav Gupta
            vbv@boundaryml.com

            Experience:
            - Founder at BoundaryML
            - CV Engineer at Google
            - CV Engineer at Microsoft

            Skills:
            - Rust
            - C++
        ";

        var bamlDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "BamlFiles");
        var runtime = BamlRuntimeAsync.FromDirectory(bamlDir);
        var client = new BamlClient(runtime);

        // Once metadata is real, this should work:
        // var result = await client.ExtractResumeAsync(sampleResume);
        //
        // Assert.NotNull(result);
        // Assert.Equal("Vaibhav Gupta", result.Name);
        // Assert.Equal("vbv@boundaryml.com", result.Email);
        // Assert.Contains("Founder at BoundaryML", result.Experience);
        // Assert.Contains("Rust", result.Skills);
        // Assert.Contains("C++", result.Skills);

        await Task.CompletedTask;
    }

    // TODO: Additional tests for generated types and methods once CLI is fixed:
    // - Generated Resume class with proper properties
    // - Generated ExtractResumeAsync method with correct signature
    // - Generated Sentiment enum (from sentiment.baml)
    // - Type safety and null handling
}
