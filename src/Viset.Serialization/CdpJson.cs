using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Viset.Serialization;

public static class CdpJson
{
    public static byte[] SerializeCommand(long id, string method, CdpEmptyParameters parameters) =>
        SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpEmptyParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpNavigateParameters parameters
    ) => SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpNavigateParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpEvaluateParameters parameters
    ) => SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpEvaluateParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpDeviceMetricsParameters parameters
    ) =>
        SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpDeviceMetricsParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpTouchEmulationParameters parameters
    ) =>
        SerializeCommand(
            id,
            method,
            parameters,
            CdpJsonContext.Default.CdpTouchEmulationParameters
        );

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpDispatchTouchEventParameters parameters
    ) =>
        SerializeCommand(
            id,
            method,
            parameters,
            CdpJsonContext.Default.CdpDispatchTouchEventParameters
        );

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpBackgroundParameters parameters
    ) => SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpBackgroundParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpScreenshotParameters parameters
    ) => SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpScreenshotParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpScreencastParameters parameters
    ) => SerializeCommand(id, method, parameters, CdpJsonContext.Default.CdpScreencastParameters);

    public static byte[] SerializeCommand(
        long id,
        string method,
        CdpScreencastFrameAckParameters parameters
    ) =>
        SerializeCommand(
            id,
            method,
            parameters,
            CdpJsonContext.Default.CdpScreencastFrameAckParameters
        );

    public static CdpIncomingMessageModel DeserializeIncoming(byte[] utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, CdpJsonContext.Default.CdpIncomingMessageModel)
        ?? throw new JsonException("CDP returned an empty message.");

    public static List<CdpTargetModel> DeserializeTargets(string json) =>
        JsonSerializer.Deserialize(json, CdpJsonContext.Default.ListCdpTargetModel)
        ?? throw new JsonException("The DevTools target list was empty.");

    public static CdpNavigateResultModel DeserializeNavigateResult(JsonElement element) =>
        element.Deserialize(CdpJsonContext.Default.CdpNavigateResultModel)
        ?? throw new JsonException("Page.navigate returned no result.");

    public static CdpEvaluateResultModel DeserializeEvaluateResult(JsonElement element) =>
        element.Deserialize(CdpJsonContext.Default.CdpEvaluateResultModel)
        ?? throw new JsonException("Runtime.evaluate returned no result.");

    public static CdpScreenshotResultModel DeserializeScreenshotResult(JsonElement element) =>
        element.Deserialize(CdpJsonContext.Default.CdpScreenshotResultModel)
        ?? throw new JsonException("Page.captureScreenshot returned no result.");

    public static CdpScreencastFrameModel DeserializeScreencastFrame(JsonElement element) =>
        element.Deserialize(CdpJsonContext.Default.CdpScreencastFrameModel)
        ?? throw new JsonException("Page.screencastFrame returned no parameters.");

    private static byte[] SerializeCommand<TParameters>(
        long id,
        string method,
        TParameters parameters,
        JsonTypeInfo<TParameters> parameterTypeInfo
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var command = new CdpCommandModel(
            id,
            method,
            JsonSerializer.SerializeToElement(parameters, parameterTypeInfo)
        );

        return JsonSerializer.SerializeToUtf8Bytes(command, CdpJsonContext.Default.CdpCommandModel);
    }
}
