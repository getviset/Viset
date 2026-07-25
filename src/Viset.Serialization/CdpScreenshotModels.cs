namespace Viset.Serialization;

public sealed record CdpScreenshotParameters(
    string Format,
    bool FromSurface,
    bool CaptureBeyondViewport,
    bool OptimizeForSpeed
)
{
    public CdpScreenshotParameters()
        : this(Format: "png", FromSurface: true, CaptureBeyondViewport: false, OptimizeForSpeed: true) { }
}

public sealed record CdpScreenshotResultModel(string Data)
{
    public CdpScreenshotResultModel()
        : this(Data: string.Empty) { }
}
