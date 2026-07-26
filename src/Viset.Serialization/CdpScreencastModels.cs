namespace Viset.Serialization;

public sealed record CdpScreencastParameters(
    string Format,
    int? Quality,
    int? MaxWidth,
    int? MaxHeight,
    int EveryNthFrame
)
{
    public CdpScreencastParameters()
        : this(Format: "png", Quality: null, MaxWidth: null, MaxHeight: null, EveryNthFrame: 1) { }
}

public sealed record CdpScreencastFrameAckParameters(int SessionId)
{
    public CdpScreencastFrameAckParameters()
        : this(SessionId: 0) { }
}

public sealed record CdpScreencastFrameModel(
    string Data,
    CdpScreencastFrameMetadataModel Metadata,
    int SessionId
)
{
    public CdpScreencastFrameModel()
        : this(Data: string.Empty, Metadata: new(), SessionId: 0) { }
}

public sealed record CdpScreencastFrameMetadataModel(
    double Timestamp,
    double DeviceWidth,
    double DeviceHeight,
    double PageScaleFactor,
    double OffsetTop,
    double ScrollOffsetX,
    double ScrollOffsetY
)
{
    public CdpScreencastFrameMetadataModel()
        : this(
            Timestamp: 0.0,
            DeviceWidth: 0.0,
            DeviceHeight: 0.0,
            PageScaleFactor: 0.0,
            OffsetTop: 0.0,
            ScrollOffsetX: 0.0,
            ScrollOffsetY: 0.0
        ) { }
}
