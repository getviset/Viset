namespace Viset

open System
open System.Text.Json
open System.Threading
open Viset.Serialization

type CdpClient private (transport: CdpTransport) =
    member private _.RequireSuccess(result: Result<JsonElement, CdpProtocolError>) =
        match result with
        | Ok value -> value
        | Error error -> raise (CdpProtocolException error)

    member this.EnablePageAndRuntimeAsync(cancellationToken: CancellationToken) =
        task {
            let! page = transport.SendEmptyAsync("Page.enable", cancellationToken)
            this.RequireSuccess page |> ignore
            let! runtime = transport.SendEmptyAsync("Runtime.enable", cancellationToken)
            this.RequireSuccess runtime |> ignore
        }

    member _.WaitForEventAsync(methodName: string, cancellationToken: CancellationToken) =
        task {
            let! _ = transport.ReadEventAsync(methodName, cancellationToken)
            return ()
        }

    member this.NavigateAsync(url: Uri, cancellationToken: CancellationToken) =
        task {
            ArgumentNullException.ThrowIfNull url
            transport.ClearEvents "Page.loadEventFired"

            use loadCancellation =
                CancellationTokenSource.CreateLinkedTokenSource cancellationToken

            let loadTask = this.WaitForEventAsync("Page.loadEventFired", loadCancellation.Token)

            try
                let parameters = CdpNavigateParameters(url.AbsoluteUri)

                let! response =
                    transport.SendCommandAsync(
                        "Page.navigate",
                        (fun id -> CdpJson.SerializeCommand(id, "Page.navigate", parameters)),
                        cancellationToken
                    )

                let result = this.RequireSuccess response |> CdpJson.DeserializeNavigateResult

                if not (String.IsNullOrWhiteSpace result.ErrorText) then
                    raise (
                        InvalidOperationException(
                            String.Concat("Page navigation failed: ", result.ErrorText)
                        )
                    )

                do! loadTask
            with error ->
                loadCancellation.Cancel()
                return raise error
        }

    member _.EvaluateAsync(expression: string, cancellationToken: CancellationToken) =
        task {
            ArgumentException.ThrowIfNullOrWhiteSpace expression
            let parameters = CdpEvaluateParameters(expression, true, true, true)

            let! response =
                transport.SendCommandAsync(
                    "Runtime.evaluate",
                    (fun id -> CdpJson.SerializeCommand(id, "Runtime.evaluate", parameters)),
                    cancellationToken
                )

            match response with
            | Error error -> return Error(Protocol error)
            | Ok resultElement ->
                let result = CdpJson.DeserializeEvaluateResult resultElement

                match Option.ofObj result.ExceptionDetails with
                | Some exceptionDetails ->
                    let description =
                        match Option.ofObj exceptionDetails.Exception with
                        | Some exceptionObject ->
                            exceptionObject.Description
                            |> Option.ofObj
                            |> Option.filter (String.IsNullOrWhiteSpace >> not)
                            |> Option.defaultValue exceptionDetails.Text
                        | None -> exceptionDetails.Text

                    return Error(JavaScript description)
                | None ->
                    let remote = result.Result

                    match remote.Type with
                    | "undefined" -> return Ok Undefined
                    | "boolean" -> return Ok(CdpEvaluationValue.Boolean(remote.Value.GetBoolean()))
                    | "number" -> return Ok(Number(remote.Value.GetDouble()))
                    | "string" ->
                        return
                            Ok(
                                CdpEvaluationValue.String(
                                    remote.Value.GetString()
                                    |> Option.ofObj
                                    |> Option.defaultValue String.Empty
                                )
                            )
                    | "object" when String.Equals(remote.Subtype, "null", StringComparison.Ordinal) ->
                        return Ok Null
                    | _ when remote.Value.ValueKind <> JsonValueKind.Undefined ->
                        return Ok(Json(remote.Value.Clone()))
                    | unsupported ->
                        return
                            Error(
                                JavaScript(
                                    String.Concat(
                                        "Runtime.evaluate returned unsupported type '",
                                        unsupported,
                                        "'."
                                    )
                                )
                            )
        }

    member this.ConfigureEmulationAsync
        (
            width: int,
            height: int,
            deviceScaleFactor: double,
            mobile: bool,
            touch: bool,
            cancellationToken: CancellationToken
        ) =
        task {
            if width <= 0 || height <= 0 then
                invalidArg (nameof width) "Emulation dimensions must be positive."

            if not (Double.IsFinite deviceScaleFactor) || deviceScaleFactor <= 0.0 then
                invalidArg
                    (nameof deviceScaleFactor)
                    "Device scale factor must be positive and finite."

            let metrics = CdpDeviceMetricsParameters(width, height, deviceScaleFactor, mobile)

            let! metricsResponse =
                transport.SendCommandAsync(
                    "Emulation.setDeviceMetricsOverride",
                    (fun id ->
                        CdpJson.SerializeCommand(id, "Emulation.setDeviceMetricsOverride", metrics)),
                    cancellationToken
                )

            this.RequireSuccess metricsResponse |> ignore

            let touchParameters = CdpTouchEmulationParameters(touch, 1)

            let! touchResponse =
                transport.SendCommandAsync(
                    "Emulation.setTouchEmulationEnabled",
                    (fun id ->
                        CdpJson.SerializeCommand(
                            id,
                            "Emulation.setTouchEmulationEnabled",
                            touchParameters
                        )),
                    cancellationToken
                )

            this.RequireSuccess touchResponse |> ignore
        }

    member this.SetTransparentBackgroundAsync(cancellationToken: CancellationToken) =
        task {
            let parameters = CdpBackgroundParameters(CdpColorModel(0, 0, 0, 0.0))

            let! response =
                transport.SendCommandAsync(
                    "Emulation.setDefaultBackgroundColorOverride",
                    (fun id ->
                        CdpJson.SerializeCommand(
                            id,
                            "Emulation.setDefaultBackgroundColorOverride",
                            parameters
                        )),
                    cancellationToken
                )

            this.RequireSuccess response |> ignore
        }

    member this.TouchAsync(x: double, y: double, cancellationToken: CancellationToken) =
        task {
            if not (Double.IsFinite x) || not (Double.IsFinite y) || x < 0.0 || y < 0.0 then
                invalidArg (nameof x) "Touch coordinates must be non-negative finite numbers."

            let point = CdpTouchPointModel(x, y, 1.0, 1.0, 1.0)

            let startParameters =
                CdpDispatchTouchEventParameters("touchStart", ResizeArray [ point ])

            let! startResponse =
                transport.SendCommandAsync(
                    "Input.dispatchTouchEvent",
                    (fun id ->
                        CdpJson.SerializeCommand(id, "Input.dispatchTouchEvent", startParameters)),
                    cancellationToken
                )

            this.RequireSuccess startResponse |> ignore

            let endParameters = CdpDispatchTouchEventParameters("touchEnd", ResizeArray())

            let! endResponse =
                transport.SendCommandAsync(
                    "Input.dispatchTouchEvent",
                    (fun id ->
                        CdpJson.SerializeCommand(id, "Input.dispatchTouchEvent", endParameters)),
                    cancellationToken
                )

            this.RequireSuccess endResponse |> ignore
        }

    member this.CapturePngAsync(cancellationToken: CancellationToken) =
        task {
            let parameters = CdpScreenshotParameters()

            let! response =
                transport.SendCommandAsync(
                    "Page.captureScreenshot",
                    (fun id -> CdpJson.SerializeCommand(id, "Page.captureScreenshot", parameters)),
                    cancellationToken
                )

            let result = this.RequireSuccess response |> CdpJson.DeserializeScreenshotResult

            return Convert.FromBase64String result.Data
        }

    member this.StartScreencastAsync
        (source: WebPSource, width: int, height: int, cancellationToken: CancellationToken)
        =
        task {
            if width <= 0 || height <= 0 then
                invalidArg (nameof width) "Screencast dimensions must be positive."

            let parameters =
                match source with
                | PngScreencast ->
                    CdpScreencastParameters(
                        "png",
                        Nullable<int>(),
                        Nullable width,
                        Nullable height,
                        1
                    )
                | JpegScreencast quality ->
                    CdpScreencastParameters(
                        "jpeg",
                        Nullable quality,
                        Nullable width,
                        Nullable height,
                        1
                    )

            transport.ClearEvents "Page.screencastFrame"

            let! response =
                transport.SendCommandAsync(
                    "Page.startScreencast",
                    (fun id -> CdpJson.SerializeCommand(id, "Page.startScreencast", parameters)),
                    cancellationToken
                )

            this.RequireSuccess response |> ignore
        }

    member this.StartScreencastAsync(source: WebPSource, cancellationToken: CancellationToken) =
        task {
            transport.ClearEvents "Page.screencastFrame"

            let parameters =
                match source with
                | PngScreencast ->
                    CdpScreencastParameters(
                        "png",
                        Nullable<int>(),
                        Nullable<int>(),
                        Nullable<int>(),
                        1
                    )
                | JpegScreencast quality ->
                    CdpScreencastParameters(
                        "jpeg",
                        Nullable quality,
                        Nullable<int>(),
                        Nullable<int>(),
                        1
                    )

            let! response =
                transport.SendCommandAsync(
                    "Page.startScreencast",
                    (fun id -> CdpJson.SerializeCommand(id, "Page.startScreencast", parameters)),
                    cancellationToken
                )

            this.RequireSuccess response |> ignore
        }

    member _.ReadScreencastFrameAsync(cancellationToken: CancellationToken) =
        task {
            let! incoming = transport.ReadEventAsync("Page.screencastFrame", cancellationToken)
            let frame = CdpJson.DeserializeScreencastFrame incoming.Parameters

            return
                { Bytes = Convert.FromBase64String frame.Data
                  Timestamp = TimeSpan.FromSeconds frame.Metadata.Timestamp
                  SessionId = frame.SessionId }
        }

    member this.AcknowledgeScreencastFrameAsync
        (sessionId: int, cancellationToken: CancellationToken)
        =
        task {
            if sessionId < 0 then
                invalidArg (nameof sessionId) "Screencast session ID must be non-negative."

            let parameters = CdpScreencastFrameAckParameters(sessionId)

            let! response =
                transport.SendCommandAsync(
                    "Page.screencastFrameAck",
                    (fun id -> CdpJson.SerializeCommand(id, "Page.screencastFrameAck", parameters)),
                    cancellationToken
                )

            this.RequireSuccess response |> ignore
        }

    member this.StopScreencastAsync(cancellationToken: CancellationToken) =
        task {
            let! response = transport.SendEmptyAsync("Page.stopScreencast", cancellationToken)
            this.RequireSuccess response |> ignore
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            (transport :> IAsyncDisposable).DisposeAsync()

    static member ConnectAsync
        (endpoint: Uri, commandTimeout: TimeSpan, cancellationToken: CancellationToken)
        =
        task {
            let! transport = CdpTransport.ConnectAsync(endpoint, commandTimeout, cancellationToken)

            return CdpClient transport
        }
