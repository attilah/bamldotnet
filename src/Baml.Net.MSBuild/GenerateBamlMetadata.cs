using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Baml.Net.Core;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Baml.Net.MSBuild
{
    /// <summary>
    /// MSBuild task that generates BAML metadata JSON from BAML source files.
    /// </summary>
    public class GenerateBamlMetadata : BuildTask
    {
        /// <summary>
        /// The root directory containing BAML files.
        /// </summary>
        [Required]
        public string? ProjectDir { get; set; }

        /// <summary>
        /// The intermediate output directory where metadata will be generated.
        /// </summary>
        [Required]
        public string? IntermediateOutputPath { get; set; }

        /// <summary>
        /// BAML source files to process.
        /// </summary>
        [Required]
        public ITaskItem[]? BamlFiles { get; set; }

        /// <summary>
        /// Output metadata file path.
        /// </summary>
        [Output]
        public string? MetadataFile { get; set; }

        public override bool Execute()
        {
            try
            {
                if (BamlFiles == null || BamlFiles.Length == 0)
                {
                    Log.LogMessage(MessageImportance.Low, "No BAML files to process, skipping metadata generation");
                    return true;
                }

                if (string.IsNullOrEmpty(ProjectDir))
                {
                    Log.LogError("ProjectDir is required");
                    return false;
                }

                if (string.IsNullOrEmpty(IntermediateOutputPath))
                {
                    Log.LogError("IntermediateOutputPath is required");
                    return false;
                }

                // Ensure intermediate output directory exists
                Directory.CreateDirectory(IntermediateOutputPath);

                // Output metadata file path
                var metadataPath = Path.Combine(IntermediateOutputPath, "baml-metadata.json");
                MetadataFile = metadataPath;

                Log.LogMessage(MessageImportance.High, $"Generating BAML metadata from {BamlFiles.Length} file(s)...");

                // Read BAML files into dictionary
                var fileDict = new Dictionary<string, string>();
                foreach (var file in BamlFiles)
                {
                    var fullPath = file.GetMetadata("FullPath");
                    if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                    {
                        var content = File.ReadAllText(fullPath);
                        var relativePath = Path.GetFileName(fullPath);
                        fileDict[relativePath] = content;
                        Log.LogMessage(MessageImportance.Low, $"  Processing: {fullPath}");
                    }
                }

                if (!fileDict.Any())
                {
                    Log.LogWarning("No valid BAML files found");
                    return true;
                }

                // Create runtime from files
                BamlRuntime runtime;
                try
                {
                    runtime = BamlRuntime.FromFiles(ProjectDir, fileDict);
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to create BAML runtime: {ex.Message}");
                    Log.LogMessage(MessageImportance.High, $"Exception details: {ex}");
                    return false;
                }

                // Invoke CLI to generate metadata using csharp/metadata target
                var outputDir = Path.GetDirectoryName(metadataPath);
                // First arg is program name (argv[0]), then actual command and flags
                // Check if there's a baml_src subdirectory, otherwise use project directory
                var bamlSrcPath = Path.Combine(ProjectDir, "baml_src");
                var fromPath = Directory.Exists(bamlSrcPath) ? bamlSrcPath : ProjectDir;
                var args = new[] { "baml", "generate", "--from", fromPath };

                Log.LogMessage(MessageImportance.High, $"Invoking BAML CLI: {string.Join(" ", args)}");

                int exitCode;
                try
                {
                    exitCode = runtime.InvokeCli(args);
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to invoke BAML CLI: {ex.Message}");
                    Log.LogMessage(MessageImportance.High, $"Exception details: {ex}");
                    runtime.Dispose();
                    return false;
                }

                runtime.Dispose();

                if (exitCode != 0)
                {
                    Log.LogError($"BAML CLI exited with code {exitCode}");
                    return false;
                }

                // Look for the generated metadata file
                // The CLI generates it based on the output_dir in the generator configuration
                // Common locations include baml_client/baml-metadata.json or baml_client/baml_client/baml-metadata.json
                string? generatedMetadataPath = null;

                // Check common output locations relative to project directory
                // Check nested directories first as they often contain the actual generated content
                var possiblePaths = new[]
                {
                    Path.Combine(ProjectDir, "baml_client", "baml_client", "baml-metadata.json"),
                    Path.Combine(ProjectDir, "baml_client", "baml-metadata.json"),
                    Path.Combine(ProjectDir, "generated", "baml-metadata.json"),
                    Path.Combine(ProjectDir, "output", "baml-metadata.json"),
                    Path.Combine(ProjectDir, "baml-metadata.json")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        generatedMetadataPath = path;
                        Log.LogMessage(MessageImportance.High, $"Found generated metadata at: {path}");
                        break;
                    }
                }

                if (generatedMetadataPath == null)
                {
                    Log.LogError($"BAML CLI did not generate metadata file. Checked locations: {string.Join(", ", possiblePaths)}");
                    return false;
                }

                // Copy the metadata to the expected location for the source generator
                if (generatedMetadataPath != metadataPath)
                {
                    File.Copy(generatedMetadataPath, metadataPath, overwrite: true);
                    Log.LogMessage(MessageImportance.High, $"Copied metadata from {generatedMetadataPath} to {metadataPath}");
                }

                Log.LogMessage(MessageImportance.High, $"Successfully generated BAML metadata: {metadataPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex, showStackTrace: true);
                return false;
            }
        }
    }
}
