using System.Text.Json;

namespace Viset.Serialization;

public sealed record CdpEvaluateParameters(string Expression, bool AwaitPromise, bool ReturnByValue, bool UserGesture)
{
    public CdpEvaluateParameters()
        : this(Expression: string.Empty, AwaitPromise: true, ReturnByValue: true, UserGesture: true) { }
}

public sealed record CdpEvaluateResultModel(CdpRemoteObjectModel Result, CdpExceptionDetailsModel? ExceptionDetails)
{
    public CdpEvaluateResultModel()
        : this(Result: new(), ExceptionDetails: null) { }
}

public sealed record CdpRemoteObjectModel(string Type, string? Subtype, JsonElement Value, string? Description)
{
    public CdpRemoteObjectModel()
        : this(Type: string.Empty, Subtype: null, Value: default, Description: null) { }
}

public sealed record CdpExceptionDetailsModel(
    string Text,
    int LineNumber,
    int ColumnNumber,
    CdpRemoteObjectModel? Exception
)
{
    public CdpExceptionDetailsModel()
        : this(Text: string.Empty, LineNumber: 0, ColumnNumber: 0, Exception: null) { }
}
