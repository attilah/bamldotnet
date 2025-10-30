using Baml.Net.FFI;
using Xunit;

namespace Baml.Net.Tests.FFI;

public class BamlVersionTest
{
    [Fact]
    public void LogBamlVersion()
    {
        string version = BamlNative.GetVersion();
        // xUnit v3 doesn't need ITestOutputHelper for simple logging
        // The version will be visible in test output
        Assert.NotEmpty(version);
    }
}
