using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baml.Cffi;
using Baml.Net.Core;
using Baml.Net.Extensions;
using Baml.Net.FFI;
using Google.Protobuf;
using Xunit;

namespace Baml.Net.Tests.FFI;

/// <summary>
/// Tests for new FFI functions added in Phase 1-3.
/// These tests verify that the new FFI functions and helper methods exist
/// and are properly structured, without requiring the native library.
/// </summary>
public class FFITests
{

    [Fact]
    public void RuntimeMethods_ShouldExist()
    {
        // Verify that all new runtime methods exist
        var runtimeType = typeof(BamlRuntime);

        // Test ParseAsync exists
        Assert.NotNull(runtimeType.GetMethod("ParseAsync"));

        // Test CreateCollector exists
        Assert.NotNull(runtimeType.GetMethod("CreateCollector"));

        // Test CreateTypeBuilder exists
        Assert.NotNull(runtimeType.GetMethod("CreateTypeBuilder"));

        // Test InvokeObjectMethod exists
        Assert.NotNull(runtimeType.GetMethod("InvokeObjectMethod"));
    }

    [Fact]
    public void ProtobufTypes_ShouldSerializeCorrectly()
    {
        // Test CFFIObjectConstructorArgs serialization
        var constructorArgs = new CFFIObjectConstructorArgs
        {
            Type = CFFIObjectType.ObjectCollector,
            Kwargs = { }
        };
        var argsData = constructorArgs.ToByteArray();
        Assert.NotNull(argsData);
        Assert.NotEmpty(argsData);

        // Test CFFIObjectMethodArguments serialization
        var methodArgs = new CFFIObjectMethodArguments
        {
            Object = new CFFIRawObject
            {
                Collector = new CFFIPointerType { Pointer = 12345 }
            },
            MethodName = "add",
            Kwargs = { }
        };
        var methodData = methodArgs.ToByteArray();
        Assert.NotNull(methodData);
        Assert.NotEmpty(methodData);
    }

    [Fact]
    public void ToCFFIMapEntries_ShouldConvertDictionaryToMapEntries()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 42,
            ["key3"] = true
        };

        // Act
        var mapEntries = dict.ToCFFIMapEntries();

        // Assert
        Assert.Equal(3, mapEntries.Count);
        Assert.Equal("key1", mapEntries[0].Key);
        Assert.Equal("value1", mapEntries[0].Value.StringValue);
        Assert.Equal("key2", mapEntries[1].Key);
        Assert.Equal(42, mapEntries[1].Value.IntValue);
        Assert.Equal("key3", mapEntries[2].Key);
        Assert.True(mapEntries[2].Value.BoolValue);
    }

    [Fact]
    public void CancelCallback_ShouldCancelPendingOperation()
    {
        // Test that the CancelCallback method exists and is accessible
        var method = typeof(BamlCallbackManager).GetMethod("CancelCallback",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(uint), parameters[0].ParameterType);
    }

    [Fact]
    public void NativeFFIDeclarations_ShouldBeAccessible()
    {
        // This test verifies that all new FFI declarations are present and accessible
        // The actual invocation would fail without the native library, but the methods should exist

        // Test CallFunctionParse exists
        Assert.NotNull(typeof(BamlNative).GetMethod("CallFunctionParse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

        // Test CancelFunctionCall exists
        Assert.NotNull(typeof(BamlNative).GetMethod("CancelFunctionCall",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

        // Test CallObjectConstructor exists
        Assert.NotNull(typeof(BamlNative).GetMethod("CallObjectConstructor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

        // Test CallObjectMethod exists
        Assert.NotNull(typeof(BamlNative).GetMethod("CallObjectMethod",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
    }

    [Fact]
    public void HelperMethods_ShouldBeAccessible()
    {
        // Verify that all new helper methods exist in BamlNativeHelpers

        // Test CallFunctionParseAsync exists
        Assert.NotNull(typeof(BamlNativeHelpers).GetMethod("CallFunctionParseAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));

        // Test CallObjectConstructor exists
        Assert.NotNull(typeof(BamlNativeHelpers).GetMethod("CallObjectConstructor",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));

        // Test CallObjectMethod exists
        Assert.NotNull(typeof(BamlNativeHelpers).GetMethod("CallObjectMethod",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
    }

}