using System;
using System.Collections.Generic;
using Baml.Cffi;
using Google.Protobuf;

namespace Baml.Net.Extensions;

/// <summary>
/// Extension methods for working with BAML protobuf values.
/// </summary>
public static class BamlValueExtensions
{
    /// <summary>
    /// Converts a C# object to a CFFI ValueHolder.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>A CFFIValueHolder containing the converted value.</returns>
    /// <exception cref="NotSupportedException">Thrown when the type cannot be converted.</exception>
    public static CFFIValueHolder ToCFFI(this object? value)
    {
        return value switch
        {
            null => new CFFIValueHolder { NullValue = new CFFIValueNull() },
            string s => new CFFIValueHolder { StringValue = s },
            int i => new CFFIValueHolder { IntValue = i },
            long l => new CFFIValueHolder { IntValue = l },
            float f => new CFFIValueHolder { FloatValue = f },
            double d => new CFFIValueHolder { FloatValue = d },
            bool b => new CFFIValueHolder { BoolValue = b },
            Dictionary<string, object> dict => CreateMapValue(dict),
            IEnumerable<object> list => CreateListValue(list),
            _ => throw new NotSupportedException($"Type {value.GetType()} not supported for CFFI conversion")
        };
    }

    /// <summary>
    /// Converts a CFFI ValueHolder to a C# object.
    /// </summary>
    /// <param name="holder">The value holder to convert.</param>
    /// <returns>A C# object representation of the value.</returns>
    /// <exception cref="NotSupportedException">Thrown when the value type cannot be converted.</exception>
    public static object? ToObject(this CFFIValueHolder holder)
    {
        return holder.ValueCase switch
        {
            CFFIValueHolder.ValueOneofCase.NullValue => null,
            CFFIValueHolder.ValueOneofCase.StringValue => holder.StringValue,
            CFFIValueHolder.ValueOneofCase.IntValue => holder.IntValue,
            CFFIValueHolder.ValueOneofCase.FloatValue => holder.FloatValue,
            CFFIValueHolder.ValueOneofCase.BoolValue => holder.BoolValue,
            CFFIValueHolder.ValueOneofCase.MapValue => ConvertMapValue(holder.MapValue),
            CFFIValueHolder.ValueOneofCase.ListValue => ConvertListValue(holder.ListValue),
            CFFIValueHolder.ValueOneofCase.ClassValue => ConvertClassValue(holder.ClassValue),
            CFFIValueHolder.ValueOneofCase.EnumValue => ConvertEnumValue(holder.EnumValue),
            CFFIValueHolder.ValueOneofCase.StreamingStateValue => ConvertStreamingStateValue(holder.StreamingStateValue),
            _ => throw new NotSupportedException($"Value case {holder.ValueCase} not supported")
        };
    }

    /// <summary>
    /// Creates a CFFIFunctionArguments from a dictionary of arguments.
    /// </summary>
    /// <param name="args">Dictionary of argument name to value.</param>
    /// <param name="envVars">Optional environment variables.</param>
    /// <returns>A CFFIFunctionArguments ready to be serialized.</returns>
    public static CFFIFunctionArguments ToFunctionArguments(
        this Dictionary<string, object> args,
        Dictionary<string, string>? envVars = null)
    {
        var functionArgs = new CFFIFunctionArguments();

        // Add keyword arguments as CFFIMapEntry items
        foreach (var (key, value) in args)
        {
            functionArgs.Kwargs.Add(new CFFIMapEntry
            {
                Key = key,
                Value = value.ToCFFI()
            });
        }

        // Add environment variables as CFFIEnvVar items
        if (envVars != null)
        {
            foreach (var (key, value) in envVars)
            {
                functionArgs.Env.Add(new CFFIEnvVar
                {
                    Key = key,
                    Value = value
                });
            }
        }

        return functionArgs;
    }

    private static CFFIValueHolder CreateMapValue(Dictionary<string, object> dict)
    {
        var mapValue = new CFFIValueMap();

        foreach (var (key, value) in dict)
        {
            var entry = new CFFIMapEntry
            {
                Key = key,
                Value = value.ToCFFI()
            };
            mapValue.Entries.Add(entry);
        }

        return new CFFIValueHolder { MapValue = mapValue };
    }

    private static CFFIValueHolder CreateListValue(IEnumerable<object> list)
    {
        var listValue = new CFFIValueList();

        foreach (var item in list)
        {
            listValue.Values.Add(item.ToCFFI());
        }

        return new CFFIValueHolder { ListValue = listValue };
    }

    private static Dictionary<string, object?> ConvertMapValue(CFFIValueMap mapValue)
    {
        var result = new Dictionary<string, object?>();

        foreach (var entry in mapValue.Entries)
        {
            result[entry.Key] = entry.Value.ToObject();
        }

        return result;
    }

    private static List<object?> ConvertListValue(CFFIValueList listValue)
    {
        var result = new List<object?>();

        foreach (var value in listValue.Values)
        {
            result.Add(value.ToObject());
        }

        return result;
    }

    private static Dictionary<string, object?> ConvertClassValue(CFFIValueClass classValue)
    {
        var result = new Dictionary<string, object?>
        {
            ["__type"] = classValue.Name.Name
        };

        foreach (var field in classValue.Fields)
        {
            result[field.Key] = field.Value.ToObject();
        }

        return result;
    }

    private static object ConvertEnumValue(CFFIValueEnum enumValue)
    {
        return enumValue.Value;
    }

    /// <summary>
    /// Converts a streaming state value by extracting the wrapped value.
    /// The streaming state contains the actual value and state information (PENDING/STARTED/DONE).
    /// </summary>
    /// <param name="streamingState">The streaming state value to convert.</param>
    /// <returns>The unwrapped value as a C# object.</returns>
    private static object? ConvertStreamingStateValue(CFFIValueStreamingState streamingState)
    {
        // Extract the wrapped value and recursively convert it
        // The state (PENDING/STARTED/DONE) is metadata we don't need for the value itself
        return streamingState.Value?.ToObject();
    }

    /// <summary>
    /// Converts a dictionary to a list of CFFIMapEntry items.
    /// </summary>
    /// <param name="dict">Dictionary to convert.</param>
    /// <returns>List of CFFIMapEntry items.</returns>
    public static List<CFFIMapEntry> ToCFFIMapEntries(this Dictionary<string, object> dict)
    {
        var entries = new List<CFFIMapEntry>();

        foreach (var (key, value) in dict)
        {
            entries.Add(new CFFIMapEntry
            {
                Key = key,
                Value = value.ToCFFI()
            });
        }

        return entries;
    }
}
