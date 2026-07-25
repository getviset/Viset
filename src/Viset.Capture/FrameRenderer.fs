namespace Viset

open System
open System.Threading
open System.Threading.Tasks

type FrameRenderer private (session: BrowserSession, server: FrameServer, readinessTimeout: TimeSpan) =
    let readyExpression = "document.querySelector('[data-frame-ready]') !== null"

    let waitUntilReadyAsync (cancellationToken: CancellationToken) =
        task {
            use timeout = CancellationTokenSource.CreateLinkedTokenSource cancellationToken
            timeout.CancelAfter readinessTimeout
            let mutable ready = false

            try
                while not ready do
                    let! result = session.EvaluateAsync(readyExpression, timeout.Token)

                    match result with
                    | Ok(CdpEvaluationValue.Boolean value) -> ready <- value
                    | Ok _ -> ready <- false
                    | Error error ->
                        raise (
                            InvalidOperationException(
                                String.Concat("Frame readiness evaluation failed: ", error.ToString())
                            )
                        )

                    if not ready then
                        do! Task.Delay(20, timeout.Token)
            with :? OperationCanceledException when not cancellationToken.IsCancellationRequested ->
                raise (
                    TimeoutException(
                        String.Concat(
                            "Frame did not signal data-frame-ready within ",
                            readinessTimeout.TotalMilliseconds,
                            " ms."
                        )
                    )
                )
        }

    member _.CapturePngAsync(cancellationToken: CancellationToken) =
        session.CapturePngAsync cancellationToken

    member private _.WaitUntilReadyAsync(cancellationToken: CancellationToken) = waitUntilReadyAsync cancellationToken

    member _.UpdateAsync(image: CompressedFrame, cancellationToken: CancellationToken) =
        task {
            Media.validateImage image |> ignore

            let! clearResult =
                session.EvaluateAsync(
                    "document.querySelectorAll('[data-frame-ready]').forEach(element => element.removeAttribute('data-frame-ready')); true",
                    cancellationToken
                )

            match clearResult with
            | Error error ->
                raise (InvalidOperationException(String.Concat("Frame readiness reset failed: ", error.ToString())))
            | Ok _ -> ()

            server.UpdateImage image

            let! updateResult = session.EvaluateAsync("window.visetFrame.update().then(() => true)", cancellationToken)

            match updateResult with
            | Error error -> raise (InvalidOperationException(String.Concat("Frame update failed: ", error.ToString())))
            | Ok _ -> do! waitUntilReadyAsync cancellationToken
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            (server :> IAsyncDisposable).DisposeAsync()

    static member StartAsync
        (
            session: BrowserSession,
            frameSource: FrameSource,
            device: Device,
            initialImage: CompressedFrame,
            readinessTimeout: TimeSpan,
            cancellationToken: CancellationToken
        ) =
        task {
            ArgumentNullException.ThrowIfNull session

            if readinessTimeout <= TimeSpan.Zero then
                invalidArg (nameof readinessTimeout) "Frame readiness timeout must be positive."

            let frame =
                device.Frame
                |> Option.defaultWith (fun () ->
                    invalidArg (nameof device) "The selected device has no frame dimensions.")

            Media.validateImage initialImage |> ignore
            let server = FrameServer.Start(frameSource, device, initialImage)

            try
                do!
                    session.ConfigureEmulationAsync(
                        frame.Width,
                        frame.Height,
                        device.DeviceScale,
                        false,
                        false,
                        cancellationToken
                    )

                do! session.SetTransparentBackgroundAsync cancellationToken
                do! session.NavigateAsync(server.Url, cancellationToken)
                let renderer = FrameRenderer(session, server, readinessTimeout)
                do! renderer.WaitUntilReadyAsync cancellationToken
                return renderer
            with error ->
                do! (server :> IAsyncDisposable).DisposeAsync().AsTask()
                return raise error
        }
