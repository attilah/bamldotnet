using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Baml.Net.Core;

/// <summary>
/// Represents the execution context for BAML operations.
/// Uses AsyncLocal to provide context that flows across async/await boundaries.
/// </summary>
public sealed class BamlContext
{
    private static readonly AsyncLocal<BamlContext> _current = new AsyncLocal<BamlContext>();
    private readonly Dictionary<string, object?> _values = new Dictionary<string, object?>();
    private readonly List<Func<string, Dictionary<string, object>, Task>> _requestInterceptors = new List<Func<string, Dictionary<string, object>, Task>>();
    private readonly List<Func<string, byte[], Task>> _responseInterceptors = new List<Func<string, byte[], Task>>();
    private readonly object _lock = new object();

    /// <summary>
    /// Gets the current BAML context for the current async flow.
    /// A new context is automatically created if one doesn't exist.
    /// </summary>
    public static BamlContext Current
    {
        get
        {
            if (_current.Value == null)
            {
                _current.Value = new BamlContext();
            }
            return _current.Value;
        }
    }

    /// <summary>
    /// Private constructor to ensure contexts are only created via Current property.
    /// </summary>
    private BamlContext()
    {
    }

    /// <summary>
    /// Sets a value in the context.
    /// </summary>
    /// <param name="key">The key to store the value under.</param>
    /// <param name="value">The value to store.</param>
    public void SetValue(string key, object? value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            _values[key] = value;
        }
    }

    /// <summary>
    /// Gets a value from the context.
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve.</typeparam>
    /// <param name="key">The key to retrieve the value for.</param>
    /// <returns>The value, or default(T) if not found.</returns>
    public T? GetValue<T>(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            if (_values.TryGetValue(key, out var value))
            {
                if (value == null)
                    return default;

                return (T)value;
            }

            return default;
        }
    }

    /// <summary>
    /// Tries to get a value from the context.
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve.</typeparam>
    /// <param name="key">The key to retrieve the value for.</param>
    /// <param name="value">When this method returns, contains the value if found.</param>
    /// <returns>True if the value was found, false otherwise.</returns>
    public bool TryGetValue<T>(string key, out T? value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            if (_values.TryGetValue(key, out var objValue))
            {
                if (objValue == null)
                {
                    value = default;
                }
                else
                {
                    value = (T)objValue;
                }
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    /// Checks if a key exists in the context.
    /// </summary>
    /// <param name="key">The key to check for.</param>
    /// <returns>True if the key exists, false otherwise.</returns>
    public bool ContainsKey(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_lock)
        {
            return _values.ContainsKey(key);
        }
    }

    /// <summary>
    /// Clears all values from the context.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _values.Clear();
        }
    }

    /// <summary>
    /// Adds a request interceptor that will be called before function execution.
    /// </summary>
    /// <param name="interceptor">The interceptor function to add.</param>
    public void AddRequestInterceptor(Func<string, Dictionary<string, object>, Task> interceptor)
    {
        if (interceptor == null)
            throw new ArgumentNullException(nameof(interceptor));

        lock (_lock)
        {
            _requestInterceptors.Add(interceptor);
        }
    }

    /// <summary>
    /// Adds a response interceptor that will be called after function execution.
    /// </summary>
    /// <param name="interceptor">The interceptor function to add.</param>
    public void AddResponseInterceptor(Func<string, byte[], Task> interceptor)
    {
        if (interceptor == null)
            throw new ArgumentNullException(nameof(interceptor));

        lock (_lock)
        {
            _responseInterceptors.Add(interceptor);
        }
    }

    /// <summary>
    /// Gets all request interceptors.
    /// </summary>
    /// <returns>A list of request interceptors.</returns>
    internal List<Func<string, Dictionary<string, object>, Task>> GetRequestInterceptors()
    {
        lock (_lock)
        {
            return new List<Func<string, Dictionary<string, object>, Task>>(_requestInterceptors);
        }
    }

    /// <summary>
    /// Gets all response interceptors.
    /// </summary>
    /// <returns>A list of response interceptors.</returns>
    internal List<Func<string, byte[], Task>> GetResponseInterceptors()
    {
        lock (_lock)
        {
            return new List<Func<string, byte[], Task>>(_responseInterceptors);
        }
    }

    /// <summary>
    /// Invokes all request interceptors.
    /// </summary>
    /// <param name="functionName">The name of the function being called.</param>
    /// <param name="args">The function arguments.</param>
    internal async Task InvokeRequestInterceptors(string functionName, Dictionary<string, object> args)
    {
        var interceptors = GetRequestInterceptors();
        foreach (var interceptor in interceptors)
        {
            await interceptor(functionName, args);
        }
    }

    /// <summary>
    /// Invokes all response interceptors.
    /// </summary>
    /// <param name="functionName">The name of the function that was called.</param>
    /// <param name="result">The function result.</param>
    internal async Task InvokeResponseInterceptors(string functionName, byte[] result)
    {
        var interceptors = GetResponseInterceptors();
        foreach (var interceptor in interceptors)
        {
            await interceptor(functionName, result);
        }
    }
}
