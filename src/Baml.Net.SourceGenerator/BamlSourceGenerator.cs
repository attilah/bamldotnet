using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Baml.Net.SourceGenerator
{
    /// <summary>
    /// Incremental source generator for BAML files that generates typed client code.
    /// </summary>
    [Generator]
    public class BamlSourceGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Initializes the incremental generator.
        /// </summary>
        /// <param name="context">The initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Register MSBuild properties
            var enableCodeGenProvider = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.BamlEnableCodeGeneration", out var value);
                    return value?.ToLower() == "true" || string.IsNullOrEmpty(value);
                });

            var enableLoggingProvider = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.BamlEnableCodeGenLogging", out var value);
                    return value?.ToLower() == "true";
                });

            var rootNamespaceProvider = context.AnalyzerConfigOptionsProvider
                .Select((options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value);
                    return !string.IsNullOrWhiteSpace(value) ? value : "BamlClient";
                });

            // Register additional files (BAML files AND metadata files)
            var additionalFilesProvider = context.AdditionalTextsProvider
                .Collect();

            // Combine all providers
            var combinedProvider = additionalFilesProvider
                .Combine(enableCodeGenProvider)
                .Combine(enableLoggingProvider)
                .Combine(rootNamespaceProvider);

            // Register source output
            context.RegisterSourceOutput(combinedProvider, (spc, source) =>
            {
                var (((additionalFiles, enableCodeGen), enableLogging), rootNamespace) = source;

                // Check if code generation is enabled
                if (!enableCodeGen)
                    return;

                if (additionalFiles.IsEmpty)
                    return;

                // Filter for BAML files
                var bamlFiles = additionalFiles
                    .Where(f => Path.GetExtension(f.Path).Equals(".baml", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!bamlFiles.Any())
                    return;

                try
                {
                    // Generate code - pass ALL additional files so it can find metadata
                    var generator = new BamlCodeGenerator(enableLogging, rootNamespace ?? "BamlClient");
                    var generatedCode = generator.GenerateCode(additionalFiles.ToList());

                    if (!string.IsNullOrEmpty(generatedCode))
                    {
                        spc.AddSource("BamlClient.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
                    }
                }
                catch (Exception ex)
                {
                    // Report diagnostic for any errors
                    var diagnostic = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "BAML001",
                            "BAML Source Generation Failed",
                            "BAML source generation failed: {0}",
                            "Baml.SourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None,
                        ex.Message);

                    spc.ReportDiagnostic(diagnostic);
                }
            });
        }
    }
}
