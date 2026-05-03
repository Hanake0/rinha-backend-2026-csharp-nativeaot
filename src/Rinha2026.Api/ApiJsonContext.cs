using System.Text.Json.Serialization;

using Rinha2026.Api.Services;

namespace Rinha2026.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RequestProfileSnapshot))]
internal sealed partial class ApiJsonContext : JsonSerializerContext {
}
