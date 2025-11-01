using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baml.Net.Core;

namespace Baml.Net.CliDebugger
{
    /// <summary>
    /// Console application that mimics the MSBuild task's behavior for debugging the CLI crash.
    /// This replicates the exact same flow as GenerateBamlMetadata.cs but in a standalone console app.
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=== BAML CLI Debugger ===");
            Console.WriteLine("This tool mimics the MSBuild task behavior to debug CLI crashes.");
            Console.WriteLine();

            // Parse command line arguments
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
            {
                ShowHelp();
                return 0;
            }

            string? projectDir = null;
            string? outputPath = null;
            List<string> bamlFiles = new List<string>();
            List<string> cliArgs = new List<string>();

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--project-dir":
                    case "-p":
                        if (i + 1 < args.Length)
                        {
                            projectDir = args[++i];
                        }
                        break;

                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            outputPath = args[++i];
                        }
                        break;

                    case "--baml-file":
                    case "-f":
                        if (i + 1 < args.Length)
                        {
                            bamlFiles.Add(args[++i]);
                        }
                        break;

                    case "--cli-args":
                        // Everything after --cli-args is passed to InvokeCli
                        cliArgs.AddRange(args.Skip(i + 1));
                        i = args.Length; // Exit loop
                        break;

                    default:
                        if (!args[i].StartsWith("-"))
                        {
                            // Treat as BAML file if not a flag
                            bamlFiles.Add(args[i]);
                        }
                        break;
                }
            }

            // Use defaults if not specified
            if (string.IsNullOrEmpty(projectDir))
            {
                // Default to test project directory
                projectDir = Path.Combine(Directory.GetCurrentDirectory(), "tests", "Baml.Net.Tests");
                if (!Directory.Exists(projectDir))
                {
                    projectDir = Directory.GetCurrentDirectory();
                }
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "output");
            }

            // If no BAML files specified, try to find some from test project
            if (bamlFiles.Count == 0)
            {
                var testBamlDir = Path.Combine(projectDir, "TestBamlSrc");
                if (Directory.Exists(testBamlDir))
                {
                    // Add a few test files
                    var clientsFile = Path.Combine(testBamlDir, "clients.baml");
                    var generatorsFile = Path.Combine(testBamlDir, "generators.baml");

                    if (File.Exists(clientsFile))
                        bamlFiles.Add(clientsFile);
                    if (File.Exists(generatorsFile))
                        bamlFiles.Add(generatorsFile);
                }

                if (bamlFiles.Count == 0)
                {
                    Console.WriteLine("No BAML files specified and couldn't find test files.");
                    Console.WriteLine("Please specify BAML files with --baml-file or -f");
                    return 1;
                }
            }

            // If no CLI args specified, use the same defaults as the MSBuild task
            if (cliArgs.Count == 0)
            {
                cliArgs = new List<string> { "generate", "--target", "csharp/metadata", "--output", outputPath };
            }

            Console.WriteLine($"Project Directory: {projectDir}");
            Console.WriteLine($"Output Path: {outputPath}");
            Console.WriteLine($"BAML Files ({bamlFiles.Count}):");
            foreach (var file in bamlFiles)
            {
                Console.WriteLine($"  - {file}");
            }
            Console.WriteLine($"CLI Arguments: {string.Join(" ", cliArgs)}");
            Console.WriteLine();

            try
            {
                // Replicate the MSBuild task behavior
                Console.WriteLine("Step 1: Ensuring output directory exists...");
                Directory.CreateDirectory(outputPath);

                Console.WriteLine("Step 2: Reading BAML files into dictionary...");
                var fileDict = new Dictionary<string, string>();
                foreach (var file in bamlFiles)
                {
                    if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    {
                        var content = File.ReadAllText(file);
                        var relativePath = Path.GetFileName(file);
                        fileDict[relativePath] = content;
                        Console.WriteLine($"  Loaded: {relativePath} ({content.Length} bytes)");
                    }
                    else
                    {
                        Console.WriteLine($"  Warning: File not found - {file}");
                    }
                }

                if (!fileDict.Any())
                {
                    Console.WriteLine("ERROR: No valid BAML files found");
                    return 1;
                }

                Console.WriteLine($"Step 3: Creating BamlRuntime from {fileDict.Count} file(s)...");
                BamlRuntime runtime;
                try
                {
                    runtime = BamlRuntime.FromFiles(projectDir, fileDict);
                    Console.WriteLine("  Runtime created successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Failed to create BAML runtime: {ex.Message}");
                    Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
                    return 1;
                }

                Console.WriteLine($"Step 4: Invoking BAML CLI with args: {string.Join(" ", cliArgs)}");
                Console.WriteLine("  This is where the MSBuild task crashes...");
                Console.WriteLine();

                int exitCode;
                try
                {
                    exitCode = runtime.InvokeCli(cliArgs.ToArray());
                    Console.WriteLine($"  CLI exited with code: {exitCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Failed to invoke BAML CLI: {ex.Message}");
                    Console.WriteLine($"Exception type: {ex.GetType().FullName}");
                    Console.WriteLine($"Stack trace:\n{ex.StackTrace}");

                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"\nInner exception: {ex.InnerException.Message}");
                        Console.WriteLine($"Inner exception type: {ex.InnerException.GetType().FullName}");
                        Console.WriteLine($"Inner stack trace:\n{ex.InnerException.StackTrace}");
                    }

                    runtime.Dispose();
                    return 1;
                }

                runtime.Dispose();

                if (exitCode != 0)
                {
                    Console.WriteLine($"ERROR: BAML CLI exited with non-zero code: {exitCode}");
                    return exitCode;
                }

                // Check if metadata file was generated
                var metadataPath = Path.Combine(outputPath, "baml-metadata.json");
                if (File.Exists(metadataPath))
                {
                    Console.WriteLine($"\nStep 5: Metadata file generated successfully: {metadataPath}");
                    var metadata = File.ReadAllText(metadataPath);
                    Console.WriteLine($"  Size: {metadata.Length} bytes");

                    if (metadata.Length < 1000)
                    {
                        Console.WriteLine($"  Content:\n{metadata}");
                    }
                    else
                    {
                        Console.WriteLine($"  Preview:\n{metadata.Substring(0, 500)}...");
                    }
                }
                else
                {
                    Console.WriteLine($"WARNING: Expected metadata file not found at: {metadataPath}");
                }

                Console.WriteLine("\nSUCCESS: CLI invocation completed without crashes!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UNEXPECTED ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
                return 1;
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("Usage: Baml.Net.CliDebugger [options] [baml-files...]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -p, --project-dir <path>   Project directory (default: current directory)");
            Console.WriteLine("  -o, --output <path>        Output directory for metadata (default: ./output)");
            Console.WriteLine("  -f, --baml-file <file>     BAML file to process (can be specified multiple times)");
            Console.WriteLine("  --cli-args <args...>       Arguments to pass to InvokeCli (default: generate --target csharp/metadata --output <output>)");
            Console.WriteLine("  -h, --help                 Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  # Use test files with default CLI args");
            Console.WriteLine("  Baml.Net.CliDebugger");
            Console.WriteLine();
            Console.WriteLine("  # Specify custom files and output");
            Console.WriteLine("  Baml.Net.CliDebugger -f file1.baml -f file2.baml -o ./metadata");
            Console.WriteLine();
            Console.WriteLine("  # Pass custom CLI arguments");
            Console.WriteLine("  Baml.Net.CliDebugger --cli-args generate --target csharp/metadata --output ./custom");
        }
    }
}