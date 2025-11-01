using System;
using System.IO;
using Baml.Net.SourceGenerator;
using Baml.Net.SourceGenerator.Metadata;

var metadataJson = File.ReadAllText("../../test-metadata-example.json");
var parser = new MetadataParser();
var metadata = parser.Parse(metadataJson);

Console.WriteLine($"Parsed metadata:");
Console.WriteLine($"  Version: {metadata.Version}");
Console.WriteLine($"  Namespace: {metadata.Namespace}");
Console.WriteLine($"  Types: {metadata.Types.Count}");
Console.WriteLine($"  Functions: {metadata.Functions.Count}");

// Test code generation - create a fake generator
// (This is just for manual verification, not automated test)
