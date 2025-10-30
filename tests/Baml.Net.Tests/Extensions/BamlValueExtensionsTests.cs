using System;
using System.Collections.Generic;
using Xunit;
using Baml.Cffi;
using Baml.Net.Extensions;

namespace Baml.Net.Tests.Extensions;

public class BamlValueExtensionsTests
{
    [Fact]
    public void ToCFFI_Null_CreatesNullValue()
    {
        // Arrange
        object? value = null;

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.NullValue, result.ValueCase);
    }

    [Fact]
    public void ToCFFI_String_CreatesStringValue()
    {
        // Arrange
        var value = "Hello, World!";

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.StringValue, result.ValueCase);
        Assert.Equal(value, result.StringValue);
    }

    [Fact]
    public void ToCFFI_Int_CreatesIntValue()
    {
        // Arrange
        var value = 42;

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.IntValue, result.ValueCase);
        Assert.Equal(value, result.IntValue);
    }

    [Fact]
    public void ToCFFI_Long_CreatesIntValue()
    {
        // Arrange
        long value = 9876543210L;

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.IntValue, result.ValueCase);
        Assert.Equal(value, result.IntValue);
    }

    [Fact]
    public void ToCFFI_Float_CreatesFloatValue()
    {
        // Arrange
        float value = 3.14f;

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.FloatValue, result.ValueCase);
        Assert.Equal(value, result.FloatValue, 0.001);
    }

    [Fact]
    public void ToCFFI_Double_CreatesFloatValue()
    {
        // Arrange
        var value = 3.14159;

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.FloatValue, result.ValueCase);
        Assert.Equal(value, result.FloatValue);
    }

    [Fact]
    public void ToCFFI_Bool_CreatesBoolValue()
    {
        // Arrange
        var value = true;

        // Act
        var result = value.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.BoolValue, result.ValueCase);
        Assert.Equal(value, result.BoolValue);
    }

    [Fact]
    public void ToCFFI_Dictionary_CreatesMapValue()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30,
            ["active"] = true
        };

        // Act
        var result = dict.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.MapValue, result.ValueCase);
        Assert.Equal(3, result.MapValue.Entries.Count);

        var nameEntry = result.MapValue.Entries[0];
        Assert.Equal("name", nameEntry.Key);
        Assert.Equal("Alice", nameEntry.Value.StringValue);
    }

    [Fact]
    public void ToCFFI_List_CreatesListValue()
    {
        // Arrange
        var list = new List<object> { "one", 2, true };

        // Act
        var result = list.ToCFFI();

        // Assert
        Assert.Equal(CFFIValueHolder.ValueOneofCase.ListValue, result.ValueCase);
        Assert.Equal(3, result.ListValue.Values.Count);
        Assert.Equal("one", result.ListValue.Values[0].StringValue);
        Assert.Equal(2, result.ListValue.Values[1].IntValue);
        Assert.True(result.ListValue.Values[2].BoolValue);
    }

    [Fact]
    public void ToCFFI_UnsupportedType_ThrowsNotSupportedException()
    {
        // Arrange
        var value = new System.IO.FileInfo("test.txt");

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => value.ToCFFI());
    }

    [Fact]
    public void ToObject_NullValue_ReturnsNull()
    {
        // Arrange
        var holder = new CFFIValueHolder { NullValue = new CFFIValueNull() };

        // Act
        var result = holder.ToObject();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ToObject_StringValue_ReturnsString()
    {
        // Arrange
        var holder = new CFFIValueHolder { StringValue = "Test" };

        // Act
        var result = holder.ToObject();

        // Assert
        Assert.Equal("Test", result);
    }

    [Fact]
    public void ToObject_IntValue_ReturnsLong()
    {
        // Arrange
        var holder = new CFFIValueHolder { IntValue = 42 };

        // Act
        var result = holder.ToObject();

        // Assert
        Assert.Equal(42L, result);
    }

    [Fact]
    public void ToObject_FloatValue_ReturnsDouble()
    {
        // Arrange
        var holder = new CFFIValueHolder { FloatValue = 3.14 };

        // Act
        var result = holder.ToObject();

        // Assert
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ToObject_BoolValue_ReturnsBool()
    {
        // Arrange
        var holder = new CFFIValueHolder { BoolValue = true };

        // Act
        var result = holder.ToObject();

        // Assert
        Assert.NotNull(result);
        Assert.True((bool)result!);
    }

    [Fact]
    public void ToObject_MapValue_ReturnsDictionary()
    {
        // Arrange
        var mapValue = new CFFIValueMap();
        mapValue.Entries.Add(new CFFIMapEntry
        {
            Key = "name",
            Value = new CFFIValueHolder { StringValue = "Bob" }
        });
        mapValue.Entries.Add(new CFFIMapEntry
        {
            Key = "age",
            Value = new CFFIValueHolder { IntValue = 25 }
        });

        var holder = new CFFIValueHolder { MapValue = mapValue };

        // Act
        var result = holder.ToObject() as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("Bob", result["name"]);
        Assert.Equal(25L, result["age"]);
    }

    [Fact]
    public void ToObject_ListValue_ReturnsList()
    {
        // Arrange
        var listValue = new CFFIValueList();
        listValue.Values.Add(new CFFIValueHolder { StringValue = "first" });
        listValue.Values.Add(new CFFIValueHolder { IntValue = 2 });
        listValue.Values.Add(new CFFIValueHolder { BoolValue = true });

        var holder = new CFFIValueHolder { ListValue = listValue };

        // Act
        var result = holder.ToObject() as List<object?>;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal("first", result[0]);
        Assert.Equal(2L, result[1]);
        Assert.True((bool)result[2]!);
    }

    [Fact]
    public void Roundtrip_String_PreservesValue()
    {
        // Arrange
        var original = "Test String";

        // Act
        var cffi = original.ToCFFI();
        var result = cffi.ToObject();

        // Assert
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_ComplexObject_PreservesStructure()
    {
        // Arrange
        var original = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30,
            ["scores"] = new List<object> { 95, 87, 92 },
            ["metadata"] = new Dictionary<string, object>
            {
                ["active"] = true,
                ["level"] = "advanced"
            }
        };

        // Act
        var cffi = original.ToCFFI();
        var result = cffi.ToObject() as Dictionary<string, object?>;

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Alice", result!["name"]);
        Assert.Equal(30L, result["age"]);

        var scores = result["scores"] as List<object?>;
        Assert.NotNull(scores);
        Assert.Equal(3, scores!.Count);
        Assert.Equal(95L, scores[0]);

        var metadata = result["metadata"] as Dictionary<string, object?>;
        Assert.NotNull(metadata);
        Assert.True((bool)metadata!["active"]!);
        Assert.Equal("advanced", metadata["level"]);
    }

    [Fact]
    public void ToFunctionArguments_CreatesValidArguments()
    {
        // Arrange
        var args = new Dictionary<string, object>
        {
            ["prompt"] = "Hello",
            ["max_tokens"] = 100
        };
        var envVars = new Dictionary<string, string>
        {
            ["API_KEY"] = "test-key"
        };

        // Act
        var result = args.ToFunctionArguments(envVars);

        // Assert
        Assert.Equal(2, result.Kwargs.Count);

        var promptEntry = result.Kwargs[0];
        Assert.Equal("prompt", promptEntry.Key);
        Assert.Equal("Hello", promptEntry.Value.StringValue);

        var maxTokensEntry = result.Kwargs[1];
        Assert.Equal("max_tokens", maxTokensEntry.Key);
        Assert.Equal(100, maxTokensEntry.Value.IntValue);

        Assert.Single(result.Env);
        Assert.Equal("API_KEY", result.Env[0].Key);
        Assert.Equal("test-key", result.Env[0].Value);
    }

    [Fact]
    public void ToFunctionArguments_WithoutEnvVars_CreatesEmptyEnvVars()
    {
        // Arrange
        var args = new Dictionary<string, object>
        {
            ["test"] = "value"
        };

        // Act
        var result = args.ToFunctionArguments();

        // Assert
        Assert.Single(result.Kwargs);
        Assert.Equal("test", result.Kwargs[0].Key);
        Assert.Equal("value", result.Kwargs[0].Value.StringValue);
        Assert.Empty(result.Env);
    }
}
