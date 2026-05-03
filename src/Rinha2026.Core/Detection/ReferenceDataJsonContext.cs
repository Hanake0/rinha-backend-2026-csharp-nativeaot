using System.Text.Json.Serialization;

namespace Rinha2026.Core.Detection;

[JsonSerializable(typeof(Dictionary<string, float>))]
[JsonSerializable(typeof(NormalizationFile))]
public sealed partial class ReferenceDataJsonContext : JsonSerializerContext;
