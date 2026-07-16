using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viset.Serialization;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(JsonElement))]
internal partial class CdpJsonSerializerContext : JsonSerializerContext;
