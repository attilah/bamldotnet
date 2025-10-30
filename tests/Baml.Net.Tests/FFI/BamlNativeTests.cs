using Baml.Net.FFI;
using Xunit;

namespace Baml.Net.Tests.FFI;

public class BamlNativeTests
{
    [Fact]
    public void GetVersion_ShouldReturnNonEmptyString()
    {
        // Act
        string version = BamlNative.GetVersion();

        // Assert
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void GetVersion_ShouldReturnValidVersionFormat()
    {
        // Act
        string version = BamlNative.GetVersion();

        // Assert
        Assert.Matches(@"\d+\.\d+\.\d+", version); // Should match semantic versioning pattern
    }

    [Fact]
    public void GetVersion_ShouldReturnConsistentValue()
    {
        // Act
        string version1 = BamlNative.GetVersion();
        string version2 = BamlNative.GetVersion();

        // Assert
        Assert.Equal(version1, version2);
    }
}
