namespace Viset.Serialization;

public sealed record CdpDispatchTouchEventParameters(string Type, List<CdpTouchPointModel> TouchPoints)
{
    public CdpDispatchTouchEventParameters()
        : this(Type: string.Empty, TouchPoints: []) { }
}

public sealed record CdpTouchPointModel(double X, double Y, double RadiusX, double RadiusY, double Force)
{
    public CdpTouchPointModel()
        : this(X: 0.0, Y: 0.0, RadiusX: 1.0, RadiusY: 1.0, Force: 1.0) { }
}
