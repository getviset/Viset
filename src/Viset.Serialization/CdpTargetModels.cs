namespace Viset.Serialization;

public sealed record CdpTargetModel(string Type, string Url, string WebSocketDebuggerUrl)
{
    public CdpTargetModel()
        : this(Type: string.Empty, Url: string.Empty, WebSocketDebuggerUrl: string.Empty) { }
}
