namespace Viset.Serialization;

public sealed record CdpNavigateParameters(string Url)
{
    public CdpNavigateParameters()
        : this(Url: string.Empty) { }
}

public sealed record CdpNavigateResultModel(string FrameId, string? ErrorText)
{
    public CdpNavigateResultModel()
        : this(FrameId: string.Empty, ErrorText: null) { }
}
