using System;
using System.Collections.Generic;
using System.IO;
using DotNetEnv;

namespace Baml.Net.Tests;

/// <summary>
/// Base class for BAML tests that provides environment configuration and common utilities.
/// </summary>
public abstract class BamlTestBase : IDisposable
{
    private static readonly object _envLoadLock = new object();
    private static bool _envLoaded = false;

    protected readonly string _bamlSrcPath;

    protected BamlTestBase()
    {
        Console.WriteLine("[BamlTestBase] Constructor started");

        // Load environment variables once per test run
        lock (_envLoadLock)
        {
            if (!_envLoaded)
            {
                Console.WriteLine("[BamlTestBase] Loading environment variables...");
                LoadEnvironmentVariables();
                _envLoaded = true;
                Console.WriteLine("[BamlTestBase] Environment variables loaded");
            }
        }

        // Set path to test BAML files
        // These files are copied to the output directory during build
        _bamlSrcPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestBamlSrc");

        Console.WriteLine($"[BamlTestBase] BAML source path: {_bamlSrcPath}");

        // Verify BAML source directory exists
        if (!Directory.Exists(_bamlSrcPath))
        {
            throw new InvalidOperationException(
                $"BAML source directory not found: {_bamlSrcPath}\n\n" +
                "The TestBamlSrc directory should be automatically copied during build.");
        }

        Console.WriteLine("[BamlTestBase] Constructor completed");
    }

    /// <summary>
    /// Load environment variables from .env file.
    /// Supports multiple .env file locations for flexibility.
    /// </summary>
    private static void LoadEnvironmentVariables()
    {
        var projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            Console.WriteLine("Warning: Could not find project root. Skipping .env file load.");
            return;
        }

        var envPath = Path.Combine(projectRoot, ".env");

        if (File.Exists(envPath))
        {
            try
            {
                Env.Load(envPath);
                Console.WriteLine($"Loaded environment variables from: {envPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to load .env file: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"Warning: .env file not found at: {envPath}");
            Console.WriteLine("Create .env file from .env.example and add your API keys.");
            Console.WriteLine("Tests requiring API keys will be skipped.");
        }
    }

    /// <summary>
    /// Find the project root directory by looking for .env.example or .git directory.
    /// </summary>
    private static string? FindProjectRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current != null)
        {
            // Check for .env.example or .git as markers for project root
            if (File.Exists(Path.Combine(current, ".env.example")) ||
                Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Check if an environment variable is set and not empty.
    /// </summary>
    protected static bool HasEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Get an environment variable, or throw if not set.
    /// </summary>
    protected static string RequireEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not set.\n" +
                $"Please add it to your .env file (see .env.example for template).");
        }
        return value;
    }

    /// <summary>
    /// Check if OpenAI API key is configured.
    /// </summary>
    protected static bool HasOpenAIKey()
    {
        return HasEnvironmentVariable("OPENAI_API_KEY");
    }

    /// <summary>
    /// Check if Anthropic API key is configured.
    /// </summary>
    protected static bool HasAnthropicKey()
    {
        return HasEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    /// <summary>
    /// Get environment variables for BAML function calls.
    /// Returns a dictionary with OPENAI_API_KEY and ANTHROPIC_API_KEY if they are set.
    /// </summary>
    protected static Dictionary<string, string> GetEnvVars()
    {
        var envVars = new Dictionary<string, string>();

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(openAiKey))
        {
            envVars["OPENAI_API_KEY"] = openAiKey;
        }

        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(anthropicKey))
        {
            envVars["ANTHROPIC_API_KEY"] = anthropicKey;
        }

        return envVars;
    }

    public virtual void Dispose()
    {
        // Override in derived classes if cleanup is needed
    }
}
