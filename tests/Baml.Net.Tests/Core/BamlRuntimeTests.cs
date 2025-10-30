using System;
using System.Collections.Generic;
using System.IO;
using Baml.Net.Core;
using Xunit;

namespace Baml.Net.Tests.Core;

public class BamlRuntimeTests
{
    [Fact]
    public void FromFiles_CreatesRuntime_WithEmptyFiles()
    {
        // Arrange
        var files = new Dictionary<string, string>();
        var envVars = new Dictionary<string, string>();

        // Act & Assert
        using var runtime = BamlRuntime.FromFiles("/tmp/test", files, envVars);
        Assert.NotNull(runtime);
        Assert.False(runtime.IsDisposed);
    }

    [Fact]
    public void Dispose_DisposesRuntime()
    {
        // Arrange
        var files = new Dictionary<string, string>();
        var runtime = BamlRuntime.FromFiles("/tmp/test", files, null);

        // Act
        runtime.Dispose();

        // Assert
        Assert.True(runtime.IsDisposed);
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var files = new Dictionary<string, string>();
        var runtime = BamlRuntime.FromFiles("/tmp/test", files, null);

        // Act & Assert
        runtime.Dispose();
        runtime.Dispose(); // Should not throw
    }

    [Fact]
    public void FromFiles_ThrowsArgumentNullException_WhenRootPathIsNull()
    {
        // Arrange
        var files = new Dictionary<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => BamlRuntime.FromFiles(null!, files, null));
    }

    [Fact]
    public void FromFiles_ThrowsArgumentNullException_WhenFilesIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => BamlRuntime.FromFiles("/tmp/test", null!, null));
    }

    [Fact]
    public void CallFunction_ThrowsObjectDisposedException_WhenDisposed()
    {
        // Arrange
        var files = new Dictionary<string, string>();
        var runtime = BamlRuntime.FromFiles("/tmp/test", files, null);
        runtime.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => runtime.CallFunction("test", Array.Empty<byte>()));
    }

    [Fact]
    public void CallFunction_ThrowsArgumentNullException_WhenFunctionNameIsNull()
    {
        // Arrange
        var files = new Dictionary<string, string>();
        using var runtime = BamlRuntime.FromFiles("/tmp/test", files, null);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => runtime.CallFunction(null!, Array.Empty<byte>()));
    }
}
