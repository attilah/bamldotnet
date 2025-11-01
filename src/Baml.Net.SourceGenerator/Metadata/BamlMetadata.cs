using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Baml.Net.SourceGenerator.Metadata
{
    internal class BamlMetadata
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("namespace")]
        public string Namespace { get; set; } = "BamlClient";

        [JsonPropertyName("types")]
        public List<TypeDefinition> Types { get; set; } = new();

        [JsonPropertyName("functions")]
        public List<FunctionDefinition> Functions { get; set; } = new();

        [JsonPropertyName("clients")]
        public List<ClientDefinition> Clients { get; set; } = new();

        [JsonPropertyName("retryPolicies")]
        public List<RetryPolicyDefinition> RetryPolicies { get; set; } = new();
    }

    internal class TypeDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("properties")]
        public List<PropertyDefinition>? Properties { get; set; }

        [JsonPropertyName("values")]
        public List<EnumValueDefinition>? Values { get; set; }

        [JsonPropertyName("isDynamic")]
        public bool IsDynamic { get; set; }
    }

    internal class PropertyDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("nullable")]
        public bool Nullable { get; set; }
    }

    internal class EnumValueDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    internal class FunctionDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("async")]
        public bool IsAsync { get; set; } = true;

        [JsonPropertyName("parameters")]
        public List<ParameterDefinition> Parameters { get; set; } = new();

        [JsonPropertyName("returnType")]
        public string ReturnType { get; set; } = "";

        [JsonPropertyName("returnNullable")]
        public bool ReturnNullable { get; set; }
    }

    internal class ParameterDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("nullable")]
        public bool Nullable { get; set; }
    }

    internal class ClientDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "";

        [JsonPropertyName("retryPolicy")]
        public string? RetryPolicy { get; set; }
    }

    internal class RetryPolicyDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; }

        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = "";
    }
}
