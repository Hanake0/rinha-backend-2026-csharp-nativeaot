using System.Text.Json.Serialization;

namespace Rinha2026.Core.Indexing;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IndexManifest))]
public sealed partial class IndexManifestJsonContext : JsonSerializerContext;
