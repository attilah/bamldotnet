using System;
using System.Threading.Tasks;
using Xunit;

namespace Baml.Net.Tests.Core;

/// <summary>
/// Simple diagnostic test to verify xUnit is working
/// </summary>
public class SimpleDiagnosticTest
{
    [Fact]
    public void Simple_Test_Passes()
    {
        Console.WriteLine("Simple test executed!");
        Assert.True(true);
    }

    [Fact]
    public async Task Simple_Async_Test_Passes()
    {
        Console.WriteLine("Simple async test executed!");
        await Task.Delay(1, TestContext.Current.CancellationToken);
        Assert.True(true);
    }
}
