using System.Text.Json.Serialization;

using Rinha2026.Core.Indexing;

namespace Rinha2026.IndexBuilder.Build;

[JsonSerializable(typeof(IndexManifest))]
[JsonSerializable(typeof(ReferenceVectorRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ReferenceCorpusJsonContext : JsonSerializerContext {
}

internal sealed class ReferenceVectorRecord {
	public string Label { get; init; } = string.Empty;

	public float[] Vector { get; init; } = [];
}
