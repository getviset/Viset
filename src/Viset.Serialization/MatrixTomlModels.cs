using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Viset.Serialization;

public static class MatrixTomlModels
{
    public static MatrixTomlModel Deserialize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return TomlSerializer.Deserialize(source, TomlModelContext.Default.MatrixTomlModel)
            ?? throw new InvalidOperationException("Tomlyn returned no Matrix v1 model.");
    }
}

public sealed class MatrixTomlModel
{
    [TomlRequired]
    public long? Version { get; set; }

    public string DefaultOutput { get; set; } = string.Empty;

    [TomlRequired]
    public string Adapter { get; set; } = null!;

    public string Frame { get; set; } = string.Empty;

    public long? FramesPerSecond { get; set; }

    public List<string> BrowserArguments { get; set; } = [];

    [TomlRequired]
    public Dictionary<string, DeviceTomlModel> Devices { get; set; } = new(StringComparer.Ordinal);

    public List<StillTomlModel> Stills { get; set; } = [];

    public List<AnimationTomlModel> Animations { get; set; } = [];
}

public sealed class DeviceTomlModel
{
    public bool? Mobile { get; set; }

    public bool? Touch { get; set; }

    public double? DeviceScale { get; set; }

    [TomlRequired]
    public DimensionsTomlModel Viewport { get; set; } = null!;

    public DimensionsTomlModel? Frame { get; set; }
}

public sealed class DimensionsTomlModel
{
    [TomlRequired]
    public long? Width { get; set; }

    [TomlRequired]
    public long? Height { get; set; }
}

public sealed class StillTomlModel
{
    [TomlRequired]
    public string Id { get; set; } = null!;

    [TomlRequired]
    public string Name { get; set; } = null!;

    [TomlRequired]
    public TomlTable Matrix { get; set; } = null!;

    public TomlTable Data { get; set; } = [];
}

public sealed class AnimationTomlModel
{
    [TomlRequired]
    public string Id { get; set; } = null!;

    [TomlRequired]
    public string Name { get; set; } = null!;

    [TomlRequired]
    public string Workflow { get; set; } = null!;

    [TomlRequired]
    public TomlTable Matrix { get; set; } = null!;

    public TomlTable Data { get; set; } = [];
}
