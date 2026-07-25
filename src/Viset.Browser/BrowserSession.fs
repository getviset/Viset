namespace Viset

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks

type BrowserSession
    private
    (
        browserProcess: Process,
        profilePath: string,
        client: CdpClient,
        standardOutput: Task<string>,
        standardError: Task<string>
    ) as this =
    let mutable disposed = 0

    member _.ProfilePath = profilePath
    member _.ProcessId = browserProcess.Id
    member _.IsDisposed = Volatile.Read(&disposed) <> 0

    member private _.DisposeCoreAsync() =
        task {
            if Interlocked.Exchange(&disposed, 1) = 0 then
                let failures = ResizeArray<string>()

                try
                    do! (client :> IAsyncDisposable).DisposeAsync().AsTask()
                with error ->
                    failures.Add(String.Concat("Failed to close CDP: ", error.Message))

                let! processExited, processFailure = BrowserProcess.cleanupProcessAsync browserProcess

                processFailure |> Option.iter failures.Add

                if processExited then
                    let! diagnosticsResult = BrowserProcess.readProcessDiagnosticsAsync standardError standardOutput

                    match diagnosticsResult with
                    | Ok _ -> ()
                    | Error diagnosticError -> failures.Add diagnosticError

                try
                    browserProcess.Dispose()
                with error ->
                    failures.Add(String.Concat("Failed to dispose browser process: ", error.Message))

                let! profileFailure = BrowserProcess.deleteProfileAsync profilePath
                profileFailure |> Option.iter failures.Add

                if failures.Count > 0 then
                    raise (InvalidOperationException(String.Join(" ", failures)))
        }

    member private this.RunAsync<'T>
        (operationName: string, cancellationToken: CancellationToken, operation: unit -> Task<'T>)
        =
        task {
            try
                return! operation ()
            with
            | :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                return raise (OperationCanceledException cancellationToken)
            | error ->
                let mutable cleanupFailure = None

                try
                    do! this.DisposeCoreAsync()
                with cleanupError ->
                    cleanupFailure <- Some cleanupError

                let message =
                    match cleanupFailure with
                    | None ->
                        String.Concat(
                            "Browser session operation '",
                            operationName,
                            "' failed; browser process and profile were cleaned up."
                        )
                    | Some cleanupError ->
                        String.Concat(
                            "Browser session operation '",
                            operationName,
                            "' failed and cleanup also failed: ",
                            cleanupError.Message
                        )

                return raise (BrowserSessionException(message, error))
        }

    member this.NavigateAsync(url: Uri, cancellationToken: CancellationToken) =
        this.RunAsync("navigate", cancellationToken, fun () -> client.NavigateAsync(url, cancellationToken))

    member this.EvaluateAsync(expression: string, cancellationToken: CancellationToken) =
        this.RunAsync("evaluate", cancellationToken, fun () -> client.EvaluateAsync(expression, cancellationToken))

    member this.ConfigureEmulationAsync
        (
            width: int,
            height: int,
            deviceScaleFactor: double,
            mobile: bool,
            touch: bool,
            cancellationToken: CancellationToken
        ) =
        this.RunAsync(
            "configure emulation",
            cancellationToken,
            fun () -> client.ConfigureEmulationAsync(width, height, deviceScaleFactor, mobile, touch, cancellationToken)
        )

    member this.SetTransparentBackgroundAsync(cancellationToken: CancellationToken) =
        this.RunAsync(
            "set transparent background",
            cancellationToken,
            fun () -> client.SetTransparentBackgroundAsync cancellationToken
        )

    member this.TouchAsync(x: double, y: double, cancellationToken: CancellationToken) =
        this.RunAsync("dispatch touch", cancellationToken, fun () -> client.TouchAsync(x, y, cancellationToken))

    member this.CapturePngAsync(cancellationToken: CancellationToken) =
        this.RunAsync("capture PNG", cancellationToken, fun () -> client.CapturePngAsync cancellationToken)

    member this.StartScreencastAsync
        (source: WebPSource, width: int, height: int, cancellationToken: CancellationToken)
        =
        this.RunAsync(
            "start screencast",
            cancellationToken,
            fun () -> client.StartScreencastAsync(source, width, height, cancellationToken)
        )

    member this.StartScreencastAsync(source: WebPSource, cancellationToken: CancellationToken) =
        this.RunAsync(
            "start screencast",
            cancellationToken,
            fun () -> client.StartScreencastAsync(source, cancellationToken)
        )

    member this.ReadScreencastFrameAsync(cancellationToken: CancellationToken) =
        this.RunAsync(
            "read screencast frame",
            cancellationToken,
            fun () -> client.ReadScreencastFrameAsync cancellationToken
        )

    member this.AcknowledgeScreencastFrameAsync(sessionId: int, cancellationToken: CancellationToken) =
        this.RunAsync(
            "acknowledge screencast frame",
            cancellationToken,
            fun () -> client.AcknowledgeScreencastFrameAsync(sessionId, cancellationToken)
        )

    member this.StopScreencastAsync(cancellationToken: CancellationToken) =
        this.RunAsync("stop screencast", cancellationToken, fun () -> client.StopScreencastAsync cancellationToken)

    interface IAsyncDisposable with
        member _.DisposeAsync() = ValueTask(this.DisposeCoreAsync())

    static member LaunchAsync(options: BrowserSessionOptions, cancellationToken: CancellationToken) =
        task {
            ArgumentNullException.ThrowIfNull options
            BrowserProcess.validateBrowserArguments options.BrowserArguments

            if not (File.Exists options.ExecutablePath) then
                invalidArg
                    (nameof options)
                    (String.Concat("Browser executable does not exist: ", options.ExecutablePath))

            let profilePath =
                Path.Combine(Path.GetTempPath(), String.Concat("viset-browser-", Guid.NewGuid().ToString "N"))

            Directory.CreateDirectory profilePath |> ignore
            let mutable browserProcess = None
            let mutable client = None
            let mutable standardOutput = Task.FromResult String.Empty
            let mutable standardError = Task.FromResult String.Empty

            try
                let started =
                    Process.Start(BrowserProcess.createStartInfo options profilePath)
                    |> Option.ofObj
                    |> Option.defaultWith (fun () ->
                        raise (InvalidOperationException "The browser process could not be started."))

                browserProcess <- Some started
                standardOutput <- started.StandardOutput.ReadToEndAsync()
                standardError <- started.StandardError.ReadToEndAsync()

                let! port =
                    BrowserProcess.waitForDevToolsPortAsync
                        started
                        profilePath
                        standardError
                        standardOutput
                        options.StartupTimeout
                        cancellationToken

                let! endpoint = BrowserProcess.findPageEndpointAsync port options.StartupTimeout cancellationToken

                let! connected = CdpClient.ConnectAsync(endpoint, options.CommandTimeout, cancellationToken)

                client <- Some connected
                do! connected.EnablePageAndRuntimeAsync cancellationToken

                return BrowserSession(started, profilePath, connected, standardOutput, standardError)
            with error ->
                let failures = ResizeArray<string>()

                match client with
                | Some connected ->
                    try
                        do! (connected :> IAsyncDisposable).DisposeAsync().AsTask()
                    with cleanupError ->
                        failures.Add(String.Concat("Failed to close CDP: ", cleanupError.Message))
                | None -> ()

                match browserProcess with
                | Some started ->
                    let! processExited, processFailure = BrowserProcess.cleanupProcessAsync started
                    processFailure |> Option.iter failures.Add

                    if processExited then
                        let! diagnosticsResult = BrowserProcess.readProcessDiagnosticsAsync standardError standardOutput

                        match diagnosticsResult with
                        | Ok _ -> ()
                        | Error diagnosticError -> failures.Add diagnosticError

                    try
                        started.Dispose()
                    with cleanupError ->
                        failures.Add(String.Concat("Failed to dispose browser process: ", cleanupError.Message))
                | None -> ()

                let! profileFailure = BrowserProcess.deleteProfileAsync profilePath
                profileFailure |> Option.iter failures.Add

                let message =
                    if failures.Count = 0 then
                        String.Concat(
                            "Browser launch failed; process and temporary profile were cleaned up. Profile: ",
                            profilePath
                        )
                    else
                        String.Concat(
                            "Browser launch failed and cleanup also failed: ",
                            String.Join(" ", failures),
                            " Profile: ",
                            profilePath
                        )

                return raise (BrowserSessionException(message, error))
        }
