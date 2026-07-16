using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace Viset.Serialization;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(JsonElement))]
internal partial class CdpJsonSerializerContext : JsonSerializerContext;

[TomlSerializable(typeof(TomlMetadataBootstrap))]
internal partial class TomlSerializerContext : Tomlyn.Serialization.TomlSerializerContext;

internal sealed class TomlMetadataBootstrap
{
    public int Version { get; init; }
}
