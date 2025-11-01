using System;
using System.Text.Json;
using Baml.Net.SourceGenerator.Metadata;

namespace Baml.Net.SourceGenerator
{
    internal class MetadataParser
    {
        private readonly JsonSerializerOptions _options;

        public MetadataParser()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
        }

        public BamlMetadata Parse(string json)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<BamlMetadata>(json, _options);
                if (metadata == null)
                {
                    throw new InvalidOperationException("Failed to deserialize BAML metadata");
                }

                // Validate metadata
                ValidateMetadata(metadata);

                return metadata;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid BAML metadata JSON: {ex.Message}", ex);
            }
        }

        private void ValidateMetadata(BamlMetadata metadata)
        {
            if (string.IsNullOrEmpty(metadata.Version))
                throw new InvalidOperationException("Metadata version is missing");

            if (string.IsNullOrEmpty(metadata.Namespace))
                throw new InvalidOperationException("Metadata namespace is missing");

            // Additional validation as needed
            foreach (var type in metadata.Types)
            {
                if (string.IsNullOrEmpty(type.Name))
                    throw new InvalidOperationException("Type name is missing");

                if (string.IsNullOrEmpty(type.Kind))
                    throw new InvalidOperationException($"Type kind is missing for {type.Name}");
            }

            foreach (var function in metadata.Functions)
            {
                if (string.IsNullOrEmpty(function.Name))
                    throw new InvalidOperationException("Function name is missing");

                if (string.IsNullOrEmpty(function.ReturnType))
                    throw new InvalidOperationException($"Return type is missing for function {function.Name}");
            }
        }
    }
}
