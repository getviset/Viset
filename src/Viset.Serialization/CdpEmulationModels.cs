namespace Viset.Serialization;

public sealed record CdpDeviceMetricsParameters(int Width, int Height, double DeviceScaleFactor, bool Mobile)
{
    public CdpDeviceMetricsParameters()
        : this(Width: 0, Height: 0, DeviceScaleFactor: 0.0, Mobile: false) { }
}

public sealed record CdpTouchEmulationParameters(bool Enabled, int MaxTouchPoints)
{
    public CdpTouchEmulationParameters()
        : this(Enabled: false, MaxTouchPoints: 0) { }
}

public sealed record CdpBackgroundParameters(CdpColorModel Color)
{
    public CdpBackgroundParameters()
        : this(Color: new()) { }
}

public sealed record CdpColorModel(int R, int G, int B, double A)
{
    public CdpColorModel()
        : this(R: 0, G: 0, B: 0, A: 0.0) { }
}
