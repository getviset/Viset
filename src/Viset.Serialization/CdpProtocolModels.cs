using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viset.Serialization;

public sealed record CdpCommandModel(
    long Id,
    string Method,
    [property: JsonPropertyName("params")] JsonElement Parameters
)
{
    public CdpCommandModel()
        : this(Id: 0, Method: string.Empty, Parameters: default) { }
}

public sealed record CdpIncomingMessageModel(
    long? Id,
    JsonElement Result,
    CdpErrorModel? Error,
    string? Method,
    [property: JsonPropertyName("params")] JsonElement Parameters
)
{
    public CdpIncomingMessageModel()
        : this(Id: null, Result: default, Error: null, Method: null, Parameters: default) { }
}

public sealed record CdpErrorModel(int Code, string Message, JsonElement Data)
{
    public CdpErrorModel()
        : this(Code: 0, Message: string.Empty, Data: default) { }
}

public sealed record CdpEmptyParameters();
