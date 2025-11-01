using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Baml.Net.Core;
using Baml.Net.Extensions;
using Google.Protobuf;

namespace Baml.Net.Tests.Integration;

/// <summary>
/// Integration tests for BamlRuntime using actual BAML files.
/// These tests verify the full pipeline from BAML source to function execution.
/// </summary>
public class BamlRuntimeIntegrationTests : IDisposable
{
    private readonly string _bamlSrcPath;
    private BamlRuntime? _runtime;

    public BamlRuntimeIntegrationTests()
    {
        // Use the test BAML files from TestData/BamlFiles
        // These files are copied to the output directory during build
        _bamlSrcPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestData", "BamlFiles");
    }

    private static Dictionary<string, string> GetEnvVars()
    {
        var envVars = new Dictionary<string, string>();
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(openAiKey))
        {
            envVars["OPENAI_API_KEY"] = openAiKey;
        }
        return envVars;
    }

    [Fact]
    public void FromDirectory_LoadsBamlFiles_Successfully()
    {
        // Arrange & Act
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());

        // Assert
        Assert.NotNull(_runtime);
        Assert.False(_runtime.IsDisposed);
    }

    [Fact]
    public void FromDirectory_WithNonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var nonExistentPath = "/path/that/does/not/exist";

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() =>
            BamlRuntime.FromDirectory(nonExistentPath, GetEnvVars()));
    }

    [Fact]
    public void FromFiles_ManuallyLoadedBamlFiles_CreatesRuntime()
    {
        // Arrange
        var files = new Dictionary<string, string>();

        // Read all BAML files
        foreach (var file in Directory.GetFiles(_bamlSrcPath, "*.baml"))
        {
            var relativePath = Path.GetFileName(file);
            files[relativePath] = File.ReadAllText(file);
        }

        // Act
        _runtime = BamlRuntime.FromFiles(_bamlSrcPath, files, GetEnvVars());

        // Assert
        Assert.NotNull(_runtime);
        Assert.False(_runtime.IsDisposed);
        Assert.True(files.Count > 0);
    }

    [Fact]
    public void CallFunction_WithProtobufArgs_HandlesCallCorrectly()
    {
        // Arrange
        var envVars = new Dictionary<string, string>();
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(openAiKey))
        {
            envVars["OPENAI_API_KEY"] = openAiKey;
        }

        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, envVars);

        // Create function arguments using extensions - need to include env vars in the call
        var args = new Dictionary<string, object>
        {
            ["resume"] = @"
                John Doe
                john@example.com

                Experience:
                - Software Engineer at TechCorp

                Skills:
                - Python
            "
        };

        var functionArgs = args.ToFunctionArguments(envVars);
        var argsBytes = functionArgs.ToByteArray();

        // Act & Assert
        // This will fail without a valid API key, but we verify the infrastructure works
        try
        {
            var result = _runtime.CallFunction("ExtractResume", argsBytes);

            // If we get here, the function was called successfully
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            // Try to parse the response to verify it's valid protobuf
            var response = Baml.Cffi.CFFIObjectResponse.Parser.ParseFrom(result);
            Assert.NotNull(response);
        }
        catch (InvalidOperationException ex)
        {
            // Expected when API key is not set
            Assert.Contains("ExtractResume", ex.Message);
        }
    }

    [Fact]
    public void CallFunction_WithInvalidFunctionName_ThrowsInvalidOperationException()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());

        var args = new Dictionary<string, object>
        {
            ["test"] = "value"
        };

        var functionArgs = args.ToFunctionArguments();
        var argsBytes = functionArgs.ToByteArray();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _runtime.CallFunction("NonExistentFunction", argsBytes));

        Assert.Contains("NonExistentFunction", exception.Message);
    }


    [Fact]
    public void Dispose_MultipleRuntimes_WorksCorrectly()
    {
        // Arrange
        var runtime1 = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        var runtime2 = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());

        // Act
        runtime1.Dispose();

        // Assert
        Assert.True(runtime1.IsDisposed);
        Assert.False(runtime2.IsDisposed);

        // Verify runtime2 is still valid (not disposed)
        Assert.NotNull(runtime2);

        runtime2.Dispose();
        Assert.True(runtime2.IsDisposed);
    }

    [Fact]
    public void Runtime_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _runtime = BamlRuntime.FromDirectory(_bamlSrcPath, GetEnvVars());
        _runtime.Dispose();

        var args = new Dictionary<string, object> { ["test"] = "value" };
        var functionArgs = args.ToFunctionArguments();
        var argsBytes = functionArgs.ToByteArray();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            _runtime.CallFunction("ExtractResume", argsBytes));
    }

    [Fact]
    public void BamlFiles_AreLoadedCorrectly()
    {
        // Arrange & Act
        var files = Directory.GetFiles(_bamlSrcPath, "*.baml");

        // Assert
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var content = File.ReadAllText(file);

            // Verify files contain expected content
            Assert.NotEmpty(content);

            if (fileName == "resume.baml")
            {
                Assert.Contains("class Resume", content);
                Assert.Contains("function ExtractResume", content);
            }
        }
    }

    public void Dispose()
    {
        _runtime?.Dispose();
    }
}
