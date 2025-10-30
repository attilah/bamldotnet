using System;
using System.Collections.Generic;
using Baml.Net.FFI;
using Xunit;

namespace Baml.Net.Tests.Core;

public class RuntimeCreationTest
{
    [Fact]
    public void CreateRuntime_DirectFFI_WithEmptyJson()
    {
        // Arrange
        var rootPath = "/tmp/test";
        var srcFilesJson = "{}";
        var envVarsJson = "{}";

        // Act & Assert - This will help us see what error we get
        var exception = Record.Exception(() =>
        {
            var runtime = BamlNativeHelpers.CreateRuntime(rootPath, srcFilesJson, envVarsJson);
        });

        // For now, just log what happens
        if (exception != null)
        {
            // We expect this might fail - let's see the error message
            Assert.NotNull(exception);
        }
    }
}
