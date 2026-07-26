using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Viset.Serialization;

public sealed record CaptureTomlModel(
    [property: TomlRequired] long? Version,
    string OutputRoot,
    [property: TomlRequired] string Output,
    string Frame,
    long? FramesPerSecond,
    WebpTomlModel? Webp,
    List<string> BrowserArguments,
    [property: TomlRequired] Dictionary<string, DeviceTomlModel> Devices,
    TomlTable Matrix,
    TomlTable Data,
    [property: TomlExtensionData] TomlTable Unmapped
)
{
    public CaptureTomlModel()
        : this(
            Version: null,
            OutputRoot: string.Empty,
            Output: null!,
            Frame: string.Empty,
            FramesPerSecond: null,
            Webp: null,
            BrowserArguments: [],
            Devices: new(StringComparer.Ordinal),
            Matrix: [],
            Data: [],
            Unmapped: []
        ) { }
}

public sealed record WebpTomlModel(
    string Source,
    long? SourceQuality,
    string Encoder,
    string Pipeline,
    string Mode,
    long? Method,
    double? Quality,
    [property: TomlExtensionData] TomlTable Unmapped
)
{
    public WebpTomlModel()
        : this(
            Source: string.Empty,
            SourceQuality: null,
            Encoder: string.Empty,
            Pipeline: string.Empty,
            Mode: string.Empty,
            Method: null,
            Quality: null,
            Unmapped: []
        ) { }
}

public sealed record DeviceTomlModel(
    bool? Mobile,
    bool? Touch,
    double? DeviceScale,
    [property: TomlRequired] DimensionsTomlModel Viewport,
    DimensionsTomlModel? Frame,
    [property: TomlExtensionData] TomlTable Unmapped
)
{
    public DeviceTomlModel()
        : this(
            Mobile: null,
            Touch: null,
            DeviceScale: null,
            Viewport: null!,
            Frame: null,
            Unmapped: []
        ) { }
}

public sealed record DimensionsTomlModel(
    [property: TomlRequired] long? Width,
    [property: TomlRequired] long? Height,
    [property: TomlExtensionData] TomlTable Unmapped
)
{
    public DimensionsTomlModel()
        : this(Width: null, Height: null, Unmapped: []) { }
}
